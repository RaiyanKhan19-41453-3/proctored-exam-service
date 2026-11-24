using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Exams;

public sealed record CreateExam(string Title, string? Description, DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc, int DurationMinutes, decimal TotalMarks, bool RequireCamera,
    bool RequireMicrophone, bool LockTab, bool DisableCopyPaste, bool AiEvaluationEnabled,
    string? Instructions, bool RequireIdentityVerification = false) : IRequest<Guid>, IRequiresTenant;

public sealed class CreateExamValidator : AbstractValidator<CreateExam>
{
    public CreateExamValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 600);
        RuleFor(x => x.TotalMarks).GreaterThan(0);
    }
}

public sealed class CreateExamHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<CreateExam, Guid>
{
    public async Task<Guid> Handle(CreateExam request, CancellationToken ct)
    {
        var exam = new Exam
        {
            TenantId = current.TenantId, Title = request.Title, Description = request.Description,
            StartUtc = request.StartUtc, EndUtc = request.EndUtc, DurationMinutes = request.DurationMinutes,
            TotalMarks = request.TotalMarks, RequireCamera = request.RequireCamera,
            RequireMicrophone = request.RequireMicrophone, LockTab = request.LockTab,
            DisableCopyPaste = request.DisableCopyPaste, AiEvaluationEnabled = request.AiEvaluationEnabled,
            Instructions = request.Instructions, Status = ExamStatus.Draft,
            RequireIdentityVerification = request.RequireIdentityVerification
        };
        db.Set<Exam>().Add(exam);
        await db.SaveChangesAsync(ct);
        return exam.Id;
    }
}

public sealed record PublishExam(Guid ExamId) : IRequest<bool>, IRequiresTenant;

public sealed class PublishExamHandler(IAppDbContext db) : IRequestHandler<PublishExam, bool>
{
    public async Task<bool> Handle(PublishExam request, CancellationToken ct)
    {
        var exam = await db.Set<Exam>().FirstOrDefaultAsync(e => e.Id == request.ExamId, ct)
                   ?? throw new KeyNotFoundException("Exam not found.");
        if (!await db.Set<Question>().AnyAsync(q => q.ExamId == exam.Id, ct))
            throw new InvalidOperationException("Exam must have at least one question.");
        exam.Status = ExamStatus.Published;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record AddQuestion(Guid ExamId, QuestionType Type, string Text, string? OptionsJson,
    string? CorrectAnswerJson, decimal Marks, int Order) : IRequest<Guid>, IRequiresTenant;

public sealed class AddQuestionHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<AddQuestion, Guid>
{
    public async Task<Guid> Handle(AddQuestion request, CancellationToken ct)
    {
        if (!await db.Set<Exam>().AnyAsync(e => e.Id == request.ExamId, ct))
            throw new KeyNotFoundException("Exam not found.");
        var question = new Question
        {
            TenantId = current.TenantId, ExamId = request.ExamId, Type = request.Type,
            Text = request.Text, OptionsJson = request.OptionsJson,
            CorrectAnswerJson = request.CorrectAnswerJson, Marks = request.Marks, Order = request.Order
        };
        db.Set<Question>().Add(question);
        await db.SaveChangesAsync(ct);
        return question.Id;
    }
}

public sealed record GetExam(Guid ExamId) : IRequest<ExamDto?>, IRequiresTenant;

public sealed record ExamDto(Guid Id, string Title, string? Description, ExamStatus Status,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, int DurationMinutes, decimal TotalMarks,
    bool RequireCamera, bool RequireMicrophone, bool LockTab, bool DisableCopyPaste,
    string? Instructions, IReadOnlyList<QuestionDto> Questions);

public sealed record QuestionDto(Guid Id, QuestionType Type, string Text, string? OptionsJson, decimal Marks);

public sealed class GetExamHandler(IAppDbContext db) : IRequestHandler<GetExam, ExamDto?>
{
    public async Task<ExamDto?> Handle(GetExam request, CancellationToken ct)
    {
        var exam = await db.Set<Exam>().FirstOrDefaultAsync(e => e.Id == request.ExamId, ct);
        if (exam is null) return null;
        var questions = await db.Set<Question>().Where(q => q.ExamId == exam.Id)
            .OrderBy(q => q.Order)
            .Select(q => new QuestionDto(q.Id, q.Type, q.Text, q.OptionsJson, q.Marks)).ToListAsync(ct);
        return new ExamDto(exam.Id, exam.Title, exam.Description, exam.Status, exam.StartUtc,
            exam.EndUtc, exam.DurationMinutes, exam.TotalMarks, exam.RequireCamera,
            exam.RequireMicrophone, exam.LockTab, exam.DisableCopyPaste, exam.Instructions, questions);
    }
}
