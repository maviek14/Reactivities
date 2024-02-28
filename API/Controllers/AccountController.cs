using API.DTOs;
using API.Services;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly UserManager<AppUser> userManager;
    private readonly TokenService tokenService;
    private readonly IConfiguration configuration;
    private readonly HttpClient httpClient;

    public AccountController(UserManager<AppUser> userManager, TokenService tokenService, IConfiguration configuration)
    {
        this.userManager = userManager;
        this.tokenService = tokenService;
        this.configuration = configuration;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://graph.facebook.com")
        };
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var user = await userManager.Users.Include(p => p.Photos)
            .FirstOrDefaultAsync(x => x.Email == loginDto.Email);

        if (user == null) return Unauthorized();

        var result = await userManager.CheckPasswordAsync(user, loginDto.Password);

        if (result)
        {
            await SetRefreshToken(user);
            return CreateUserObject(user);
        }

        return Unauthorized();
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        if (await userManager.Users.AnyAsync(x => x.Email == registerDto.Email))
        {
            ModelState.AddModelError("email", "Email taken");
            return ValidationProblem();
        }

        if (await userManager.Users.AnyAsync(x => x.UserName == registerDto.Username))
        {
            ModelState.AddModelError("username", "Username taken");
            return ValidationProblem();
        }

        var user = new AppUser
        {
            DisplayName = registerDto.DisplayName,
            Email = registerDto.Email,
            UserName = registerDto.Username
        };

        var result = await userManager.CreateAsync(user, registerDto.Password);

        if (result.Succeeded)
        {
            await SetRefreshToken(user);
            return CreateUserObject(user);
        }

        return BadRequest(result.Errors);
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var user = await userManager.Users.Include(p => p.Photos)
            .FirstOrDefaultAsync(x => x.Email == User.FindFirstValue(ClaimTypes.Email));

        await SetRefreshToken(user);
        return CreateUserObject(user);
    }

    [AllowAnonymous]
    [HttpPost("fbLogin")]
    public async Task<ActionResult<UserDto>> FacebookLogin(string accessToken)
    {
        var fbVerifyKeys = configuration["Facebook:AppId"] + "|" + configuration["Facebook:ApiSecret"];

        var verifyTokenResponse = await httpClient
            .GetAsync($"debug_token?input_token={accessToken}&access_token={fbVerifyKeys}");

        if (!verifyTokenResponse.IsSuccessStatusCode) return Unauthorized();

        var fbUrl = $"me?access_token={accessToken}&fields=name,email,picture.width(100).height(100)";

        var fbInfo = await httpClient.GetFromJsonAsync<FacebookDto>(fbUrl);

        var user = await userManager.Users.Include(p => p.Photos).FirstOrDefaultAsync(x => x.Email == fbInfo.Email);

        if (user != null) return CreateUserObject(user);

        user = new AppUser
        {
            DisplayName = fbInfo.Name,
            Email = fbInfo.Email,
            UserName = fbInfo.Email,
            Photos = new List<Photo>
            {
                new Photo
                {
                    Id="fb_"+fbInfo.Id,
                    Url=fbInfo.Picture.Data.Url,
                    IsMain=true
                }
            }
        };

        var result = await userManager.CreateAsync(user);

        if (!result.Succeeded) return BadRequest("Problem creating user account");

        await SetRefreshToken(user);
        return CreateUserObject(user);
    }

    [Authorize]
    [HttpPost("refreshToken")]
    public async Task<ActionResult<UserDto>> RefreshToken()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        var user = await userManager.Users
            .Include(r => r.RefreshTokens)
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(x => x.UserName == User.FindFirstValue(ClaimTypes.Name));

        if (user == null) return Unauthorized();

        var oldToken = user.RefreshTokens.SingleOrDefault(x => x.Token == refreshToken);

        if (oldToken != null && !oldToken.IsActive) return Unauthorized();

        if (oldToken != null) oldToken.Revoked = DateTime.UtcNow;

        return CreateUserObject(user);
    }

    private async Task SetRefreshToken(AppUser user)
    {
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshTokens.Add(refreshToken);

        await userManager.UpdateAsync(user);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Expires = DateTime.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("refreshToken", refreshToken.Token, cookieOptions);
    }

    private UserDto CreateUserObject(AppUser user)
    {
        return new UserDto
        {
            DisplayName = user.DisplayName,
            Image = user?.Photos?.FirstOrDefault(x => x.IsMain)?.Url,
            Token = tokenService.CreateToken(user),
            Username = user.UserName
        };
    }
}