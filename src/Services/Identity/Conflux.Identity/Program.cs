using Conflux.Identity.Configuration.Authentication;
using Conflux.Identity.Domain;
using Conflux.Identity.Features.GetCurrentUser;
using Conflux.Identity.Features.Login;
using Conflux.Identity.Features.Register;
using Conflux.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("IdentityDatabase")));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddConfluxAuthentication(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapRegisterEndpoint();
app.MapLoginEndpoint();
app.MapGetCurrentUserEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program {
}