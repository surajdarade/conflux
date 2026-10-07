using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Conflux.Observability;

/// <summary>
/// Registers the standard Conflux OpenTelemetry pipeline.
/// </summary>
public static class ObservabilityExtensions {
    /// <summary>
    /// Adds traces and runtime/application metrics for a Conflux process.
    /// </summary>
    /// <param name="builder">
    /// The web application builder.
    /// </param>
    /// <param name="serviceName">
    /// The logical service name.
    /// </param>
    /// <returns>
    /// The same builder for fluent configuration.
    /// </returns>
    public static WebApplicationBuilder AddConfluxObservability(
        this WebApplicationBuilder builder,
        string serviceName) {
        builder.Services.AddSingleton<ConfluxBusinessMetrics>();

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(
                resource => resource.AddService(serviceName))
            .WithTracing(
                tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Conflux.Kafka")
                    .AddOtlpExporter())
            .WithMetrics(
                metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(ConfluxBusinessMetrics.MeterName)
                    .AddPrometheusExporter());

        return builder;
    }
}