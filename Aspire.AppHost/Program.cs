var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("governanceDb");

builder.AddProject("api", "../RPAS.Governance.Api/RPAS.Governance.Api.csproj")
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
