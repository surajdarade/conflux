using System.Diagnostics.Metrics;

namespace Conflux.Observability;

/// <summary>Exposes domain-level counters and latency measurements for Conflux.</summary>
public sealed class ConfluxBusinessMetrics : IDisposable
{
    private readonly Meter _meter;

    /// <summary>Gets the OpenTelemetry meter name.</summary>
    public const string MeterName = "Conflux.Business";

    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersFailed;
    private readonly Counter<long> _inventoryReserved;
    private readonly Counter<long> _inventoryFailed;
    private readonly Counter<long> _paymentsAuthorized;
    private readonly Counter<long> _paymentsFailed;
    private readonly Counter<long> _paymentsCaptured;
    private readonly Counter<long> _paymentsVoided;
    private readonly Counter<long> _paymentsRefunded;
    private readonly Counter<long> _sagaFailures;
    private readonly Histogram<double> _reservationLatency;
    private readonly Histogram<double> _sagaDuration;

    /// <summary>Initializes business metrics.</summary>
    public ConfluxBusinessMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _ordersCreated = _meter.CreateCounter<long>("conflux.orders.created.total");
        _ordersFailed = _meter.CreateCounter<long>("conflux.orders.failed.total");
        _inventoryReserved = _meter.CreateCounter<long>("conflux.inventory.reserved.total");
        _inventoryFailed = _meter.CreateCounter<long>("conflux.inventory.failed.total");
        _paymentsAuthorized = _meter.CreateCounter<long>("conflux.payments.authorized.total");
        _paymentsFailed = _meter.CreateCounter<long>("conflux.payments.failed.total");
        _paymentsCaptured = _meter.CreateCounter<long>("conflux.payments.captured.total");
        _paymentsVoided = _meter.CreateCounter<long>("conflux.payments.voided.total");
        _paymentsRefunded = _meter.CreateCounter<long>("conflux.payments.refunded.total");
        _sagaFailures = _meter.CreateCounter<long>("conflux.saga.failures.total");
        _reservationLatency = _meter.CreateHistogram<double>("conflux.inventory.reservation.duration", "ms");
        _sagaDuration = _meter.CreateHistogram<double>("conflux.saga.duration", "ms");
    }

    /// <summary>Releases the underlying meter.</summary>
    public void Dispose() => _meter.Dispose();

    /// <summary>Records an order creation.</summary>
    public void OrderCreated() => _ordersCreated.Add(1);
    /// <summary>Records an order failure.</summary>
    public void OrderFailed() => _ordersFailed.Add(1);
    /// <summary>Records a successful inventory reservation.</summary>
    public void InventoryReserved() => _inventoryReserved.Add(1);
    /// <summary>Records an inventory failure.</summary>
    public void InventoryFailed() => _inventoryFailed.Add(1);
    /// <summary>Records an authorized payment.</summary>
    public void PaymentAuthorized() => _paymentsAuthorized.Add(1);
    /// <summary>Records a failed payment operation.</summary>
    public void PaymentFailed() => _paymentsFailed.Add(1);
    /// <summary>Records a captured payment.</summary>
    public void PaymentCaptured() => _paymentsCaptured.Add(1);
    /// <summary>Records a voided payment.</summary>
    public void PaymentVoided() => _paymentsVoided.Add(1);
    /// <summary>Records a refunded payment.</summary>
    public void PaymentRefunded() => _paymentsRefunded.Add(1);
    /// <summary>Records a saga failure.</summary>
    public void SagaFailed() => _sagaFailures.Add(1);
    /// <summary>Records reservation latency.</summary>
    public void ReservationDuration(TimeSpan duration) => _reservationLatency.Record(duration.TotalMilliseconds);
    /// <summary>Records saga duration.</summary>
    public void SagaDuration(TimeSpan duration) => _sagaDuration.Record(duration.TotalMilliseconds);
}
