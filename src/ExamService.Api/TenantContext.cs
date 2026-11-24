using CoachingPlatform.Shared;
using Microsoft.AspNetCore.Authorization;
using ExamService.Application.Common;
using ExamService.Infrastructure.Persistence;


namespace ExamService.Api;

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private HttpContext? Http => accessor.HttpContext;

    public Guid TenantId
    {
        get
        {
            var claim = Http?.User.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(claim, out var id)) return id;
            if (Guid.TryParse(Http?.Request.Headers["X-Tenant-Id"], out var header)) return header;
            return Guid.Empty;
        }
    }

    public Guid? UserId
    {
        get
        {
            var value = Http?.User.FindFirst("sub")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserEmail => Http?.User.FindFirst("email")?.Value;

    public IReadOnlyList<string> Roles => Http?.User.FindAll("role").Select(c => c.Value).ToArray() ?? [];
}

public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (KeyNotFoundException ex) { await Write(context, 404, ex.Message); }
        catch (InvalidOperationException ex) { await Write(context, 409, ex.Message); }
        catch (UnauthorizedAccessException ex) { await Write(context, 401, ex.Message); }
        catch (AppException ex) { await Write(context, ex.Status, ex.Message); }
        catch (FluentValidation.ValidationException ex)
        {
            await context.Response.WriteAsJsonAsync(new
            {
                status = 400,
                errors = ex.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
            });
            context.Response.StatusCode = 400;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error on {Path}", context.Request.Path);
            await Write(context, 500, "Internal server error");
        }
    }

    private static Task Write(HttpContext context, int status, string message)
    {
        context.Response.Clear();
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { status, message });
    }
}
