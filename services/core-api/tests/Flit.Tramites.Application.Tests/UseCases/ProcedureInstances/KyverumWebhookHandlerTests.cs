using System.Text;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class KyverumWebhookHandlerTests
{
    private const string Secret = "whsec_abc";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly KyverumWebhookHandler _handler;

    public KyverumWebhookHandlerTests() =>
        _handler = new KyverumWebhookHandler(
            _repo, _protector, _kyverum, new IdentityValidationResultApplier(_events),
            Substitute.For<IIdentityValidationAuditLog>(), NullLogger<KyverumWebhookHandler>.Instance);

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
        // Simula el UPDATE atómico del conteo contra el `v` en memoria: cuenta (incrementa + sella la clave +
        // reinicia el presupuesto de sondeo) solo si es un intento NUEVO y sigue en proceso; un redelivery del
        // mismo intento (misma clave) devuelve false, igual que la guarda `last_attempt_at <> @key` en la BD.
        _repo.TryCountKyverumAttemptAsync(v.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(1);
                if (v.Status != BiometricEstados.EnProceso
                    || string.Equals(key, v.LastAttemptAt, StringComparison.Ordinal))
                    return false;
                v.Attempts += 1;
                v.LastAttemptAt = key;
                v.ReconcilePollCount = 0;
                return true;
            });
        return v;
    }

    // `closedAt` del intento en el cuerpo: es la CLAVE de dedup del conteo (webhook autoritativo).
    private const string AttemptClosedAt = "2026-06-23T15:30:00.000Z";

    // Serie/hash del certificado que reporta Kyverum en el subject aprobado (HU #10488).
    private const string FirmaSerie = "FS-9A3F21C7";

    // Cuerpo del webhook según el contrato real (evento + data.aprobado + subjects[]). `closedAt` parametrizable
    // para simular intentos distintos (clave de dedup) vs. redeliveries del mismo intento (misma clave).
    private static byte[] Body(bool aprobado, int score = 88, string closedAt = AttemptClosedAt)
    {
        var evento = aprobado ? "validation.completed" : "validation.rejected";
        var status = aprobado ? "aprobado" : "rechazado";
        var json =
            "{\"evento\":\"" + evento + "\",\"requestId\":\"550e8400\",\"data\":{\"aprobado\":"
            + (aprobado ? "true" : "false")
            + ",\"closedAt\":\"" + closedAt + "\",\"subjects\":[{\"id\":\"66824abc\",\"rol\":\"comprador\",\"documento\":\"123\",\"status\":\""
            + status + "\",\"score\":" + score + ",\"firmaSerie\":\"" + FirmaSerie + "\""
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
    public async Task Webhook_Approved_PersistsCertificateHashFromFirmaSerie()
    {
        // HU #10488 — la serie/hash del certificado (firmaSerie del subject aprobado) se persiste como
        // CertificateHash para alimentar el sello de identidad del FUR.
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        var body = Body(aprobado: true);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sha256=" + Sign(body)), ct);

        error.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.CertificateHash.Should().Be(FirmaSerie);
    }

    [Fact]
    public async Task Webhook_RejectedAttempt_WithAttemptsRemaining_StaysEnProceso_IncrementsCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        v.MaxAttempts = 3;
        v.Attempts = 0;
        v.ReconcilePollCount = 2; // presupuesto de sondeo consumido; el intento nuevo debe reiniciarlo
        // Kyverum reporta un INTENTO rechazado (rechazado_intento), pero quedan reintentos. NO terminaliza:
        // el webhook cuenta el intento (por el cuerpo) y sigue en_proceso para que el cliente reintente en su móvil.
        // La consulta de estado solo enriquece el motivo (ya no cuenta).
        _kyverum.GetStatusAsync(v.KyverumVerificationId!, v.PartyRole, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("rechazado_intento", 0, "{\"validado_at\":\"t1\"}", AttemptAt: "t1", Motivo: "rostro no visible"));
        var body = Body(aprobado: false);

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.EnProceso);
        v.Attempts.Should().Be(1);
        v.LastAttemptAt.Should().Be(AttemptClosedAt); // dedup por la clave del cuerpo
        v.ReconcilePollCount.Should().Be(0); // intento nuevo → presupuesto de sondeo del worker reiniciado
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_RejectedAttempt_WhenAttemptsExhausted_SetsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        v.MaxAttempts = 3;
        v.Attempts = 2; // ya se usaron 2; este es el 3º y último
        _kyverum.GetStatusAsync(v.KyverumVerificationId!, v.PartyRole, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("rechazado_intento", 0, "{\"validado_at\":\"t3\"}", AttemptAt: "t3", Motivo: "rostro no visible"));
        var body = Body(aprobado: false);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        v.Attempts.Should().Be(3);
        v.Status.Should().Be(BiometricEstados.Rechazado); // intentos agotados → rechazo terminal
    }

    [Fact]
    public async Task Webhook_RejectedAttempt_SameAttemptTwice_NotDoubleCounted()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        v.MaxAttempts = 3;
        v.Attempts = 1;
        v.LastAttemptAt = AttemptClosedAt; // el intento (por su closedAt) ya fue contado
        var body = Body(aprobado: false); // redelivery del MISMO evento (mismo closedAt)

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Attempts.Should().Be(1); // mismo intento (mismo closedAt) → NO re-cuenta (dedup por cuerpo)
        v.Status.Should().Be(BiometricEstados.EnProceso);
        // El redelivery se descarta ANTES de consultar el estado (no gasta llamada al proveedor).
        await _kyverum.DidNotReceive().GetStatusAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_RejectedAttempt_DistinctAttempt_CountsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        v.MaxAttempts = 3;
        v.Attempts = 1;
        v.LastAttemptAt = AttemptClosedAt; // primer intento ya contado
        _kyverum.GetStatusAsync(v.KyverumVerificationId!, v.PartyRole, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("rechazado_intento", 0, "{\"validado_at\":\"t2\"}", AttemptAt: "t2", Motivo: "rostro no visible"));
        // Segundo intento REAL: closedAt distinto → clave distinta → se cuenta.
        var body = Body(aprobado: false, closedAt: "2026-06-23T15:40:00.000Z");

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, Sign(body)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Attempts.Should().Be(2); // intento nuevo → cuenta
        v.LastAttemptAt.Should().Be("2026-06-23T15:40:00.000Z");
        v.Status.Should().Be(BiometricEstados.EnProceso);
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

    [Fact]
    public async Task Webhook_SecretUndecryptable_ReconcilesViaPollAndApproves()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed(); // en_proceso, con kyverum_verification_id "kyv_123"
        _protector.ThrowOnUnprotect = true; // keyring roto ⇒ no se puede descifrar el secreto
        _kyverum.GetStatusAsync("kyv_123", "comprador", Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("aprobado", 90, "{}"));
        var body = Body(aprobado: true);

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sha256=whatever"), ct);

        // NO 500: se reconcilia por consulta autenticada y se aplica el resultado real.
        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.Score.Should().Be(90);
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationCompleted>(e =>
            e.ValidationId == v.Id && e.Estado == BiometricEstados.Aprobado), ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Webhook_SecretUndecryptable_ProviderTransient_ReturnsRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        _protector.ThrowOnUnprotect = true;
        _kyverum.GetStatusAsync("kyv_123", "comprador", Arg.Any<CancellationToken>())
            .Returns<KyverumVerifyStatus?>(_ => throw new KyverumVerifyException("down", transient: true));
        var body = Body(aprobado: true);

        var (_, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sig"), ct);

        // Fallo transitorio del proveedor ⇒ pide reintento (503) para que Kyverum reintente el webhook.
        error.Should().Be("reintentar");
    }
}
