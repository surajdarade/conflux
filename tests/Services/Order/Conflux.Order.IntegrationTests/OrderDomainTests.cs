using Conflux.Order.Domain;
using OrderEntity = Conflux.Order.Domain.Order;
using Xunit;

namespace Conflux.Order.IntegrationTests;

/// <summary>
/// Provides tests for the Order aggregate lifecycle and state transitions.
/// </summary>
public sealed class OrderDomainTests
{
    /// <summary>
    /// Verifies that a newly created order starts in the Pending state.
    /// </summary>
    [Fact]
    public void NewOrder_StartsInPendingState()
    {
        var order = CreateOrder();

        Assert.Equal(
            OrderStatus.Pending,
            order.Status);
    }

    /// <summary>
    /// Verifies that a pending order can transition to InventoryReserved.
    /// </summary>
    [Fact]
    public void MarkInventoryReserved_FromPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();

        Assert.Equal(
            OrderStatus.InventoryReserved,
            order.Status);
    }

    /// <summary>
    /// Verifies that an order cannot transition to InventoryReserved
    /// from a state other than Pending.
    /// </summary>
    [Fact]
    public void MarkInventoryReserved_FromInvalidState_Throws()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();

        Assert.Throws<InvalidOperationException>(
            () => order.MarkInventoryReserved());
    }

    /// <summary>
    /// Verifies that an inventory-reserved order can transition to
    /// PaymentPending.
    /// </summary>
    [Fact]
    public void MarkPaymentPending_FromInventoryReserved_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();

        Assert.Equal(
            OrderStatus.PaymentPending,
            order.Status);
    }

    /// <summary>
    /// Verifies that an order cannot transition directly from Pending
    /// to PaymentPending.
    /// </summary>
    [Fact]
    public void MarkPaymentPending_FromPending_Throws()
    {
        var order = CreateOrder();

        Assert.Throws<InvalidOperationException>(
            () => order.MarkPaymentPending());
    }

    /// <summary>
    /// Verifies that a payment-pending order can transition to Confirmed.
    /// </summary>
    [Fact]
    public void Confirm_FromPaymentPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Confirm();

        Assert.Equal(
            OrderStatus.Confirmed,
            order.Status);
    }

    /// <summary>
    /// Verifies that an order cannot transition directly from Pending
    /// to Confirmed.
    /// </summary>
    [Fact]
    public void Confirm_FromPending_Throws()
    {
        var order = CreateOrder();

        Assert.Throws<InvalidOperationException>(
            () => order.Confirm());
    }

    /// <summary>
    /// Verifies that an order cannot transition directly from
    /// InventoryReserved to Confirmed.
    /// </summary>
    [Fact]
    public void Confirm_FromInventoryReserved_Throws()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();

        Assert.Throws<InvalidOperationException>(
            () => order.Confirm());
    }

    /// <summary>
    /// Verifies that a pending order can be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_FromPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.Cancel();

        Assert.Equal(
            OrderStatus.Cancelled,
            order.Status);
    }

    /// <summary>
    /// Verifies that an inventory-reserved order can be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_FromInventoryReserved_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.Cancel();

        Assert.Equal(
            OrderStatus.Cancelled,
            order.Status);
    }

    /// <summary>
    /// Verifies that a payment-pending order can be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_FromPaymentPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Cancel();

        Assert.Equal(
            OrderStatus.Cancelled,
            order.Status);
    }

    /// <summary>
    /// Verifies that a confirmed order cannot be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_FromConfirmed_Throws()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(
            () => order.Cancel());
    }

    /// <summary>
    /// Verifies that a failed order cannot be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_FromFailed_Throws()
    {
        var order = CreateOrder();

        order.Fail();

        Assert.Throws<InvalidOperationException>(
            () => order.Cancel());
    }

    /// <summary>
    /// Verifies that a pending order can transition to Failed.
    /// </summary>
    [Fact]
    public void Fail_FromPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.Fail();

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);
    }

    /// <summary>
    /// Verifies that an inventory-reserved order can transition to Failed.
    /// </summary>
    [Fact]
    public void Fail_FromInventoryReserved_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.Fail();

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);
    }

    /// <summary>
    /// Verifies that a payment-pending order can transition to Failed.
    /// </summary>
    [Fact]
    public void Fail_FromPaymentPending_TransitionsSuccessfully()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Fail();

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);
    }

    /// <summary>
    /// Verifies that a confirmed order cannot be marked as failed.
    /// </summary>
    [Fact]
    public void Fail_FromConfirmed_Throws()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(
            () => order.Fail());
    }

    /// <summary>
    /// Verifies that a failed order cannot transition to another state.
    /// </summary>
    [Fact]
    public void FailedOrder_CannotTransitionToAnotherState()
    {
        var order = CreateOrder();

        order.Fail();

        Assert.Throws<InvalidOperationException>(
            () => order.MarkInventoryReserved());

        Assert.Throws<InvalidOperationException>(
            () => order.MarkPaymentPending());

        Assert.Throws<InvalidOperationException>(
            () => order.Confirm());

        Assert.Throws<InvalidOperationException>(
            () => order.Fail());
    }

    /// <summary>
    /// Verifies that a cancelled order cannot transition to another state.
    /// </summary>
    [Fact]
    public void CancelledOrder_CannotTransitionToAnotherState()
    {
        var order = CreateOrder();

        order.Cancel();

        Assert.Throws<InvalidOperationException>(
            () => order.MarkInventoryReserved());

        Assert.Throws<InvalidOperationException>(
            () => order.MarkPaymentPending());

        Assert.Throws<InvalidOperationException>(
            () => order.Confirm());

        Assert.Throws<InvalidOperationException>(
            () => order.Fail());

        Assert.Throws<InvalidOperationException>(
            () => order.Cancel());
    }

    /// <summary>
    /// Verifies that a confirmed order is terminal and cannot transition
    /// to another lifecycle state.
    /// </summary>
    [Fact]
    public void ConfirmedOrder_CannotTransitionToAnotherState()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();
        order.MarkPaymentPending();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(
            () => order.MarkInventoryReserved());

        Assert.Throws<InvalidOperationException>(
            () => order.MarkPaymentPending());

        Assert.Throws<InvalidOperationException>(
            () => order.Confirm());

        Assert.Throws<InvalidOperationException>(
            () => order.Fail());

        Assert.Throws<InvalidOperationException>(
            () => order.Cancel());
    }

    /// <summary>
    /// Verifies that order items cannot be added after the order leaves
    /// the Pending state.
    /// </summary>
    [Fact]
    public void AddItem_AfterOrderLeavesPending_Throws()
    {
        var order = CreateOrder();

        order.MarkInventoryReserved();

        var item = new OrderItem(
            Guid.NewGuid(),
            "CONFLUX-002",
            1,
            100m,
            "INR");

        Assert.Throws<InvalidOperationException>(
            () => order.AddItem(item));
    }

    /// <summary>
    /// Verifies that an inventory reservation transitions from
    /// <see cref="OrderInventoryReservationStatus.Pending"/> to
    /// <see cref="OrderInventoryReservationStatus.Reserved"/> when marked as reserved.
    /// </summary>
    [Fact]
    public void OrderInventoryReservation_ShouldTransitionFromPendingToReserved()
    {
        var reservation =
            new OrderInventoryReservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                2);

        Assert.Equal(
            OrderInventoryReservationStatus.Pending,
            reservation.Status);

        reservation.MarkReserved();

        Assert.Equal(
            OrderInventoryReservationStatus.Reserved,
            reservation.Status);
    }

    /// <summary>
    /// Verifies that a reserved inventory reservation transitions to
    /// <see cref="OrderInventoryReservationStatus.Released"/> and records
    /// the time at which the reservation was released.
    /// </summary>
    [Fact]
    public void OrderInventoryReservation_ShouldTransitionFromReservedToReleased()
    {
        var reservation =
            new OrderInventoryReservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                2);

        reservation.MarkReserved();
        reservation.MarkReleased();

        Assert.Equal(
            OrderInventoryReservationStatus.Released,
            reservation.Status);

        Assert.NotNull(reservation.ReleasedAt);
    }

    /// <summary>
    /// Verifies that releasing an inventory reservation before it has been
    /// reserved throws an <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void OrderInventoryReservation_ShouldRejectReleaseBeforeReservation()
    {
        var reservation =
            new OrderInventoryReservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                2);

        Assert.Throws<InvalidOperationException>(
            () => reservation.MarkReleased());
    }

    /// <summary>
    /// Verifies that releasing an already released inventory reservation
    /// is idempotent and does not change the original release timestamp.
    /// </summary>
    [Fact]
    public void OrderInventoryReservation_ShouldBeIdempotentWhenReleasedTwice()
    {
        var reservation =
            new OrderInventoryReservation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                2);

        reservation.MarkReserved();
        reservation.MarkReleased();

        var releasedAt =
            reservation.ReleasedAt;

        reservation.MarkReleased();

        Assert.Equal(
            OrderInventoryReservationStatus.Released,
            reservation.Status);

        Assert.Equal(
            releasedAt,
            reservation.ReleasedAt);
    }


    /// <summary>
    /// Creates a valid order used by the domain tests.
    /// </summary>
    /// <returns>
    /// A new pending order.
    /// </returns>
    private static OrderEntity CreateOrder()
    {
        return new OrderEntity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString());
    }
}