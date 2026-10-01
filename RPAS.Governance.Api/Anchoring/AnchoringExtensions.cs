using Azure.Identity;
using Azure.Storage.Blobs;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Anchoring;

public static class AnchoringExtensions
{
    /// <summary>
    /// Governance:Anchoring:Provider = "File" (Path) or "AzureBlob" (ContainerUri, authenticated by Azure identity).
    /// Not configured means no anchoring and no anchor verification; the hash chain itself is unaffected.
    /// </summary>
    public static IServiceCollection AddLedgerAnchoring(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Governance:Anchoring:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return services;
        }

        switch (provider.Trim().ToLowerInvariant())
        {
            case "file":
                var path = configuration["Governance:Anchoring:Path"]
                           ?? throw new InvalidOperationException("Governance:Anchoring:Path is required for the File anchoring provider.");
                services.AddSingleton<ILedgerAnchorSink>(new FileLedgerAnchorSink(path));
                break;

            case "azureblob":
                var uri = configuration["Governance:Anchoring:ContainerUri"]
                          ?? throw new InvalidOperationException("Governance:Anchoring:ContainerUri is required for the AzureBlob anchoring provider.");
                services.AddSingleton<ILedgerAnchorSink>(
                    new AzureBlobLedgerAnchorSink(new BlobContainerClient(new Uri(uri), new DefaultAzureCredential())));
                break;

            default:
                throw new InvalidOperationException($"Unknown Governance:Anchoring:Provider '{provider}'. Use File or AzureBlob.");
        }

        services.AddSingleton<LedgerAnchorService>();
        services.AddHostedService(sp => sp.GetRequiredService<LedgerAnchorService>());
        return services;
    }
}
