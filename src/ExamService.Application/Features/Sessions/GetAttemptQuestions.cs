using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Sessions;

public sealed record GetAttemptQuestions(Guid AttemptId) : IRequest<IReadOnlyList<AttemptQuestionDto>>, IRequiresTenant;

public sealed record AttemptQuestionDto(Guid QuestionId, string Text, string? OptionsJson, decimal Marks, int Order);

public sealed class GetAttemptQuestionsHandler(IAppDbContext db)
    : IRequestHandler<GetAttemptQuestions, IReadOnlyList<AttemptQuestionDto>>
{
    public async Task<IReadOnlyList<AttemptQuestionDto>> Handle(GetAttemptQuestions request, CancellationToken ct)
        => await (from link in db.Set<AttemptQuestion>()
                  join q in db.Set<Question>() on link.QuestionId equals q.Id
                  where link.AttemptId == request.AttemptId
                  orderby link.Order
                  select new AttemptQuestionDto(q.Id, q.Text,
                      link.ShuffledOptionsJson ?? q.OptionsJson, q.Marks, link.Order)).ToListAsync(ct);
}

public sealed record ListAnomalyFlags(Guid ExamId) : IRequest<IReadOnlyList<FlagDto>>, IRequiresTenant;

public sealed record FlagDto(Guid Id, FlagKind Kind, decimal Score, string? DetailJson, bool Reviewed);

public sealed class ListAnomalyFlagsHandler(IAppDbContext db) : IRequestHandler<ListAnomalyFlags, IReadOnlyList<FlagDto>>
{
    public async Task<IReadOnlyList<FlagDto>> Handle(ListAnomalyFlags request, CancellationToken ct)
        => await db.Set<AnomalyFlag>().AsNoTracking()
            .Where(f => f.ExamId == request.ExamId)
            .OrderByDescending(f => f.Score)
            .Select(f => new FlagDto(f.Id, f.Kind, f.Score, f.DetailJson, f.Reviewed)).ToListAsync(ct);
}

public sealed record ReviewFlag(Guid FlagId, bool Reviewed) : IRequest<bool>, IRequiresTenant;

public sealed class ReviewFlagHandler(IAppDbContext db) : IRequestHandler<ReviewFlag, bool>
{
    public async Task<bool> Handle(ReviewFlag request, CancellationToken ct)
    {
        var flag = await db.Set<AnomalyFlag>().FirstOrDefaultAsync(f => f.Id == request.FlagId, ct)
                   ?? throw new AppException("Flag not found.", 404);
        flag.Reviewed = request.Reviewed;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
