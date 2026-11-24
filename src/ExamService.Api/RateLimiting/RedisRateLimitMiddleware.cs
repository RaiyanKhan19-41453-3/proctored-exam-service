using StackExchange.Redis;

namespace ExamService.Api.RateLimiting;

/// <summary>Global per-tenant quota shared by every replica. Fails open if Redis is down.</summary>
public sealed class RedisRateLimitMiddleware(
    RequestDelegate next, IConfiguration configuration, IConnectionMultiplexer? redis = null)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (redis is not null)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
            {
                var tenant = context.User?.FindFirst("tenant_id")?.Value ?? "anon";
                var limit = int.TryParse(configuration["RateLimit:PerMinute"], out var configured) ? configured : 600;
                var window = $"{DateTimeOffset.UtcNow:yyyyMMddHHmm}";
                var key = $"exam-ratelimit:{tenant}:{window}";
                try
                {
                    var db = redis.GetDatabase();
                    var count = await db.StringIncrementAsync(key);
                    if (count == 1) await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
                    if (count > limit)
                    {
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return;
                    }
                }
                catch (Exception) { /* in-process limiter still applies */ }
            }
        }
        await next(context);
    }
}
