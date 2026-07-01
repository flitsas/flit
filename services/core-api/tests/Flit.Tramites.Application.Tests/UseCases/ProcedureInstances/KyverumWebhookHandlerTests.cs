using System.Text;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class KyverumWebhookHandlerTests
{
    private const string Secret = "whsec_abc";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly KyverumWebhookHandler _handler;

    public KyverumWebhookHandlerTests() =>
        _handler = new KyverumWebhookHandler(_repo, _protector, _events);

    private ProcedureInstanceBiometricValidation Seed(string estado = BiometricEstados.EnProceso)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Juan",
            DocumentType = "CC",
            DocumentNumber = "123",
            Email = "j@x.com",
            Status = estado,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv_123",
            // Guardado "cifrado" con el mismo fake protector.
            WebhookSecretEncrypted = _protector.Protect(Secret),
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    // Cuerpo del webhook según el contrato real (evento + data.aprobado + subjects[]).
    private static byte[] Body(bool aprobado, int score = 88)
    {
        var evento = aprobado ? "validation.completed" : "validation.rejected";
        var status = aprobado ? "aprobado" : "rechazado";
        var json =
            "{\"evento\":\"" + evento + "\",\"requestId\":\"550e8400\",\"data\":{\"aprobado\":"
            + (aprobado ? "true" : "false")
            + ",\"closedAt\":\"2026-06-23T15:30:00.000Z\",\"subjects\":[{\"id\":\"66824abc\",\"rol\":\"comprador\",\"documento\":\"123\",\"status\":\""
            + status + "\",\"score\":" + score
            + ",\"datosExtraidos\":{\"nombres\":\"ANDRES FELIPE\",\"apellidos\":\"PEREZ GOMEZ\"}}]},\"deliveryId\":\"7c9e6679\",\"ts\":\"2026-06-23T15:30:01.000Z\"}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static string Sign(byte[] body) => KyverumWebhookVerifier.ComputeHmac(body, Secret);

    [Fact]
    public async Task Webhook_ValidSignatureApproved_UpdatesStateAndEmitsCompleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        var body = Body(aprobado: true, score: 92);

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sha256=" + Sign(body)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().NotBeNull();
        v.ProviderStatus.Should().Be("validation.completed");
        v.Score.Should().Be(92);
        // Payload sanitizado: sin OCR/PII.
        v.ProviderPayload.Should().NotContain("ANDRES FELIPE").And.NotContain("datosExtraidos");
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationCompleted>(e =>
            e.ValidationId == v.Id && e.Estado == BiometricEstados.Aprobado), ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Webhook_Rejected_SetsRejectedWithoutValidadoAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        var body = Body(aprobado: false);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.ValidatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns401AndNoChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        var body = Body(aprobado: true);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sha256=deadbeef"), ct);

        error.Should().Be("firma_invalida");
        // AC3: sin cambios en BD ni evento.
        v.Status.Should().Be(BiometricEstados.EnProceso);
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_AlreadyTerminal_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(estado: BiometricEstados.Aprobado);
        var body = Body(aprobado: true);

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        // No re-emite evento ni vuelve a persistir.
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_UnknownValidation_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        var body = Body(aprobado: true);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(Guid.NewGuid(), body, Sign(body)), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Webhook_EmptyBody_ReturnsInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(Guid.NewGuid(), [], "sig"), ct);
        error.Should().Be("cuerpo_invalido");
    }
}
