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

/// <summary>
/// HU #13286 (Épica #13202, A4) — un webhook TARDÍO de Kyverum sobre una validación que ya pasó al flujo
/// manual se responde 200 (Kyverum no reintenta) sin aplicar nada y deja la etapa
/// <c>webhook_ignorado_manual</c> en la bitácora.
/// </summary>
public sealed class KyverumWebhookManualTests
{
    private const string Secret = "whsec_abc";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly KyverumWebhookHandler _handler;

    public KyverumWebhookManualTests() =>
        _handler = new KyverumWebhookHandler(
            _repo, _protector, _kyverum, new IdentityValidationResultApplier(_events), _audit,
            NullLogger<KyverumWebhookHandler>.Instance);

    private ProcedureInstanceBiometricValidation SeedManual(
        string estado = BiometricEstados.ManualActivo, bool conSecretoResidual = false)
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
            Provider = BiometricProviders.Manual,
            // Tras A2 la fila manual NO conserva id, url ni secreto de Kyverum.
            KyverumVerificationId = null,
            CaptureUrl = null,
            WebhookSecretEncrypted = conSecretoResidual ? _protector.Protect(Secret) : null,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private static byte[] ApprovedBody() => Encoding.UTF8.GetBytes(
        "{\"evento\":\"validation.completed\",\"requestId\":\"550e8400\",\"data\":{\"aprobado\":true,"
        + "\"closedAt\":\"2026-06-23T15:30:00.000Z\",\"subjects\":[{\"id\":\"66824abc\",\"rol\":\"comprador\","
        + "\"documento\":\"123\",\"status\":\"aprobado\",\"score\":90}]},\"deliveryId\":\"7c9e6679\","
        + "\"ts\":\"2026-06-23T15:30:01.000Z\"}");

    [Theory]
    [InlineData(BiometricEstados.ManualActivo)]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    public async Task Webhook_OnManualRow_WithoutSecret_Returns200AndAuditsIgnored(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var v = SeedManual(estado);
        var body = ApprovedBody();

        var (result, error) = await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, "sha256=deadbeef"), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(estado);
        v.Provider.Should().Be(BiometricProviders.Manual);
        v.ValidatedAt.Should().BeNull();
        await _audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e =>
                e.Stage == IdentityValidationAuditStages.WebhookIgnoradoManual
                && e.ValidationId == v.Id && e.HttpStatus == 200),
            ct);
        await _audit.DidNotReceive().LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e => e.Stage == IdentityValidationAuditStages.WebhookNotVerifiable), ct);
        await _kyverum.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, default!, ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, ct);
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Webhook_OnManualRow_EvenWithValidSignature_DoesNotApplyApproval()
    {
        // Defensa en profundidad: aunque quedara un secreto residual y el webhook trajera una firma válida,
        // la fila manual no la decide Kyverum.
        var ct = TestContext.Current.CancellationToken;
        var v = SeedManual(conSecretoResidual: true);
        var body = ApprovedBody();

        var (result, error) = await _handler.HandleAsync(
            new KyverumWebhookInput(v.Id, body, "sha256=" + KyverumWebhookVerifier.ComputeHmac(body, Secret)), ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.ValidatedAt.Should().BeNull();
        v.Score.Should().BeNull();
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, ct);
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
        await _audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e => e.Stage == IdentityValidationAuditStages.WebhookIgnoradoManual), ct);
    }

    [Fact]
    public async Task Webhook_IgnoredAuditEntry_CarriesNoPayloadNorSecretsNorSignature()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = SeedManual(conSecretoResidual: true);
        var body = ApprovedBody();
        var signature = "sha256=" + KyverumWebhookVerifier.ComputeHmac(body, Secret);
        IdentityValidationAuditEntry? ignored = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e =>
        {
            if (e.Stage == IdentityValidationAuditStages.WebhookIgnoradoManual) ignored = e;
        }), ct);

        await _handler.HandleAsync(new KyverumWebhookInput(v.Id, body, signature), ct);

        ignored.Should().NotBeNull();
        IdentityValidationAuditStages.WebhookIgnoradoManual.Length.Should().BeLessThanOrEqualTo(40);
        var flat = $"{ignored!.Message}|{ignored.Detail}|{ignored.ProviderStatus}|{ignored.ErrorType}";
        flat.Should().NotContain(Secret).And.NotContain(signature).And.NotContain("66824abc").And.NotContain("score");
        ignored.TenantId.Should().Be(v.TenantId);
        ignored.ProcedureInstanceId.Should().Be(v.ProcedureInstanceId);
    }

    [Fact]
    public async Task Webhook_OnKyverumRow_StillAppliesAsBefore()
    {
        // AC4 — la rama manual no altera el comportamiento de una validación Kyverum normal.
        var ct = TestContext.Current.CancellationToken;
        var v = SeedManual();
        v.Provider = BiometricProviders.Kyverum;
        v.Status = BiometricEstados.EnProceso;
        v.KyverumVerificationId = "kyv_123";
        v.WebhookSecretEncrypted = _protector.Protect(Secret);
        var body = ApprovedBody();

        var (_, error) = await _handler.HandleAsync(
            new KyverumWebhookInput(v.Id, body, "sha256=" + KyverumWebhookVerifier.ComputeHmac(body, Secret)), ct);

        error.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        await _audit.DidNotReceive().LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e => e.Stage == IdentityValidationAuditStages.WebhookIgnoradoManual), ct);
    }
}
