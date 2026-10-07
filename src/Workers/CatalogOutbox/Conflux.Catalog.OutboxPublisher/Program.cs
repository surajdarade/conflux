using Conflux.Catalog.Infrastructure;
using Conflux.Catalog.OutboxPublisher;
using Conflux.Catalog.OutboxPublisher.Configuration;
using Conflux.Catalog.OutboxPublisher.Kafka;
using Microsoft.EntityFrameworkCore;

var builder =
    Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<KafkaOptions>()
    .Bind(
        builder.Configuration.GetSection(
            KafkaOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.BootstrapServers),
        "Kafka BootstrapServers must be configured.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(
                options.CatalogEventsTopic),
        "Kafka CatalogEventsTopic must be configured.")
    .Validate(
        options =>
            options.BatchSize > 0,
        "Kafka BatchSize must be greater than zero.")
    .Validate(
        options =>
            options.PollInterval > TimeSpan.Zero,
        "Kafka PollInterval must be greater than zero.")
    .Validate(
        options =>
            options.ClaimLeaseDuration > TimeSpan.Zero,
        "Kafka ClaimLeaseDuration must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddDbContext<CatalogDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString(
                "CatalogDatabase")));

builder.Services.AddSingleton<IKafkaEventPublisher>(
    serviceProvider =>
    {
        var options =
            serviceProvider
                .GetRequiredService<
                    Microsoft.Extensions.Options
                        .IOptions<KafkaOptions>>()
                .Value;

        return new ConfluentKafkaEventPublisher(
            options.BootstrapServers);
    });

builder.Services.AddHostedService<OutboxPublisherWorker>();

var host = builder.Build();

await host.RunAsync();