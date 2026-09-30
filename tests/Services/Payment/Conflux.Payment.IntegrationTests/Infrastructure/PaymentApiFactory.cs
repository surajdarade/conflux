extern alias Payment;

using Conflux.Payment.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentProgram = Payment::Program;
using Testcontainers.PostgreSql;

namespace Conflux.Payment.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host
/// for Payment integration tests.
/// </summary>
public sealed class PaymentApiFactory :
    WebApplicationFactory<PaymentProgram>
{
    private readonly PostgreSqlContainer _postgresContainer;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="PaymentApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the test host.
    /// </param>
    public PaymentApiFactory(
        PostgreSqlContainer postgresContainer)
    {
        _postgresContainer = postgresContainer;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting(
            "ConnectionStrings:PaymentDatabase",
            _postgresContainer.GetConnectionString());
    }

    /// <summary>
    /// Applies all pending Payment database migrations.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<PaymentDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}