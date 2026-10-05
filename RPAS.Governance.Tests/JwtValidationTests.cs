using Microsoft.IdentityModel.Protocols;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace RPAS.Governance.Tests;

/// <summary>
/// AMD-2026-10-01-0003 (and the human-token rule of 0007) through the REAL JwtBearer pipeline, with the production
/// validation parameters from RpasAuthentication. Tokens are signed locally; only the OIDC discovery fetch is replaced
/// by a static configuration. This is what a stand-in header handler cannot show: signature, issuer, audience and lifetime
/// are enforced, and the claims an Entra app-only token and a delegated user token really carry are told apart.
/// What it cannot show is that YOUR tenant issues these claims; verify that against the real tenant.
/// </summary>
public sealed class JwtValidationTests : IDisposable
{
    private const string Issuer = "https://login.example.test/00000000-0000-0000-0000-000000000000/v2.0";
    private const string Audience = "api://rpas-governance-test";
    private const string Human = "11111111-1111-1111-1111-111111111111";

    private static readonly RSA SigningRsa = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(SigningRsa) { KeyId = "test-key" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "test-key" }; // same kid, different key

    private readonly JwtFactory _factory = new();

    public JwtValidationTests() => _factory.EnsureDatabase();

    public void Dispose() => _factory.Dispose();

    /// <summary>The real API with the real RpasAuthentication, pointed at a fixed issuer and key instead of live discovery.</summary>
    private sealed class JwtFactory() : ApiFactory(useTestAuth: false, new Dictionary<string, string?>
    {
        ["Authentication:Authority"] = Issuer,
        ["Authentication:Audience"] = Audience,
        ["Governance:Petitioners:sidpa:Scopes"] = "heal,edit-contract",
    })
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
        }
    }

    private static string Token(
        IEnumerable<KeyValuePair<string, object>> claims,
        string issuer = Issuer, string audience = Audience, SecurityKey? key = null,
        DateTime? expires = null, DateTime? notBefore = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
            Claims = claims.ToDictionary(c => c.Key, c => c.Value),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>What an Entra v2 client-credentials (app-only) token carries.</summary>
    private static Dictionary<string, object> AppOnly(string azp = "sidpa") => new()
    {
        ["azp"] = azp, ["azpacr"] = "1", ["roles"] = new[] { "Petitioner" },
        ["oid"] = "99999999-9999-9999-9999-999999999999", // the service principal's own object id
        ["idtyp"] = "app", ["ver"] = "2.0",
    };

    /// <summary>What an Entra v2 delegated (signed-in user) token carries.</summary>
    private static Dictionary<string, object> User(string azp = "sidpa", string oid = Human) => new()
    {
        ["azp"] = azp, ["scp"] = "access_as_user", ["oid"] = oid, ["ver"] = "2.0",
    };

    private HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private async Task<HttpStatusCode> List(string? token)
    {
        using var client = Client(token);
        return (await client.GetAsync("/Rituals/definitions")).StatusCode;
    }

    private async Task<HttpStatusCode> Issue(string token, string ritual)
    {
        using var client = Client(token);
        return (await client.PostAsJsonAsync("/Tokens/issue", new { RitualType = ritual, EntityId = "mod-1" })).StatusCode;
    }

    // ---- what the validation accepts ---------------------------------------------------------------------------------

    [Fact]
    public async Task AValidAppOnlyToken_IsAccepted() =>
        Assert.Equal(HttpStatusCode.OK, await List(Token(AppOnly())));

    [Fact]
    public async Task AValidUserToken_IsAccepted() =>
        Assert.Equal(HttpStatusCode.OK, await List(Token(User())));

    [Fact]
    public async Task AV1StyleToken_IdentifiedByAppid_IsAccepted() =>
        Assert.Equal(HttpStatusCode.OK, await List(Token(new Dictionary<string, object> { ["appid"] = "sidpa", ["roles"] = new[] { "Petitioner" } })));

    [Fact]
    public async Task AGenericClientCredentialsToken_IdentifiedByClientId_IsAccepted() =>
        Assert.Equal(HttpStatusCode.OK, await List(Token(new Dictionary<string, object> { ["client_id"] = "sidpa" })));

    // ---- what it rejects ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task NoToken_IsRejected() => Assert.Equal(HttpStatusCode.Unauthorized, await List(null));

    [Fact]
    public async Task GarbageToken_IsRejected() => Assert.Equal(HttpStatusCode.Unauthorized, await List("not.a.jwt"));

    [Fact]
    public async Task WrongAudience_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await List(Token(AppOnly(), audience: "api://some-other-api")));

    [Fact]
    public async Task WrongIssuer_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await List(Token(AppOnly(), issuer: "https://login.example.test/another-tenant/v2.0")));

    [Fact]
    public async Task ExpiredToken_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await List(Token(AppOnly(), notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1))));

    [Fact]
    public async Task TokenNotYetValid_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await List(Token(AppOnly(), notBefore: DateTime.UtcNow.AddHours(1), expires: DateTime.UtcNow.AddHours(2))));

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await List(Token(AppOnly(), key: OtherKey)));

    [Fact]
    public async Task UnsignedToken_IsRejected()
    {
        // alg "none": a classic bypass; RequireSignedTokens must refuse it.
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Issuer, ["aud"] = Audience, ["azp"] = "sidpa",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        });
        var unsigned = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(payload)}.";
        Assert.Equal(HttpStatusCode.Unauthorized, await List(unsigned));
    }

    [Fact]
    public async Task ATamperedPayload_IsRejected()
    {
        var parts = Token(AppOnly("sidpa")).Split('.');
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var forgedPayload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Issuer, ["aud"] = Audience, ["azp"] = "someone-else",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, await List($"{parts[0]}.{B64(forgedPayload)}.{parts[2]}"));
    }

    [Fact]
    public async Task AValidTokenWithNoPetitionerClaim_IsAuthenticatedButForbidden() =>
        Assert.Equal(HttpStatusCode.Forbidden, await List(Token(new Dictionary<string, object> { ["roles"] = new[] { "Petitioner" } })));

    // ---- the human-only scope with real claim shapes (AMD-0007) ---------------------------------------------------------

    [Fact]
    public async Task EditContract_IsRefused_ToARealAppOnlyToken_EvenThoughItCarriesAnOid() =>
        Assert.Equal(HttpStatusCode.Forbidden, await Issue(Token(AppOnly()), "EditContract"));

    [Fact]
    public async Task EditContract_IsIssued_ToARealDelegatedUserToken() =>
        Assert.Equal(HttpStatusCode.Created, await Issue(Token(User()), "EditContract"));

    [Fact]
    public async Task EditContract_IsRefused_ToAUserTokenWithNoObjectId() =>
        Assert.Equal(HttpStatusCode.Forbidden, await Issue(Token(new Dictionary<string, object> { ["azp"] = "sidpa", ["scp"] = "access_as_user" }), "EditContract"));

    [Fact]
    public async Task HealToken_IsIssued_ToAnAppOnlyToken() =>
        Assert.Equal(HttpStatusCode.Created, await Issue(Token(AppOnly()), "Heal"));

    [Fact]
    public async Task APetitionerIsTheTokensAzp_NotAnythingTheCallerCanChooseInTheBody()
    {
        // A token for another application gets no scopes: grants are per authenticated petitioner.
        Assert.Equal(HttpStatusCode.Forbidden, await Issue(Token(AppOnly("some-other-app")), "Heal"));
    }
}
