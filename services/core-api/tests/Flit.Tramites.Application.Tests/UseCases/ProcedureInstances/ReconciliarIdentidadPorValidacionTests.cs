using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// L1 — reconciliar con Kyverum Verify por <c>validationId</c>, sin exigir trámite: sirve para la validación ligada a
/// un trámite y para la prevalidación (<c>ProcedureInstanceId</c> nulo). En release el expirado aplicado por esta ruta
/// solo deja la auditoría (sin frase en bitácora: depende de la HU #14179) y no publica <see cref="IdentityValidationCompleted"/>.
/// <para>Uso de ejemplo:
/// <c>var (r, err) = await handler.HandleByValidationAsync(tenantId, validationId, ct);</c> → <c>r.Status</c>,
/// <c>r.Updated</c>; <c>err</c> ∈ { not_found, no_kyverum, proveedor_error, proveedor_no_disponible, persist_error }.</para>
/// </summary>
public sealed class ReconciliarIdentidadPorValidacionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly List<ProcedureInstanceEvent> _bitacora = [];
    private readonly ReconciliarIdentidadHandler _handler;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _validation = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ReconciliarIdentidadPorValidacionTests()
    {
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(_bitacora.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _handler = new ReconciliarIdentidadHandler(
            _repo, _kyverum, new IdentityValidationResultApplier(_events, _repo), _audit);
    }

    private ProcedureInstanceBiometricValidation Seed(
        Guid? procedureInstanceId,
        string status = BiometricEstados.Enviado,
        string provider = BiometricProviders.Kyverum,
        string? verificationId = "kyv-1",
        string? partyRole = "comprador")
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = _validation,
            TenantId = _tenant,
            ProcedureInstanceId = procedureInstanceId,
            PartyRole = partyRole,
            Status = status,
            Provider = provider,
            KyverumVerificationId = verificationId,
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "x@y.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private void KyverumResponde(string status, string? firmaSerie = null, string? partyRole = "comprador") =>
        _kyverum.GetStatusAsync("kyv-1", partyRole, Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus(status, 90, "{\"status\":\"" + status + "\"}", FirmaSerie: firmaSerie));

    // ── L1 ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task L1_PrevalidacionAprobadaEnVerify_AplicaConVigenciaYSello_PublicaEvento_YAuditaSinTramite()
    {
        var v = Seed(procedureInstanceId: null, partyRole: null);
        KyverumResponde("aprobado", firmaSerie: "FS-PRE-1", partyRole: null);

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        error.Should().BeNull();
        result.Should().Be(new ReconciliarIdentidadResult(BiometricEstados.Aprobado, true));
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidUntil.Should().NotBeNull();
        v.CertificateHash.Should().Be("FS-PRE-1");
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationCompleted>(e =>
            e.ValidationId == _validation && e.ProcedureInstanceId == null && e.Estado == BiometricEstados.Aprobado), Ct);
        await _audit.Received(1).LogAsync(Arg.Is<IdentityValidationAuditEntry>(a =>
            a.Stage == IdentityValidationAuditStages.Reconcile
            && a.Outcome == BiometricEstados.Aprobado
            && a.ProcedureInstanceId == null
            && a.ValidationId == _validation), Ct);
        await _repo.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task L1_ValidacionConTramite_PorLaRutaNueva_AplicaLaMismaLogicaQueElWizard()
    {
        var v = Seed(procedureInstanceId: _instance, status: BiometricEstados.EnProceso);
        KyverumResponde("aprobado");

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        error.Should().BeNull();
        result.Should().Be(new ReconciliarIdentidadResult(BiometricEstados.Aprobado, true));
        v.Status.Should().Be(BiometricEstados.Aprobado);
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationCompleted>(e =>
            e.ValidationId == _validation && e.ProcedureInstanceId == _instance), Ct);
        _bitacora.Should().BeEmpty(); // release: sin frases de bitácora de identidad (HU #14179 no está en release)
    }

    [Fact]
    public async Task L1_VerifyAunNoResuelve_UpdatedFalse_YReiniciaElPresupuestoDeSondeo()
    {
        var v = Seed(procedureInstanceId: null, partyRole: null);
        v.ReconcilePollCount = 4;
        KyverumResponde("enviado", partyRole: null);

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        error.Should().BeNull();
        result.Should().Be(new ReconciliarIdentidadResult(BiometricEstados.Enviado, false));
        v.ReconcilePollCount.Should().Be(0);
        await _repo.Received(1).SaveChangesAsync(Ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task L1_ValidacionInexistente_NotFound_SinConsultarAVerify()
    {
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _kyverum.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, default, Ct);
    }

    [Fact]
    public async Task L1_ValidacionDeOtroTenant_NotFound_SinConsultarAVerify()
    {
        Seed(procedureInstanceId: _instance);

        var (result, error) = await _handler.HandleByValidationAsync(Guid.NewGuid(), _validation, Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _kyverum.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, default, Ct);
    }

    [Theory]
    [InlineData(BiometricProviders.Mock, null)]
    [InlineData(BiometricProviders.Manual, null)]
    [InlineData(BiometricProviders.Kyverum, null)] // kyverum sin id de verificación: no hay qué consultar
    public async Task L1_ValidacionMockOManual_NoKyverum(string provider, string? verificationId)
    {
        Seed(procedureInstanceId: null, provider: provider, verificationId: verificationId);

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        result.Should().BeNull();
        error.Should().Be("no_kyverum"); // el endpoint lo mapea a 409
        await _kyverum.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, default, Ct);
    }

    [Fact]
    public async Task L1_ProveedorCaido_ProveedorNoDisponible_YAuditaElFallo()
    {
        Seed(procedureInstanceId: null, partyRole: null);
        _kyverum.GetStatusAsync("kyv-1", null, Arg.Any<CancellationToken>())
            .Returns<KyverumVerifyStatus?>(_ => throw new KyverumVerifyException("down", transient: true));

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        result.Should().BeNull();
        error.Should().Be("proveedor_no_disponible"); // el endpoint lo mapea a 503
        await _audit.Received(1).LogAsync(Arg.Is<IdentityValidationAuditEntry>(a =>
            a.Stage == IdentityValidationAuditStages.Reconcile
            && a.Outcome == IdentityValidationAuditOutcomes.Error
            && a.HttpStatus == 503
            && a.ValidationId == _validation), Ct);
    }

    // ── Expirado por la reconciliación manual (release: sin frase en bitácora) ─────────────────────────────

    [Fact]
    public async Task Expirado_ConTramite_AuditaSinBitacoraNiEvento()
    {
        var v = Seed(procedureInstanceId: _instance, status: BiometricEstados.EnProceso);
        KyverumResponde("expirado");

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        error.Should().BeNull();
        result.Should().Be(new ReconciliarIdentidadResult(BiometricEstados.Expirado, true));
        v.Status.Should().Be(BiometricEstados.Expirado);
        _bitacora.Should().BeEmpty();
        await _audit.Received(1).LogAsync(Arg.Is<IdentityValidationAuditEntry>(a =>
            a.Stage == IdentityValidationAuditStages.Reconcile
            && a.Outcome == IdentityValidationAuditOutcomes.Expired
            && a.ProcedureInstanceId == _instance), Ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
        await _repo.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Expirado_SinTramite_SoloAuditoria_SinBitacoraNiEvento()
    {
        var v = Seed(procedureInstanceId: null, partyRole: null);
        KyverumResponde("expirado", partyRole: null);

        var (result, error) = await _handler.HandleByValidationAsync(_tenant, _validation, Ct);

        error.Should().BeNull();
        result.Should().Be(new ReconciliarIdentidadResult(BiometricEstados.Expirado, true));
        v.Status.Should().Be(BiometricEstados.Expirado);
        _bitacora.Should().BeEmpty();
        await _repo.DidNotReceiveWithAnyArgs().AddEventAsync(default!, Ct);
        await _audit.Received(1).LogAsync(Arg.Is<IdentityValidationAuditEntry>(a =>
            a.Outcome == IdentityValidationAuditOutcomes.Expired && a.ProcedureInstanceId == null), Ct);
        await _events.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }
}
