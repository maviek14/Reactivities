using Application.Core;
using Application.Interfaces;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Activities;

public class List
{
    public class Query : IRequest<Result<List<ActivityDto>>> { }

    public class Handler : IRequestHandler<Query, Result<List<ActivityDto>>>
    {
        private readonly DataContext _context;
        private readonly IMapper mapper;
        private readonly IUserAccessor userAccessor;

        public Handler(DataContext context, IMapper mapper, IUserAccessor userAccessor)
        {
            _context = context;
            this.mapper = mapper;
            this.userAccessor = userAccessor;
        }

        public async Task<Result<List<ActivityDto>>> Handle(Query request, CancellationToken token)
        {
            var activities = await _context.Activities
                .ProjectTo<ActivityDto>(mapper.ConfigurationProvider,
                    new { currentUsername = userAccessor.GetUsername() })
                .ToListAsync(token);

            return Result<List<ActivityDto>>.Success(activities);
        }
    }
}
