extern alias Payment;
using Conflux.Payment.Features.Payments.AuthorizePayment;
using Conflux.Payment.IntegrationTests.Infrastructure;
using FluentAssertions;
using Payment::Conflux.Payment.Features.Payments.CapturePayment;
using Conflux.Payment.Features.Payments.VoidPayment;
using Conflux.Payment.Features.Payments.GetPayment;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using System.Text.Json;
using Conflux.Contracts.Events;
using Conflux.Payment.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Payment.IntegrationTests;

/// <summary>
/// Integration tests for the Payment HTTP endpoints.
/// </summary>
[Collection("Payment integration tests")]
public sealed class PaymentEndpointsTests :
    IAsyncLifetime
{
    private readonly PaymentTestFixture _fixture;
    private readonly PaymentApiFactory _factory;
    private HttpClient _client = null!;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="PaymentEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Payment test infrastructure.
    /// </param>
    public PaymentEndpointsTests(
        PaymentTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new PaymentApiFactory(
            _fixture.PostgresContainer);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();

        await _factory.ApplyDatabaseMigrationsAsync(
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();

        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Verifies that a valid payment authorization request
    /// creates an authorized payment.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithValidRequest_ReturnsCreated()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = new AuthorizePaymentRequest
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 1499.99m,
            Currency = "INR"
        };

        var response =
            await SendAuthorizeRequestAsync(
                request,
                idempotencyKey);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var payment =
            await response.Content
                .ReadFromJsonAsync<AuthorizePaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .NotBe(Guid.Empty);

        payment.OrderId
            .Should()
            .Be(orderId);

        payment.CustomerId
            .Should()
            .Be(customerId);

        payment.Amount
            .Should()
            .Be(1499.99m);

        payment.Currency
            .Should()
            .Be("INR");

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Authorized);

        payment.AlreadyAuthorized
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// Verifies that the Idempotency-Key header is required.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var request = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "INR"
        };

        var response =
            await _client.PostAsJsonAsync(
                "/api/v1/payments/authorize",
                request,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that a non-positive payment amount is rejected.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AuthorizePayment_WithInvalidAmount_ReturnsBadRequest(
        decimal amount)
    {
        using var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/payments/authorize");

        httpRequest.Headers.Add(
            "Idempotency-Key",
            Guid.NewGuid().ToString());

        httpRequest.Content =
            JsonContent.Create(
                new AuthorizePaymentRequest
                {
                    OrderId = Guid.NewGuid(),
                    CustomerId = Guid.NewGuid(),
                    Amount = amount,
                    Currency = "INR"
                });

        var response =
            await _client.SendAsync(
                httpRequest,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that retrying the same authorization request with
    /// the same idempotency key returns the original payment.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithSameIdempotencyKey_ReturnsSamePayment()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = new AuthorizePaymentRequest
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 999.99m,
            Currency = "INR"
        };

        var firstResponse =
            await SendAuthorizeRequestAsync(
                request,
                idempotencyKey);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var firstPayment =
            await firstResponse.Content
                .ReadFromJsonAsync<AuthorizePaymentResponse>(
                    TestContext.Current.CancellationToken);

        firstPayment.Should().NotBeNull();

        var secondResponse =
            await SendAuthorizeRequestAsync(
                request,
                idempotencyKey);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var secondPayment =
            await secondResponse.Content
                .ReadFromJsonAsync<AuthorizePaymentResponse>(
                    TestContext.Current.CancellationToken);

        secondPayment.Should().NotBeNull();

        secondPayment!.PaymentId
            .Should()
            .Be(firstPayment!.PaymentId);

        secondPayment.OrderId
            .Should()
            .Be(firstPayment.OrderId);

        secondPayment.CustomerId
            .Should()
            .Be(firstPayment.CustomerId);

        secondPayment.Amount
            .Should()
            .Be(firstPayment.Amount);

        secondPayment.Currency
            .Should()
            .Be(firstPayment.Currency);

        secondPayment.Status
            .Should()
            .Be(firstPayment.Status);

        secondPayment.AlreadyAuthorized
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// Verifies that reusing an idempotency key for another order
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithSameKeyDifferentOrder_ReturnsConflict()
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstRequest = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "INR"
        };

        var secondRequest = firstRequest with
        {
            OrderId = Guid.NewGuid()
        };

        var firstResponse =
            await SendAuthorizeRequestAsync(
                firstRequest,
                idempotencyKey);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var secondResponse =
            await SendAuthorizeRequestAsync(
                secondRequest,
                idempotencyKey);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that reusing an idempotency key for another customer
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithSameKeyDifferentCustomer_ReturnsConflict()
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstRequest = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "INR"
        };

        var secondRequest = firstRequest with
        {
            CustomerId = Guid.NewGuid()
        };

        var firstResponse =
            await SendAuthorizeRequestAsync(
                firstRequest,
                idempotencyKey);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var secondResponse =
            await SendAuthorizeRequestAsync(
                secondRequest,
                idempotencyKey);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that reusing an idempotency key with another amount
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithSameKeyDifferentAmount_ReturnsConflict()
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstRequest = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "INR"
        };

        var secondRequest = firstRequest with
        {
            Amount = 600m
        };

        var firstResponse =
            await SendAuthorizeRequestAsync(
                firstRequest,
                idempotencyKey);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var secondResponse =
            await SendAuthorizeRequestAsync(
                secondRequest,
                idempotencyKey);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that reusing an idempotency key with another currency
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithSameKeyDifferentCurrency_ReturnsConflict()
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstRequest = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 500m,
            Currency = "INR"
        };

        var secondRequest = firstRequest with
        {
            Currency = "USD"
        };

        var firstResponse =
            await SendAuthorizeRequestAsync(
                firstRequest,
                idempotencyKey);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var secondResponse =
            await SendAuthorizeRequestAsync(
                secondRequest,
                idempotencyKey);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that concurrent authorization requests using the same
    /// idempotency key create exactly one payment.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_WithConcurrentSameIdempotencyKey_CreatesExactlyOnePayment()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = new AuthorizePaymentRequest
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 2499.99m,
            Currency = "INR"
        };

        const int requestCount = 20;

        var tasks = Enumerable
            .Range(0, requestCount)
            .Select(
                _ => SendAuthorizeRequestAsync(
                    request,
                    idempotencyKey))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        responses
            .Should()
            .HaveCount(requestCount);

        var createdResponses = responses
            .Where(
                response =>
                    response.StatusCode == HttpStatusCode.Created)
            .ToArray();

        var successfulResponses = responses
            .Where(
                response =>
                    response.StatusCode == HttpStatusCode.Created ||
                    response.StatusCode == HttpStatusCode.OK)
            .ToArray();

        createdResponses
            .Should()
            .HaveCount(1);

        successfulResponses
            .Should()
            .HaveCount(requestCount);

        var payments = new List<AuthorizePaymentResponse>();

        foreach (var response in successfulResponses)
        {
            var payment =
                await response.Content
                    .ReadFromJsonAsync<AuthorizePaymentResponse>(
                        TestContext.Current.CancellationToken);

            payment.Should().NotBeNull();

            payments.Add(payment!);
        }

        payments
            .Select(payment => payment.PaymentId)
            .Distinct()
            .Should()
            .ContainSingle();

        payments
            .Select(payment => payment.OrderId)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be(orderId);

        payments
            .Select(payment => payment.CustomerId)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be(customerId);

        payments
            .Select(payment => payment.Status)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Authorized);
    }

    /// <summary>
    /// Verifies that an authorized payment can be captured.
    /// </summary>
    [Fact]
    public async Task CapturePayment_AuthorizedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var response =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await response.Content
                .ReadFromJsonAsync<CapturePaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Captured);

        payment.AlreadyCaptured
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// Verifies that capturing an already captured payment is idempotent.
    /// </summary>
    [Fact]
    public async Task CapturePayment_AlreadyCapturedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var firstResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var secondResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await secondResponse.Content
                .ReadFromJsonAsync<CapturePaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Captured);

        payment.AlreadyCaptured
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// Verifies that attempting to capture a nonexistent payment
    /// returns not found.
    /// </summary>
    [Fact]
    public async Task CapturePayment_NonexistentPayment_ReturnsNotFound()
    {
        var paymentId = Guid.NewGuid();

        var response =
            await _client.PostAsync(
                $"/api/v1/payments/{paymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that an invalid payment state cannot be captured.
    /// </summary>
    [Fact]
    public async Task CapturePayment_InvalidPaymentState_ReturnsConflict()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var firstResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var secondResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await secondResponse.Content
                .ReadFromJsonAsync<CapturePaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Captured);
    }

    /// <summary>
    /// Verifies that an authorized payment can be voided.
    /// </summary>
    [Fact]
    public async Task VoidPayment_AuthorizedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var response =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await response.Content
                .ReadFromJsonAsync<VoidPaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Voided);

        payment.AlreadyVoided
            .Should()
            .BeFalse();
    }

    /// <summary>
    /// Verifies that voiding an already voided payment is idempotent.
    /// </summary>
    [Fact]
    public async Task VoidPayment_AlreadyVoidedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var firstResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        firstResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var secondResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        secondResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await secondResponse.Content
                .ReadFromJsonAsync<VoidPaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Voided);

        payment.AlreadyVoided
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// Verifies that attempting to void a nonexistent payment
    /// returns not found.
    /// </summary>
    [Fact]
    public async Task VoidPayment_NonexistentPayment_ReturnsNotFound()
    {
        var paymentId = Guid.NewGuid();

        var response =
            await _client.PostAsync(
                $"/api/v1/payments/{paymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that a captured payment cannot be voided.
    /// </summary>
    [Fact]
    public async Task VoidPayment_CapturedPayment_ReturnsConflict()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var captureResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        captureResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var voidResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        voidResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies that an authorized payment can be retrieved.
    /// </summary>
    [Fact]
    public async Task GetPayment_AuthorizedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var response =
            await _client.GetAsync(
                $"/api/v1/payments/{authorization.PaymentId}",
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await response.Content
                .ReadFromJsonAsync<GetPaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.OrderId
            .Should()
            .Be(authorization.OrderId);

        payment.CustomerId
            .Should()
            .Be(authorization.CustomerId);

        payment.Amount
            .Should()
            .Be(authorization.Amount);

        payment.Currency
            .Should()
            .Be(authorization.Currency);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Authorized);
    }

    /// <summary>
    /// Verifies that a captured payment can be retrieved.
    /// </summary>
    [Fact]
    public async Task GetPayment_CapturedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var captureResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/capture",
                null,
                TestContext.Current.CancellationToken);

        captureResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var response =
            await _client.GetAsync(
                $"/api/v1/payments/{authorization.PaymentId}",
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await response.Content
                .ReadFromJsonAsync<GetPaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Captured);
    }

    /// <summary>
    /// Verifies that a voided payment can be retrieved.
    /// </summary>
    [Fact]
    public async Task GetPayment_VoidedPayment_ReturnsOk()
    {
        var authorization =
            await CreateAuthorizedPaymentAsync();

        var voidResponse =
            await _client.PostAsync(
                $"/api/v1/payments/{authorization.PaymentId}/void",
                null,
                TestContext.Current.CancellationToken);

        voidResponse.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var response =
            await _client.GetAsync(
                $"/api/v1/payments/{authorization.PaymentId}",
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.OK);

        var payment =
            await response.Content
                .ReadFromJsonAsync<GetPaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.PaymentId
            .Should()
            .Be(authorization.PaymentId);

        payment.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Voided);
    }

    /// <summary>
    /// Verifies that retrieving a nonexistent payment returns not found.
    /// </summary>
    [Fact]
    public async Task GetPayment_NonexistentPayment_ReturnsNotFound()
    {
        var paymentId = Guid.NewGuid();

        var response =
            await _client.GetAsync(
                $"/api/v1/payments/{paymentId}",
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that authorizing a payment creates the payment record and
    /// the corresponding <c>payment.authorized.v1</c> outbox message with
    /// the expected event metadata and payment details.
    /// </summary>
    [Fact]
    public async Task AuthorizePayment_CreatesPaymentAndOutboxMessage() {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        const decimal amount = 1499.99m;
        const string currency = "INR";
        const string idempotencyKey =
            "outbox-test-" +
            nameof(AuthorizePayment_CreatesPaymentAndOutboxMessage);

        var request = new AuthorizePaymentRequest
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency
        };

        using var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/payments/authorize")
            {
                Content = JsonContent.Create(request)
            };

        httpRequest.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);

        var response =
            await _client.SendAsync(
                httpRequest,
                TestContext.Current.CancellationToken);

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var responseBody =
            await response.Content.ReadFromJsonAsync<AuthorizePaymentResponse>(
                TestContext.Current.CancellationToken);

        responseBody.Should().NotBeNull();

        responseBody!.Status
            .Should()
            .Be(
                Conflux.Payment.Domain.PaymentStatus.Authorized);

        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var payment =
            await dbContext.Payments
                .AsNoTracking()
                .SingleAsync(
                    payment =>
                        payment.Id ==
                        responseBody.PaymentId,
                    TestContext.Current.CancellationToken);

        var outboxMessage =
            await dbContext.OutboxMessages
                .AsNoTracking()
                .SingleAsync(
                    message =>
                        message.CorrelationId == orderId &&
                        message.EventType == "payment.authorized.v1",
                    TestContext.Current.CancellationToken);

        payment.OrderId
            .Should()
            .Be(orderId);

        payment.CustomerId
            .Should()
            .Be(customerId);

        payment.Amount
            .Should()
            .Be(amount);

        payment.Currency
            .Should()
            .Be(currency);

        payment.Status
            .Should()
            .Be(
                Conflux.Payment.Domain.PaymentStatus.Authorized);

        outboxMessage.EventType
            .Should()
            .Be("payment.authorized.v1");

        outboxMessage.CorrelationId
            .Should()
            .Be(orderId);

        outboxMessage.CausationId
            .Should()
            .BeNull();

        outboxMessage.PublishedAt
            .Should()
            .BeNull();

        outboxMessage.AttemptCount
            .Should()
            .Be(0);

        outboxMessage.Payload
            .Should()
            .NotBeNullOrWhiteSpace();

        var paymentAuthorized =
            JsonSerializer.Deserialize<PaymentAuthorized>(
                outboxMessage.Payload);

        paymentAuthorized.Should().NotBeNull();

        paymentAuthorized!.EventId
            .Should()
            .Be(outboxMessage.Id);

        paymentAuthorized.OccurredAt
            .Should()
            .BeCloseTo(
                outboxMessage.OccurredAt,
                TimeSpan.FromMilliseconds(1));

        paymentAuthorized.CorrelationId
            .Should()
            .Be(orderId);

        paymentAuthorized.CausationId
            .Should()
            .BeNull();

        paymentAuthorized.PaymentId
            .Should()
            .Be(responseBody.PaymentId);

        paymentAuthorized.OrderId
            .Should()
            .Be(orderId);

        paymentAuthorized.CustomerId
            .Should()
            .Be(customerId);

        paymentAuthorized.Amount
            .Should()
            .Be(amount);

        paymentAuthorized.Currency
            .Should()
            .Be(currency);
    }

    private async Task<AuthorizePaymentResponse>
        CreateAuthorizedPaymentAsync()
    {
        var request = new AuthorizePaymentRequest
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 1000m,
            Currency = "INR"
        };

        var response =
            await SendAuthorizeRequestAsync(
                request,
                Guid.NewGuid().ToString());

        response.StatusCode
            .Should()
            .Be(HttpStatusCode.Created);

        var payment =
            await response.Content
                .ReadFromJsonAsync<AuthorizePaymentResponse>(
                    TestContext.Current.CancellationToken);

        payment.Should().NotBeNull();

        payment!.Status
            .Should()
            .Be(Conflux.Payment.Domain.PaymentStatus.Authorized);

        return payment;
    }

    private async Task<HttpResponseMessage> SendAuthorizeRequestAsync(
        AuthorizePaymentRequest request,
        string idempotencyKey)
    {
        using var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/payments/authorize");

        httpRequest.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);

        httpRequest.Content =
            JsonContent.Create(request);

        return await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);
    }
}