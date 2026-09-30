extern alias Inventory;

using Conflux.Contracts.Inventory;
using Conflux.Inventory.Domain;
using Conflux.Inventory.Infrastructure;
using Conflux.Inventory.IntegrationTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using InventoryApiFactory =
    Conflux.Inventory.IntegrationTests.Infrastructure.InventoryApiFactory;

namespace Conflux.Inventory.IntegrationTests;

/// <summary>
/// Provides integration tests for the Inventory gRPC service.
/// </summary>
[Collection(InventoryTestCollection.Name)]
public sealed class InventoryGrpcTests : IAsyncLifetime {
    private readonly InventoryTestFixture _fixture;
    private InventoryApiFactory _factory = null!;
    private GrpcChannel _channel = null!;
    private InventoryService.InventoryServiceClient _client = null!;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InventoryGrpcTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Inventory test fixture.
    /// </param>
    public InventoryGrpcTests(
        InventoryTestFixture fixture) {
        _fixture = fixture;
    }

    /// <summary>
    /// Initializes the Inventory gRPC test host.
    /// </summary>
    public async ValueTask InitializeAsync() {
        _factory = new InventoryApiFactory(
            _fixture.PostgresContainer);

        await _factory.ApplyDatabaseMigrationsAsync(
            TestContext.Current.CancellationToken);

        _channel = GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions
            {
                HttpHandler = _factory.Server.CreateHandler()
            });

