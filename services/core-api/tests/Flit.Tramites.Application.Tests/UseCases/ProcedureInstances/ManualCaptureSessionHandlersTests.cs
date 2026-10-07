using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13289 (Feature #13281 B, Épica #13202) — <see cref="GetManualCaptureHandler"/> y
/// <see cref="RegistrarConsentimientoManualHandler"/>: estados del token (vigente, vencido, usado, regenerado, inexistente,
/// de otro proveedor), vista sin datos extra, constancia de consentimiento (sobrescribe un ciclo previo), IP del servidor y
/// auditoría sin PII ni IP.
/// <para>Uso: <c>await new GetManualCaptureHandler(repo, clock).HandleAsync(token, ct)</c>.</para>
/// </summary>
public sealed class ManualCaptureSessionHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Token = "token-de-prueba-del-flujo-manual";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();

    private GetManualCaptureHandler Get() => new(_repo, new FixedTime(Now));
    private RegistrarConsentimientoManualHandler Consent() => new(_repo, _audit, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        bool tramite = false,
        string? tramiteStatus = null)
    {
        var instanceId = tramite ? Guid.NewGuid() : (Guid?)null;
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureInstanceId = instanceId,
            ProcedureInstance = instanceId is null
                ? null
                : new ProcedureInstance
                {
                    Id = instanceId.Value,
                    Status = tramiteStatus ?? TramiteEstado.Entregado,
                    ProcedureType = new ProcedureType { Name = "Matrícula inicial" },
                },
            PersonId = tramite ? null : Guid.NewGuid(),
            Name = "Ana Prueba",
            DocumentType = "CC",
            DocumentNumber = "900111222",
            Email = "ana@example.test",
            Status = status,
            Provider = provider,
            TokenHash = BiometricToken.Hash(Token),
            ExpiresAt = expiresAt ?? Now.AddHours(20),
            ManualActivatedAt = Now.AddHours(-4),
        };
        _repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(Token), Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    // ── GET: AC1 sesión válida ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_sesion_vigente_de_tramite_devuelve_solo_lo_del_contrato()
    {
        var v = Fila(tramite: true);

        var (view, error) = await Get().HandleAsync(Token, Ct);

        error.Should().BeNull();
        view.Should().Be(new ManualCaptureViewDto(
            "Ana Prueba", "CC", "900111222", "Matrícula inicial", v.ExpiresAt, ManualCaptureConsent.TextVersion));
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_prevalidacion_standalone_no_trae_producto()
    {
        Fila();

        var (view, error) = await Get().HandleAsync(Token, Ct);

        error.Should().BeNull();
        view!.ProductName.Should().BeNull();
    }

    // ── GET: AC2 vencido, AC3 usado / inválido ──────────────────────────────────────────────

    [Fact]
    public async Task Get_enlace_de_mas_de_24h_responde_expirada_sin_modificar_la_fila()
    {
        var v = Fila(expiresAt: Now.AddSeconds(-1));

        var (view, error) = await Get().HandleAsync(Token, Ct);

        (view, error).Should().Be(((ManualCaptureViewDto?)null, ManualCaptureErrors.Expirada));
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    public async Task Get_enlace_ya_usado_o_resuelto_responde_estado_invalido(string estado)
    {
        Fila(estado);

        var (_, error) = await Get().HandleAsync(Token, Ct);

        error.Should().Be(ManualCaptureErrors.EstadoInvalido);
    }

    // ── HU #13299 — rechazada con enlace nuevo ──────────────────────────────────────────────

    [Fact]
    public async Task Get_rechazada_con_motivo_y_enlace_vigente_devuelve_la_sesion()
    {
        var v = Fila(BiometricEstados.Rechazado);
        v.RejectionReasonCode = "rostro_no_coincide";

        var (view, error) = await Get().HandleAsync(Token, Ct);

        error.Should().BeNull();
        view!.FullName.Should().Be("Ana Prueba");
        view.ExpiresAt.Should().Be(v.ExpiresAt);
    }

    [Fact]
    public async Task Get_rechazada_con_el_enlace_vencido_responde_expirada()
    {
        var v = Fila(BiometricEstados.Rechazado, expiresAt: Now.AddSeconds(-1));
        v.RejectionReasonCode = "rostro_no_coincide";

        (await Get().HandleAsync(Token, Ct)).Error.Should().Be(ManualCaptureErrors.Expirada);
    }

    [Fact]
    public async Task Consent_estando_rechazada_con_enlace_vigente_guarda_la_constancia_nueva()
    {
        var v = Fila(BiometricEstados.Rechazado);
        v.RejectionReasonCode = "rostro_no_coincide";
        v.ConsentAt = Now.AddDays(-5);
        v.ConsentIp = "198.51.100.9";

        var error = await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, true, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct);

        error.Should().BeNull();
        v.ConsentAt.Should().Be(Now);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.Status.Should().Be(BiometricEstados.Rechazado, "el consentimiento no cambia el estado");
    }

    [Fact]
    public async Task Get_tramite_anulado_responde_estado_invalido()
    {
        Fila(tramite: true, tramiteStatus: TramiteEstado.Anulado);

        var (_, error) = await Get().HandleAsync(Token, Ct);

        error.Should().Be(ManualCaptureErrors.EstadoInvalido);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Get_token_vacio_o_desconocido_o_regenerado_responde_not_found(string token)
    {
        // El token regenerado ya no coincide con ningún TokenHash: el repo no lo encuentra.
        _repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        (await Get().HandleAsync(token, Ct)).Error.Should().Be(ManualCaptureErrors.NotFound);
        (await Get().HandleAsync("cualquier-otro-token", Ct)).Error.Should().Be(ManualCaptureErrors.NotFound);
    }

    [Theory]
    [InlineData(BiometricProviders.Mock)]
    [InlineData(BiometricProviders.Kyverum)]
    public async Task Get_token_de_otro_proveedor_no_se_revela_como_manual(string proveedor)
    {
        Fila(BiometricEstados.Enviado, proveedor);

        var (_, error) = await Get().HandleAsync(Token, Ct);

        error.Should().Be(ManualCaptureErrors.NotFound);
    }

    // ── POST consent: AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_guarda_fecha_ip_y_version_y_audita_sin_PII_ni_IP()
    {
        var v = Fila(tramite: true);
        IdentityValidationAuditEntry? entry = null;
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(e => entry = e), Arg.Any<CancellationToken>());

        var error = await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, true, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct);

        error.Should().BeNull();
        v.ConsentAt.Should().Be(Now);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
        v.Status.Should().Be(BiometricEstados.ManualActivo, "consentir no consume el enlace");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        entry.Should().NotBeNull();
        entry!.Stage.Should().Be("manual_consentimiento");
        entry.Outcome.Should().Be(IdentityValidationAuditOutcomes.Ok);
        entry.ValidationId.Should().Be(v.Id);
        entry.TenantId.Should().Be(v.TenantId);
        var texto = $"{entry.Message} {entry.Detail}";
        texto.Should().NotContain("203.0.113.7").And.NotContain("900111222").And.NotContain("Ana").And.NotContain(Token);
    }

    [Fact]
    public async Task Consent_sobrescribe_la_constancia_de_un_ciclo_manual_previo()
    {
        var v = Fila();
        v.ConsentAt = Now.AddDays(-5);
        v.ConsentIp = "198.51.100.9";
        v.ConsentTextVersion = "version-anterior";

        var error = await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, true, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct);

        error.Should().BeNull();
        v.ConsentAt.Should().Be(Now);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
    }

    [Fact]
    public async Task Consent_no_aceptado_responde_400_y_no_guarda_nada()
    {
        var v = Fila();

        var error = await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, false, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct);

        error.Should().Be(ManualCaptureErrors.ConsentimientoNoAceptado);
        v.ConsentAt.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("version-inventada")]
    public async Task Consent_con_version_que_no_es_la_vigente_responde_400(string? version)
    {
        var v = Fila();

        var error = await Consent().HandleAsync(new RegistrarConsentimientoManualCommand(Token, true, version, "203.0.113.7"), Ct);

        error.Should().Be(ManualCaptureErrors.VersionTextoInvalida);
        v.ConsentAt.Should().BeNull();
    }

    [Fact]
    public async Task Consent_con_token_inexistente_es_404_aunque_el_cuerpo_sea_invalido()
    {
        _repo.GetBiometricByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var error = await Consent().HandleAsync(new RegistrarConsentimientoManualCommand("x", false, null, null), Ct);

        error.Should().Be(ManualCaptureErrors.NotFound);
    }

    [Fact]
    public async Task Consent_con_enlace_vencido_o_usado_no_guarda_nada()
    {
        var vencida = Fila(expiresAt: Now.AddMinutes(-1));
        (await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, true, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct))
            .Should().Be(ManualCaptureErrors.Expirada);
        vencida.ConsentAt.Should().BeNull();

        var usada = Fila(BiometricEstados.PendienteRevisionManual);
        (await Consent().HandleAsync(
            new RegistrarConsentimientoManualCommand(Token, true, ManualCaptureConsent.TextVersion, "203.0.113.7"), Ct))
            .Should().Be(ManualCaptureErrors.EstadoInvalido);
        usada.ConsentAt.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
