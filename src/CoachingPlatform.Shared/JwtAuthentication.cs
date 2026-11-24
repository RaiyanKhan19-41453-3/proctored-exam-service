using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CoachingPlatform.Shared;

/// <summary>
/// Shared JWT validation for every product microservice: the same key/issuer/audience as the
/// monolith, so a token issued by the main API is accepted here with no shared session.
/// </summary>
public static class JwtAuthentication
{
    public static IServiceCollection AddProductJwt(this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"]
                  ?? "dev-only-exam-signing-key-change-this-please-1234";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "coaching-platform",
                    ValidAudience = configuration["Jwt:Audience"] ?? "coaching-web",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
            });
        services.AddAuthorization();
        return services;
    }
}
