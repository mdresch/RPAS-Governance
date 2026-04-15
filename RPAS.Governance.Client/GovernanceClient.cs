using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using RPAS.Governance.Client.DTOs;
using RPAS.Governance.Client.Exceptions;

namespace RPAS.Governance.Client;

public interface IGovernanceClient
{
    Task<GovernanceResult> CreateBusinessCaseAsync(BusinessCaseCreateRequest request);
    Task<GovernanceResult> ApproveBusinessCaseAsync(string entityId, string justification);
    Task<GovernanceResult> RejectBusinessCaseAsync(string entityId, string reason);
}

public class GovernanceClient : IGovernanceClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GovernanceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<GovernanceResult> CreateBusinessCaseAsync(BusinessCaseCreateRequest request)
    {
        return await SendPetitionAsync(new ValidationPetitionDto("BusinessCase", request.Id, "Create", request));
    }

    public async Task<GovernanceResult> ApproveBusinessCaseAsync(string entityId, string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException("Justification is mandatory for approval rituals.", nameof(justification));
        }

        return await SendPetitionAsync(new ValidationPetitionDto("BusinessCase", entityId, "MarkApproved", new { justification }));
    }

    public async Task<GovernanceResult> RejectBusinessCaseAsync(string entityId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is mandatory for rejection rituals.", nameof(reason));
        }

        return await SendPetitionAsync(new ValidationPetitionDto("BusinessCase", entityId, "MarkRejected", new { reason }));
    }

    private async Task<GovernanceResult> SendPetitionAsync(ValidationPetitionDto petition)
    {
        var response = await _http.PostAsJsonAsync("/validation/validate", petition);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var errorBody = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rule = errorBody.GetProperty("rule").GetString() ?? "UnknownRule";
            var message = errorBody.GetProperty("error").GetString() ?? "Unknown Law Violation";
            throw new RpasLawViolationException(rule, message);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var errorBody = await response.Content.ReadFromJsonAsync<JsonElement>();
            var path = errorBody.GetProperty("path").GetString() ?? "UnknownPath";
            var message = errorBody.GetProperty("error").GetString() ?? "Topology Violation";
            throw new TopologyViolationException(path, message);
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<InternalGovernanceResponse>(JsonOptions);
        
        return new GovernanceResult(
            Success: true,
            Message: result?.Message ?? "Success",
            AuthorityToken: result?.AuthorityToken != null 
                ? new AuthorityTokenDto(result.AuthorityToken.Id, result.AuthorityToken.ExpiresAt, result.AuthorityToken.RitualType)
                : null
        );
    }

    // Helper classes for internal deserialization to avoid leaking JSON patterns
    private record InternalGovernanceResponse(
        string Status,
        string Message,
        InternalAuthorityToken? AuthorityToken
    );

    private record InternalAuthorityToken(
        Guid Id,
        DateTime ExpiresAt,
        string RitualType
    );
}
