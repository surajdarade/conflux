using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Identity.Configuration.Authentication;
using Conflux.Identity.Domain;
using Conflux.Identity.Features.GetCurrentUser;
using Conflux.Identity.Features.Login;
using Conflux.Identity.Features.Register;
using Conflux.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-identity");

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("IdentityDatabase")));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddConfluxAuthentication(builder.Configuration);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await migrationDbContext.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks(
    "/alive",
    new HealthCheckOptions
    {
        Predicate = static _ => false
    });

app.MapHealthChecks("/ready");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

app.MapRegisterEndpoint();
app.MapLoginEndpoint();
app.MapGetCurrentUserEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program {
}