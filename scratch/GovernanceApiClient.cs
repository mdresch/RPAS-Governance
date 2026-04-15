using System.Text.Json;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace Adpa.Orchestrator.Clients;

public class ValidationPetition
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public object Payload { get; set; } = new();
}

public class GovernanceApiClient
{
    private readonly HttpClient _http;

    public GovernanceApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> ValidateStateTransitionAsync(ValidationPetition petition)
    {
        var response = await _http.PostAsJsonAsync("/Validation/validate", petition);
        
        if (response.IsSuccessStatusCode) return true;
        
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Governance Law Violation: {err}");
        }
        
        response.EnsureSuccessStatusCode();
        return true;
    }
}
