using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamService.Application.Features.Identity;

public sealed record CreateAssignment(Guid ExamId, Guid CandidateId, string CandidateName,
    string? CandidateEmail, string? IdentityPhotoKey) : IRequest<AssignmentResponse>, IRequiresTenant;

public sealed record AssignmentResponse(Guid AssignmentId, string AttemptToken, DateTimeOffset ExpiresAt);

public sealed class CreateAssignmentValidator : AbstractValidator<CreateAssignment>
{
    public CreateAssignmentValidator()
    {
        RuleFor(x => x.ExamId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.CandidateName).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateAssignmentHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<CreateAssignment, AssignmentResponse>
{
    public async Task<AssignmentResponse> Handle(CreateAssignment request, CancellationToken ct)
    {
        if (!await db.Set<Exam>().AnyAsync(e => e.Id == request.ExamId, ct))
            throw new AppException("Exam not found.", 404);
        if (await db.Set<ExamAssignment>().AnyAsync(a =>
                a.ExamId == request.ExamId && a.CandidateId == request.CandidateId, ct))
            throw new AppException("Candidate is already assigned to this exam.", 409);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var assignment = new ExamAssignment
        {
            TenantId = current.TenantId, ExamId = request.ExamId,
            CandidateId = request.CandidateId, CandidateName = request.CandidateName,
            CandidateEmail = request.CandidateEmail, IdentityPhotoKey = request.IdentityPhotoKey,
            AttemptTokenHash = Hash(token),
            TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };
        db.Set<ExamAssignment>().Add(assignment);
        await db.SaveChangesAsync(ct);
        return new AssignmentResponse(assignment.Id, token, assignment.TokenExpiresAt.Value);
    }

    internal static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record AcceptConsent(Guid AssignmentId, bool Accepted, string? Ip)
    : IRequest<bool>, IRequiresTenant;

public sealed class AcceptConsentHandler(IAppDbContext db) : IRequestHandler<AcceptConsent, bool>
{
    public async Task<bool> Handle(AcceptConsent request, CancellationToken ct)
    {
        var assignment = await db.Set<ExamAssignment>().FirstOrDefaultAsync(a => a.Id == request.AssignmentId, ct)
                         ?? throw new AppException("Assignment not found.", 404);
        if (!request.Accepted) throw new AppException("Consent is required to take this exam.", 403);
        assignment.Consent = ConsentStatus.Accepted;
        assignment.ConsentAt = DateTimeOffset.UtcNow;
        assignment.ConsentIp = request.Ip;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record VerifyIdentity(Guid AssignmentId, byte[] Selfie)
    : IRequest<IdentityVerificationDto>, IRequiresTenant;

public sealed record IdentityVerificationDto(bool Verified, decimal? MatchScore, string Message);

public sealed class VerifyIdentityHandler(IAppDbContext db, IObjectStorage storage, HttpClient http, IConfiguration config)
    : IRequestHandler<VerifyIdentity, IdentityVerificationDto>
{
    public async Task<IdentityVerificationDto> Handle(VerifyIdentity request, CancellationToken ct)
    {
        var assignment = await db.Set<ExamAssignment>().FirstOrDefaultAsync(a => a.Id == request.AssignmentId, ct)
                         ?? throw new AppException("Assignment not found.", 404);
        if (assignment.IdentityPhotoKey is null)
            return new IdentityVerificationDto(false, null, "No identity photo on file; manual verification required.");
        if (!await storage.ExistsAsync(assignment.IdentityPhotoKey, ct))
            return new IdentityVerificationDto(false, null, "Identity photo is missing.");

        var key = $"{assignment.TenantId:N}/identity/{assignment.Id:N}/{Guid.NewGuid():N}.jpg";
        var url = await storage.PresignUploadAsync(key, "image/jpeg", TimeSpan.FromMinutes(5), ct);
        using var content = new ByteArrayContent(request.Selfie);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        var put = await http.PutAsync(url, content, ct);
        if (!put.IsSuccessStatusCode)
            return new IdentityVerificationDto(false, null, "Could not upload verification selfie.");

        db.Set<Domain.Entities.Evidence>().Add(new Domain.Entities.Evidence
        {
            TenantId = assignment.TenantId, Kind = EvidenceKind.IdentitySelfie,
            StorageKey = key, ContentType = "image/jpeg", SizeBytes = request.Selfie.Length,
            CapturedAt = DateTimeOffset.UtcNow,
            DetailJson = JsonSerializer.Serialize(new { assignment.Id })
        });

        var score = await CompareFacesAsync(assignment.IdentityPhotoKey, key, ct);
        var verified = score.HasValue && score >= 70m;
        assignment.FaceMatchScore = score;
        if (verified)
        {
            assignment.IdentityVerifiedAt = DateTimeOffset.UtcNow;
            assignment.Status = AssignmentStatus.Verified;
        }
        await db.SaveChangesAsync(ct);
        return new IdentityVerificationDto(verified, score,
            verified ? "Identity verified." : "Face match below threshold; manual review required.");
    }

    private async Task<decimal?> CompareFacesAsync(string referenceKey, string selfieKey, CancellationToken ct)
    {
        var endpoint = config["AI:FaceEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) return null; // never claim a match without a real engine
        try
        {
            var referenceUrl = await storage.PresignDownloadAsync(referenceKey, TimeSpan.FromMinutes(5), ct);
            var selfieUrl = await storage.PresignDownloadAsync(selfieKey, TimeSpan.FromMinutes(5), ct);
            var response = await http.PostAsJsonAsync(endpoint, new { referenceUrl, selfieUrl }, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("matchScore").GetDecimal();
        }
        catch { return null; }
    }
}
