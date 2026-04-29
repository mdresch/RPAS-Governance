using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Persistence.Data;

var builder = WebApplication.CreateBuilder(args);

// Add standard Aspire defaults (Health checks, OTel, Resilience)
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Sovereign persistence with built-in Aspire health checks and pooling (same connection name as Adpa orchestrator)
builder.Services.AddSingleton<RpasLawEnforcementInterceptor>();
builder.Services.AddDbContext<GovernanceDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("governance-ledger");
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<RpasLawEnforcementInterceptor>());
});

var app = builder.Build();

// Map standard Aspire endpoints (/health, /alive)
app.MapDefaultEndpoints();

if (!app.Configuration.GetValue("Governance:SkipEfMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var _db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
    try
    {
        _db.Database.Migrate();
    }
    catch
    {
        _db.Database.EnsureCreated();
    }
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
