using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RPAS.Governance.Api.Security;

/// <summary>
/// Petitioner authentication (AMD-2026-10-01-0003).
///
/// Every courthouse endpoint requires an authenticated petitioner. With Authentication:Authority
/// configured (Microsoft Entra ID client-credentials tokens, or any OIDC issuer) JWT bearer validation
/// is used. With NO authority configured the API fails closed: every request is rejected with 401 until
/// authentication is configured. There is deliberately no "anonymous" or development bypass.
/// </summary>
public static class RpasAuthentication
{
    public const string DenyAllScheme = "RpasDenyAll";

    // Claims that carry the calling application's identity, in order of preference:
    // Entra v2 tokens ("azp"), Entra v1 tokens ("appid"), generic OAuth client credentials ("client_id").
    private static readonly string[] PetitionerClaimTypes = ["azp", "appid", "client_id"];

    public static IServiceCollection AddRpasAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];

        if (!string.IsNullOrWhiteSpace(authority))
        {
            services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.MapInboundClaims = false; // keep raw claim names such as "azp"
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateLifetime = true;
                    options.TokenValidationParameters.RequireSignedTokens = true;
                });
        }
        else
        {
            services.AddAuthentication(DenyAllScheme)
                .AddScheme<AuthenticationSchemeOptions, DenyAllHandler>(DenyAllScheme, _ => { });
        }

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Returns the authenticated petitioner's identity, or null when the principal carries none.
    /// </summary>
    public static string? GetPetitionerId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        foreach (var claimType in PetitionerClaimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the named human behind the request, or null when the caller is an automated petitioner
    /// (AMD-2026-10-01-0007). Only a delegated user token counts: it carries the "scp" claim and the user's
    /// object id ("oid"). App-only client-credentials tokens carry "roles" instead of "scp" (and may say
    /// idtyp=app), so a petitioner's own credentials can never satisfy a human-only scope.
    /// </summary>
    public static string? GetHumanId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (string.Equals(user.FindFirst("idtyp")?.Value, "app", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.FindFirst("scp")?.Value))
        {
            return null;
        }

        var oid = user.FindFirst("oid")?.Value;
        return string.IsNullOrWhiteSpace(oid) ? null : oid;
    }

    private sealed class DenyAllHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Logger.LogCritical("RPAS authentication is not configured (Authentication:Authority is empty); rejecting request.");
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
