using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExamService.Infrastructure.Persistence.Configurations;

public abstract class TenantConfig<T>(string table) : IEntityTypeConfiguration<T> where T : TenantEntity
{
    public virtual void Configure(EntityTypeBuilder<T> b)
    {
        b.ToTable(table);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.TenantId).IsRequired();
        b.Property(e => e.CreatedAt).IsRequired();
        b.Property(e => e.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
    }
}

public sealed class ExamConfig() : TenantConfig<Exam>("Exams")
{
    public override void Configure(EntityTypeBuilder<Exam> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.TenantId, e.Status });
        b.Property(e => e.Title).HasMaxLength(200).IsRequired();
    }
}

public sealed class QuestionConfig() : TenantConfig<Question>("Questions")
{
    public override void Configure(EntityTypeBuilder<Question> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.ExamId, e.Order });
    }
}

public sealed class AttemptConfig() : TenantConfig<ExamAttempt>("Attempts")
{
    public override void Configure(EntityTypeBuilder<ExamAttempt> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.ExamId, e.CandidateId });
    }
}

public sealed class AnswerConfig() : TenantConfig<Answer>("Answers")
{
    public override void Configure(EntityTypeBuilder<Answer> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.QuestionId }).IsUnique();
    }
}

public sealed class EventConfig() : TenantConfig<ProctorEvent>("ProctorEvents")
{
    public override void Configure(EntityTypeBuilder<ProctorEvent> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.OccurredAt });
    }
}

public sealed class RecordingConfig() : TenantConfig<Recording>("Recordings")
{
    public override void Configure(EntityTypeBuilder<Recording> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.Kind });
    }
}

public sealed class ReportConfig() : TenantConfig<EvaluationReport>("Reports")
{
    public override void Configure(EntityTypeBuilder<EvaluationReport> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.ExamId, e.Decision });
        b.HasIndex(e => e.AttemptId).IsUnique();
    }
}

public sealed class OutboxConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.HasIndex(e => e.ProcessedAt);
    }
}

public sealed class AssignmentConfig() : TenantConfig<ExamAssignment>("Assignments")
{
    public override void Configure(EntityTypeBuilder<ExamAssignment> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.ExamId, e.CandidateId }).IsUnique();
        b.HasIndex(e => e.AttemptTokenHash).IsUnique();
    }
}

public sealed class EvidenceConfig() : TenantConfig<Evidence>("Evidence")
{
    public override void Configure(EntityTypeBuilder<Evidence> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.Kind, e.CapturedAt });
    }
}

public sealed class AttemptQuestionConfig() : TenantConfig<AttemptQuestion>("AttemptQuestions")
{
    public override void Configure(EntityTypeBuilder<AttemptQuestion> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.Order }).IsUnique();
    }
}

public sealed class AttemptContextConfig() : TenantConfig<AttemptContextRecord>("AttemptContexts")
{
    public override void Configure(EntityTypeBuilder<AttemptContextRecord> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.AttemptId, e.CapturedAt });
        b.HasIndex(e => e.DeviceFingerprint);
        b.HasIndex(e => e.IpAddress);
    }
}

public sealed class AnomalyFlagConfig() : TenantConfig<AnomalyFlag>("AnomalyFlags")
{
    public override void Configure(EntityTypeBuilder<AnomalyFlag> b)
    {
        base.Configure(b);
        b.HasIndex(e => new { e.ExamId, e.Kind });
    }
}
