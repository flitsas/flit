using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13284 (Feature #13280 A2, Épica #13202) — <see cref="ActivarIdentidadManualHandler"/>: activa el flujo manual sobre la
/// MISMA fila (trámite y prevalidación standalone), responde 409 sobre una identidad aprobada y vigente, cancela la
/// verificación Kyverum en curso, audita sin secretos y entrega el token en claro solo al puerto del notificador.
/// <para>Uso: <c>await handler.HandleAsync(new ActivarIdentidadManualCommand(validationId, userId), ct)</c>.</para>
/// </summary>
public sealed class ActivarIdentidadManualHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly IManualCaptureLinkNotifier _notifier = Substitute.For<IManualCaptureLinkNotifier>();

    private ActivarIdentidadManualHandler Handler() => new(_repo, _audit, _notifier, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status, string provider = BiometricProviders.Kyverum, Guid? instanceId = null, string? tramiteStatus = null)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = instanceId,
            ProcedureInstance = instanceId is null || tramiteStatus is null
                ? null
                : new ProcedureInstance { Id = instanceId.Value, Status = tramiteStatus },
            PersonId = instanceId is null ? Guid.NewGuid() : null,
            PartyRole = instanceId is null ? null : BiometricRules.ParteComprador,
            Status = status,
            Provider = provider,
            TokenHash = new string('0', 64),
            ExpiresAt = Now.AddHours(-2),
            KyverumVerificationId = provider == BiometricProviders.Kyverum ? "kyv_ext_9" : null,
            CaptureUrl = provider == BiometricProviders.Kyverum ? "https://captura.example.test/x" : null,
            WebhookSecretEncrypted = provider == BiometricProviders.Kyverum ? "cifrado" : null,
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    // ── AC1 — activación exitosa (trámite) ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    [InlineData(BiometricEstados.EnProceso)]
    public async Task AC1_ValidacionDeTramiteNoAprobada_PasaAManualActivo_ConTokenDe24hGuardadoComoHash(string origen)
    {
        var v = Fila(origen, instanceId: Guid.NewGuid(), tramiteStatus: TramiteEstado.Entregado);
        ManualCaptureLink? enviado = null;
        await _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => enviado = l), Arg.Any<CancellationToken>());

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("manual");
        result.Status.Should().Be("manual_activo");
        result.ExpiresAt.Should().Be(Now.AddHours(24));
        v.Provider.Should().Be(BiometricProviders.Manual);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.ManualActivatedBy.Should().Be(User);
        v.ManualActivatedAt.Should().Be(Now);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // El token en claro solo llega al notificador; en la fila hay únicamente su hash SHA-256.
        enviado.Should().NotBeNull();
        enviado!.Token.Should().NotBeNullOrWhiteSpace();
        v.TokenHash.Should().Be(BiometricToken.Hash(enviado.Token)).And.NotBe(enviado.Token);
        enviado.ExpiresAt.Should().Be(Now.AddHours(24));
        enviado.TenantId.Should().Be(v.TenantId);
    }

    [Fact]
    public async Task AC1_Audita_manual_activado_ConUsuario_SinTokenNiSecretos()
    {
        var v = Fila(BiometricEstados.Rechazado, instanceId: Guid.NewGuid(), tramiteStatus: TramiteEstado.Entregado);
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());
        string? token = null;
        await _notifier.NotifyAsync(Arg.Do<ManualCaptureLink>(l => token = l.Token), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        var activado = entradas.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.ManualActivado).Subject;
        activado.TenantId.Should().Be(v.TenantId);
        activado.ValidationId.Should().Be(v.Id);
        activado.Detail.Should().Contain(User.ToString());
        entradas.Should().NotContain(e => e.Stage == IdentityValidationAuditStages.KyverumCanceladoPorManual,
            "una rechazada no tiene nada en vuelo en Kyverum");
        foreach (var e in entradas)
        {
            (e.Message + e.Detail).Should().NotContain(token!).And.NotContain("cifrado").And.NotContain("captura.example");
        }
    }

    // ── AC2 — aprobada y vigente: 409 y la fila no cambia ───────────────────────────────────────

    [Fact]
    public async Task AC2_AprobadaYVigente_Responde409_YNoCambiaNada()
    {
        var v = Fila(BiometricEstados.Aprobado, instanceId: Guid.NewGuid(), tramiteStatus: TramiteEstado.Entregado);
        v.ValidatedAt = Now.AddDays(-2);
        v.ValidUntil = Now.AddDays(28);

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be(ActivarIdentidadManualHandler.AprobadaVigente).And.Be("identidad_aprobada_vigente");
        v.Provider.Should().Be(BiometricProviders.Kyverum);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.KyverumVerificationId.Should().Be("kyv_ext_9");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ManualCaptureLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AprobadaVencida_SiSeActiva()
    {
        var v = Fila(BiometricEstados.Aprobado, instanceId: Guid.NewGuid(), tramiteStatus: TramiteEstado.Entregado);
        v.ValidatedAt = Now.AddDays(-45);
        v.ValidUntil = Now.AddDays(-15);

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be("manual_activo");
        result.KyverumCancelado.Should().BeFalse("la aprobada vencida es terminal: no había nada en vuelo");
    }

    // ── AC4 — prevalidación standalone ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_PrevalidacionStandalone_SeActiva_YSigueLigadaASuPersona()
    {
        var v = Fila(BiometricEstados.EnProceso, instanceId: null);
        var persona = v.PersonId;

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.ProcedureInstanceId.Should().BeNull();
        v.ProcedureInstanceId.Should().BeNull();
        v.PersonId.Should().Be(persona);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
    }

    // ── AC5 — Kyverum en curso se cancela ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(BiometricEstados.Enviado)]
    [InlineData(BiometricEstados.EnProceso)]
    [InlineData(BiometricEstados.PendienteEnvio)]
    [InlineData(BiometricEstados.ErrorEnvio)]
    public async Task AC5_KyverumEnCurso_QuedaCancelada_ConIdSoloEnLaAuditoria(string origen)
    {
        var v = Fila(origen, instanceId: Guid.NewGuid(), tramiteStatus: TramiteEstado.Entregado);
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());

        var (result, _) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        result!.KyverumCancelado.Should().BeTrue();
        v.KyverumVerificationId.Should().BeNull();
        v.CaptureUrl.Should().BeNull();
        v.WebhookSecretEncrypted.Should().BeNull();
        var cancelado = entradas.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.KyverumCanceladoPorManual).Subject;
        cancelado.KyverumVerificationId.Should().Be("kyv_ext_9", "el id externo se conserva solo como trazabilidad");
        cancelado.Detail.Should().Contain(origen);
        entradas.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.ManualActivado);
    }

    // ── Bordes ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidacionInexistente_DevuelveNotFound()
    {
        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(Guid.NewGuid(), User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Theory]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task TramiteAnuladoORevocado_NoSeActiva_ConservaLaValidacion(string estadoTramite)
    {
        var v = Fila(BiometricEstados.Rechazado, instanceId: Guid.NewGuid(), tramiteStatus: estadoTramite);

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("tramite_inactivo");
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.Provider.Should().Be(BiometricProviders.Kyverum);
    }

    [Fact]
    public async Task ReactivarUnaFilaManual_GeneraUnTokenNuevo()
    {
        var v = Fila(BiometricEstados.ManualActivo, provider: BiometricProviders.Manual, instanceId: Guid.NewGuid(),
            tramiteStatus: TramiteEstado.Entregado);
        var hashViejo = v.TokenHash;

        var (result, error) = await Handler().HandleAsync(new ActivarIdentidadManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        result!.KyverumCancelado.Should().BeFalse();
        v.TokenHash.Should().NotBe(hashViejo);
        v.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
