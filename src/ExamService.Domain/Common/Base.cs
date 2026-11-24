namespace ExamService.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

public interface ITenantEntity { Guid TenantId { get; set; } }
public interface IAuditable { DateTimeOffset CreatedAt { get; set; } Guid? CreatedBy { get; set; } }
public interface IConcurrent { byte[] RowVersion { get; set; } }

public abstract class TenantEntity : Entity, ITenantEntity, IAuditable, IConcurrent
{
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum ExamStatus { Draft, Published, Ongoing, Completed, Archived }
public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, ShortAnswer, Essay }
public enum AttemptStatus { InProgress, Submitted, UnderReview, Graded, Disqualified }
public enum Severity { None, Low, Medium, High, Critical }
public enum EventType { Started, Submitted, TabBlur, TabFocus, Copy, Paste, FullscreenExit, CameraBlocked,
    MicBlocked, FaceAbsent, MultipleFaces, FaceCovered, AudioAnomaly, Disconnected, Reconnected, ForcedSubmit }
public enum ReviewDecision { Pending, Approved, Flagged, Rejected }
public enum AssignmentStatus { Assigned, Verified, InProgress, Completed, Revoked }
public enum EvidenceKind { Snapshot, CameraChunk, AudioChunk, ScreenChunk, IdentitySelfie, RoomScan }
public enum ConsentStatus { Pending, Accepted, Declined }
public enum FlagKind { AnswerSimilarity, SharedDevice, SharedIp, GeoAnomaly, KioskRequired }
public enum ClientMode { Browser, Kiosk }

public sealed record ProctorEventView(EventType Type, Severity Severity, DateTimeOffset At, string? Detail);
