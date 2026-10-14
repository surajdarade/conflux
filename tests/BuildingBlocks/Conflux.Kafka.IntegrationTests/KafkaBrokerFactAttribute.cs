using System.Runtime.CompilerServices;
using Xunit;

namespace Conflux.Kafka.IntegrationTests;

/// <summary>
/// Marks a test that requires an explicitly configured Kafka broker.
/// The tests are skipped during ordinary unit-test runs when no broker is configured.
/// </summary>
public sealed class KafkaBrokerFactAttribute : FactAttribute
{
    /// <summary>Initializes a broker-dependent fact and preserves xUnit source information.</summary>
    /// <param name="sourceFilePath">The source file containing the test.</param>
    /// <param name="sourceLineNumber">The source line containing the test.</param>
    public KafkaBrokerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CONFLUX_KAFKA_BOOTSTRAP_SERVERS")))
        {
            Skip = "Set CONFLUX_KAFKA_BOOTSTRAP_SERVERS to run real-broker Kafka integration tests.";
        }
    }
}
