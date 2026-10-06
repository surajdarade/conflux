using Conflux.Payment.Infrastructure;
using Conflux.Payment.OutboxPublisher;
using Conflux.Payment.OutboxPublisher.Configuration;
using Conflux.Payment.OutboxPublisher.Kafka;
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
                options.PaymentEventsTopic),
        "Kafka PaymentEventsTopic must be configured.")
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

builder.Services.AddDbContext<PaymentDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString(
                "PaymentDatabase")));

builder.Services.AddSingleton<IKafkaEventPublisher>(
    serviceProvider =>
    {
        var options =
            serviceProvider
                .GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<KafkaOptions>>()
                .Value;

        return new ConfluentKafkaEventPublisher(
            options.BootstrapServers);
    });

builder.Services.AddHostedService<OutboxPublisherWorker>();

var host =
    builder.Build();

await host.RunAsync();