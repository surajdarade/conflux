using Conflux.Inventory.Infrastructure;
using Conflux.Inventory.Infrastructure.Sharding;
using Conflux.Inventory.OutboxPublisher;
using Conflux.Inventory.OutboxPublisher.Configuration;
using Conflux.Inventory.OutboxPublisher.Kafka;
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
                options.InventoryEventsTopic),
        "Kafka InventoryEventsTopic must be configured.")
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

builder.Services.AddDbContext<InventoryDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString(
                "InventoryDatabase")));
builder.Services.AddSingleton<InventoryDbContextProvider>();

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