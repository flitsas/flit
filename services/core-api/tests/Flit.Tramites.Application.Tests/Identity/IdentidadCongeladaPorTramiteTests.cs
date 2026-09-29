using System.Text;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Bug #13055 — si el trámite está anulado o revocado, un resultado tardío del proveedor (webhook,
/// consulta, expiración) NO cambia la validación de identidad: conserva el estado que tenía. Una
/// prevalidación standalone (sin trámite) y un trámite aprobado siguen el flujo normal.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// v.ProcedureInstance = new ProcedureInstance { Status = TramiteEstado.Anulado };
/// await applier.ApplyAsync(v, aprobado, now); // false: v.Status sigue en en_proceso
/// </code>
/// </summary>
public sealed class IdentidadCongeladaPorTramiteTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IdentityValidationTerminalResult Aprobado => new(true, "aprobado", "{}", 90, "FS-1");

    // ── Applier: punto único de resultados terminales ───────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task Applier_TramiteAnuladoORevocado_NoCambiaLaValidacion_NiEmiteEvento(string estado)
    {
        var v = Validacion(BiometricEstados.EnProceso, estado);

        var aplicado = await Applier().ApplyAsync(v, Aprobado, Now, Ct);

        aplicado.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.EnProceso, "el registro está anulado: la VID queda en su estado viejo");
        v.ValidatedAt.Should().BeNull();
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationCompleted>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applier_TramiteAnulado_NoEnriqueceElHashDeUnaAprobada()
    {
        var v = Validacion(BiometricEstados.Aprobado, TramiteEstado.Anulado);

        await Applier().ApplyAsync(v, Aprobado, Now, Ct);

        v.CertificateHash.Should().BeNull();
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    public async Task Applier_TramiteNoCongelado_AplicaElResultado(string estado)
    {
        var v = Validacion(BiometricEstados.EnProceso, estado);

        var aplicado = await Applier().ApplyAsync(v, Aprobado, Now, Ct);

        aplicado.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task Applier_PrevalidacionStandalone_NuncaSeCongela()
    {
        var v = Validacion(BiometricEstados.EnProceso, estadoTramite: null);

        var aplicado = await Applier().ApplyAsync(v, Aprobado, Now, Ct);

        aplicado.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
    }

    // ── Reconciliador: expiración y refresco de intento (no pasan por el applier) ───────────────

    [Theory]
    [InlineData("expirado")]
    [InlineData("rechazado_intento")]
    [InlineData("rechazado")]
    public async Task Reconciliador_TramiteAnulado_NoTocaLaValidacion(string estadoProveedor)
    {
        var v = Validacion(BiometricEstados.EnProceso, TramiteEstado.Anulado);
        v.MaxAttempts = 3;
        var status = new KyverumVerifyStatus(estadoProveedor, 40, "{\"status\":\"x\"}");

        var cambio = await IdentityValidationReconciler.ApplyStatusAsync(Applier(), v, status, Now, Ct);

        cambio.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.EnProceso);
        v.ProviderPayload.Should().BeNull();
    }

    // ── Webhook de Kyverum ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_TramiteRevocado_Responde200SinContarIntentos_YAudita()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var audit = Substitute.For<IIdentityValidationAuditLog>();
        var v = Validacion(BiometricEstados.EnProceso, TramiteEstado.Revocado);
        repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        var handler = new KyverumWebhookHandler(
            repo, Substitute.For<IWebhookSecretProtector>(), Substitute.For<IKyverumVerifyClient>(),
            Applier(), audit, NullLogger<KyverumWebhookHandler>.Instance);

        var (result, error) = await handler.HandleAsync(
            new KyverumWebhookInput(v.Id, Encoding.UTF8.GetBytes("{\"event\":\"validation.completed\"}"), "sig"), Ct);

        result.Should().Be("ok", "un error haría que Kyverum reintentara indefinidamente");
        error.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.EnProceso);
        await repo.DidNotReceive().TryCountKyverumAttemptAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e => e.Outcome == IdentityValidationAuditOutcomes.TramiteInactivo),
            Arg.Any<CancellationToken>());
    }

    // ── Flujos simulados (DEV/QA) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompletarBiometriaMock_TramiteAnulado_RechazaElIntento()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var v = Validacion(BiometricEstados.Enviado, TramiteEstado.Anulado);
        v.ExpiresAt = Now.AddYears(10);
        repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(v);
        var handler = new CompletarBiometriaHandler(
            repo, Substitute.For<Flit.Tramites.Application.Storage.IAttachmentStorage>(),
            Substitute.For<Flit.Tramites.Application.Biometrics.IBiometricScorer>());

        var (_, error) = await handler.HandleAsync("token", new CompletarBiometriaInput(null, null, null), Ct);

        error.Should().Be("tramite_inactivo");
        v.Status.Should().Be(BiometricEstados.Enviado);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private IdentityValidationResultApplier Applier() => new(_events);

    private static ProcedureInstanceBiometricValidation Validacion(string estado, string? estadoTramite)
    {
        var instanceId = estadoTramite is null ? (Guid?)null : Guid.NewGuid();
        return new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = instanceId,
            ProcedureInstance = estadoTramite is null
                ? null
                : new ProcedureInstance { Id = instanceId!.Value, Status = estadoTramite },
            PartyRole = "comprador",
            Name = "Juan",
            DocumentType = "CC",
            DocumentNumber = "123",
            Status = estado,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv_123",
            TokenHash = "h",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddHours(-1),
        };
    }
}