        _client =
            new InventoryService.InventoryServiceClient(
                _channel);
    }

    /// <summary>
    /// Disposes the Inventory gRPC test host.
    /// </summary>
    public async ValueTask DisposeAsync() {
        _channel.Dispose();
        _factory.Dispose();

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Verifies that the gRPC service can reserve inventory successfully.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithSufficientInventory_Succeeds() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var inventoryItemId =
            await CreateInventoryItemAsync(
                "GRPC-RESERVE-001",
                10,
                cancellationToken);

        var reservationId =
            Guid.NewGuid();

        var response =
            await _client.ReserveInventoryAsync(
                new ReserveInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString(),
                    Quantity = 3
                },
                cancellationToken: cancellationToken);

        Assert.Equal(
            inventoryItemId.ToString(),
            response.InventoryItemId);

        Assert.Equal(
            reservationId.ToString(),
            response.ReservationId);

        Assert.Equal(
            "GRPC-RESERVE-001",
            response.Sku);

        Assert.Equal(
            3,
            response.ReservedQuantity);

        Assert.Equal(
            7,
            response.AvailableQuantity);

        Assert.Equal(
            3,
            response.TotalReservedQuantity);
    }

    /// <summary>
    /// Verifies that repeating the same reservation request is idempotent.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithSameReservationId_IsIdempotent() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var inventoryItemId =
            await CreateInventoryItemAsync(
                "GRPC-IDEMPOTENT-001",
                10,
                cancellationToken);

        var reservationId =
            Guid.NewGuid();

        var request =
            new ReserveInventoryRequest
            {
                InventoryItemId =
                    inventoryItemId.ToString(),
                ReservationId =
                    reservationId.ToString(),
                Quantity = 4
            };

        var firstResponse =
            await _client.ReserveInventoryAsync(
                request,
                cancellationToken: cancellationToken);

        var secondResponse =
            await _client.ReserveInventoryAsync(
                request,
                cancellationToken: cancellationToken);

        Assert.Equal(
            firstResponse.InventoryItemId,
            secondResponse.InventoryItemId);

        Assert.Equal(
            firstResponse.ReservationId,
            secondResponse.ReservationId);

        Assert.Equal(
            firstResponse.ReservedQuantity,
            secondResponse.ReservedQuantity);

        Assert.Equal(
            6,
            secondResponse.AvailableQuantity);

        Assert.Equal(
            4,
            secondResponse.TotalReservedQuantity);

        var inventory =
            await GetInventoryItemAsync(
                inventoryItemId,
                cancellationToken);

        Assert.Equal(
            6,
            inventory.AvailableQuantity);

        Assert.Equal(
            4,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that insufficient inventory is returned as
    /// a resource-exhausted gRPC status.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithInsufficientInventory_ReturnsResourceExhausted() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var inventoryItemId =
            await CreateInventoryItemAsync(
                "GRPC-INSUFFICIENT-001",
                2,
                cancellationToken);

        var exception =
            await Assert.ThrowsAsync<RpcException>(
                async () =>
                    await _client.ReserveInventoryAsync(
                        new ReserveInventoryRequest
                        {
                            InventoryItemId =
                                inventoryItemId.ToString(),
                            ReservationId =
                                Guid.NewGuid().ToString(),
                            Quantity = 3
                        },
                        cancellationToken: cancellationToken));

        Assert.Equal(
            StatusCode.ResourceExhausted,
            exception.StatusCode);
    }

    /// <summary>
    /// Verifies that an unknown inventory item returns
    /// a not-found gRPC status.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithUnknownInventoryItem_ReturnsNotFound() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var exception =
            await Assert.ThrowsAsync<RpcException>(
                async () =>
                    await _client.ReserveInventoryAsync(
                        new ReserveInventoryRequest
                        {
                            InventoryItemId =
                                Guid.NewGuid().ToString(),
                            ReservationId =
                                Guid.NewGuid().ToString(),
                            Quantity = 1
                        },
                        cancellationToken: cancellationToken));

        Assert.Equal(
            StatusCode.NotFound,
            exception.StatusCode);
    }

    /// <summary>
    /// Verifies that an invalid inventory item identifier
    /// returns an invalid-argument gRPC status.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithInvalidInventoryItemId_ReturnsInvalidArgument() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var exception =
            await Assert.ThrowsAsync<RpcException>(
                async () =>
                    await _client.ReserveInventoryAsync(
                        new ReserveInventoryRequest
                        {
                            InventoryItemId = "invalid-guid",
                            ReservationId =
                                Guid.NewGuid().ToString(),
                            Quantity = 1
                        },
                        cancellationToken: cancellationToken));

        Assert.Equal(
            StatusCode.InvalidArgument,
            exception.StatusCode);
    }

    /// <summary>
    /// Verifies that an active reservation can be released through gRPC.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithActiveReservation_Succeeds() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var inventoryItemId =
            await CreateInventoryItemAsync(
                "GRPC-RELEASE-001",
                10,
                cancellationToken);

        var reservationId =
            Guid.NewGuid();

        await _client.ReserveInventoryAsync(
            new ReserveInventoryRequest
            {
                InventoryItemId =
                    inventoryItemId.ToString(),
                ReservationId =
                    reservationId.ToString(),
                Quantity = 4
            },
            cancellationToken: cancellationToken);

        var response =
            await _client.ReleaseInventoryAsync(
                new ReleaseInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString()
                },
                cancellationToken: cancellationToken);

        Assert.Equal(
            inventoryItemId.ToString(),
            response.InventoryItemId);

        Assert.Equal(
            reservationId.ToString(),
            response.ReservationId);

        Assert.Equal(
            4,
            response.ReleasedQuantity);

        Assert.Equal(
            10,
            response.AvailableQuantity);

        Assert.Equal(
            0,
            response.TotalReservedQuantity);

        Assert.False(
            response.AlreadyReleased);
    }

    /// <summary>
    /// Verifies that repeating a release request does not release
    /// the same reservation twice.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithSameReservationId_IsIdempotent() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var inventoryItemId =
            await CreateInventoryItemAsync(
                "GRPC-RELEASE-IDEMPOTENT-001",
                10,
                cancellationToken);

        var reservationId =
            Guid.NewGuid();

        await _client.ReserveInventoryAsync(
            new ReserveInventoryRequest
            {
                InventoryItemId =
                    inventoryItemId.ToString(),
                ReservationId =
                    reservationId.ToString(),
                Quantity = 5
            },
            cancellationToken: cancellationToken);

        var firstResponse =
            await _client.ReleaseInventoryAsync(
                new ReleaseInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString()
                },
                cancellationToken: cancellationToken);

        var secondResponse =
            await _client.ReleaseInventoryAsync(
                new ReleaseInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString()
                },
                cancellationToken: cancellationToken);

        Assert.False(
            firstResponse.AlreadyReleased);

        Assert.True(
            secondResponse.AlreadyReleased);

        Assert.Equal(
            10,
            secondResponse.AvailableQuantity);

        Assert.Equal(
            0,
            secondResponse.TotalReservedQuantity);

        var inventory =
            await GetInventoryItemAsync(
                inventoryItemId,
                cancellationToken);

        Assert.Equal(
            10,
            inventory.AvailableQuantity);

        Assert.Equal(
            0,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that an invalid reservation identifier returns
    /// an invalid-argument gRPC status.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithInvalidReservationId_ReturnsInvalidArgument() {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var exception =
            await Assert.ThrowsAsync<RpcException>(
                async () =>
                    await _client.ReleaseInventoryAsync(
                        new ReleaseInventoryRequest
                        {
                            InventoryItemId =
                                Guid.NewGuid().ToString(),
                            ReservationId =
                                "invalid-guid"
                        },
                        cancellationToken: cancellationToken));

        Assert.Equal(
            StatusCode.InvalidArgument,
            exception.StatusCode);
    }

    private async Task<Guid> CreateInventoryItemAsync(
        string sku,
        int quantity,
        CancellationToken cancellationToken) {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();

        var inventoryItem =
            new InventoryItem(
                Guid.NewGuid(),
                sku,
                quantity);

        dbContext.InventoryItems.Add(
            inventoryItem);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return inventoryItem.Id;
    }

    private async Task<InventoryItemSnapshot>
        GetInventoryItemAsync(
            Guid inventoryItemId,
            CancellationToken cancellationToken) {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();

        var inventoryItem =
            await dbContext.InventoryItems
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        inventoryItemId,
                    cancellationToken);

        return new InventoryItemSnapshot(
            inventoryItem.AvailableQuantity,
            inventoryItem.ReservedQuantity);
    }

    private sealed record InventoryItemSnapshot(
        int AvailableQuantity,
        int ReservedQuantity);
}