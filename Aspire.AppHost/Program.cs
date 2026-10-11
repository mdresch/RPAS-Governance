var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin()
    .AddDatabase("governanceDb");

var api = builder.AddProject<Projects.RPAS_Governance_Api>("api")
    .WithHttpsEndpoint()
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithReference(db)
    .WaitFor(db);

var sidpaWeb = builder.AddJavaScriptApp("sidpa-web", "../../SIDPA/apps/web")
    .WithPnpm()
    .WithRunScript("dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithEnvironment("RPAS_COURTHOUSE_URL", api.GetEndpoint("http"))
    .WithEnvironment("BROWSER", "none")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
