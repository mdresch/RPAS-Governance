using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MassTransit;
using Adpa.Orchestrator.Clients;
using Adpa.Orchestrator.Services;
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Aspire Service Defaults (Observability / Resilience)
// ---------------------------------------------------------------------------

builder.AddServiceDefaults();

// ---------------------------------------------------------------------------
// 2. Data Persistence (Delegated to Governance APIs)
// ---------------------------------------------------------------------------

builder.Services.AddHttpClient<GovernanceApiClient>(client =>
{
    var govUrl = builder.Configuration["RPAS_GOVERNANCE_BASE_URL"] ?? "http://localhost:7200";
    client.BaseAddress = new Uri(govUrl);
});

// ---------------------------------------------------------------------------
// 2. Authentication (Firebase JWT Validation)
// ---------------------------------------------------------------------------

var firebaseProjectId = builder.Configuration["FIREBASE_PROJECT_ID"] 
    ?? throw new InvalidOperationException("Missing FIREBASE_PROJECT_ID environment variable. Required for RPAS-CM Experience Tier authentication.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true
        };
    });

// ---------------------------------------------------------------------------
// 2b. Experience Tier Security (CORS for Vercel)
// ---------------------------------------------------------------------------

builder.Services.AddCors(options =>
{
    options.AddPolicy("ExperienceTierPolicy",
        policy =>
        {
            // Allow Vercel and Local Debugging
            policy.WithOrigins("https://adpa-researcher.vercel.app", "http://localhost:3000", "http://localhost:3005")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// ---------------------------------------------------------------------------
// 3. Messaging (MassTransit + RabbitMQ)
// ---------------------------------------------------------------------------

builder.AddRabbitMQClient("messaging");
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("messaging");
        if (!string.IsNullOrEmpty(connectionString))
        {
            cfg.Host(connectionString);
        }
        else
        {
            // Fallback for Aspire service discovery
            cfg.Host("messaging");
        }
        cfg.ConfigureEndpoints(context);
    });
});

// ---------------------------------------------------------------------------
// 4. Intelligence Bridge (Typed HttpClient)
// ---------------------------------------------------------------------------

builder.Services.AddHttpClient<IntelligenceClient>(client => 
{
    var intelUrl = builder.Configuration["INTELLIGENCE_URL"] ?? "http://intelligence";
    // Fallback for local debugging without service discovery
    if (builder.Environment.IsDevelopment() && intelUrl == "http://intelligence")
    {
        intelUrl = "http://localhost:8000";
    }
    client.BaseAddress = new Uri(intelUrl);
});

// ---------------------------------------------------------------------------
// 5. Domain Services (Semantic RTM)
// ---------------------------------------------------------------------------

builder.Services.AddScoped<ISemanticRtmSeeder, SemanticRtmSeeder>();
builder.Services.AddScoped<IRtmExecutionService, RtmExecutionService>();

// ---------------------------------------------------------------------------
// 6. Controller Infrastructure
// ---------------------------------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------------
// 6. Middleware & Endpoints
// ---------------------------------------------------------------------------

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("ExperienceTierPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ---------------------------------------------------------------------------
// 6. Startup Validation (Mechanical Integrity)
// ---------------------------------------------------------------------------

var configProvider = app.Services.GetRequiredService<IConfiguration>();
var aiProvider = configProvider["AI_PROVIDER"] ?? Environment.GetEnvironmentVariable("AI_PROVIDER");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("RPAS Stabilization: AI_PROVIDER resolved as: {Value}", aiProvider ?? "<NULL>");

app.Run();
