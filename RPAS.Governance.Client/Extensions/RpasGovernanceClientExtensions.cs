using System;
using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class RpasGovernanceClientExtensions
{
    /// <summary>
    /// Adds the RPAS Sovereign Governance Petitioner SDK to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Governance Courthouse (e.g., http://localhost:7200).</param>
    /// <returns>An IHttpClientBuilder to allow further configuration (Resilience, Auth, etc.).</returns>
    public static IHttpClientBuilder AddRpasGovernanceClient(this IServiceCollection services, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Governance Base URL must be provided.", nameof(baseUrl));
        }

        return services.AddHttpClient<IGovernanceClient, GovernanceClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });
    }
}
