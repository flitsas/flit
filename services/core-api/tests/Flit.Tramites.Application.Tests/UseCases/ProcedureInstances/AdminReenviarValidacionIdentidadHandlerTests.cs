using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Tests unitarios de <see cref="AdminReenviarValidacionIdentidadHandler"/> — HU #12161 (Feature #12155).
/// AC1: reenvía al correo actual sin cambiarlo, queda en el historial. AC2: actualiza el correo y reenvía
/// al nuevo. AC3: 'aprobado'/'anulado'/'revocado' (string, HU #12165 aún no existe como enum) rechazan la
/// acción. AC4 (mensaje visible) es responsabilidad del frontend (HU #12164); aquí se verifica que la
/// respuesta del handler en éxito es inequívoca (resultado no nulo, con los datos que el frontend necesita
/// para confirmar).
/// </summary>
public sealed class AdminReenviarValidacionIdentidadHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();

    private readonly Guid _tenantId = Guid.NewGuid();

    private AdminReenviarValidacionIdentidadHandler BuildHandler(bool isKyverum = false)
    {
        var opts = new BiometricsProviderOptions
        {
            Provider = isKyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock,
        };
        return new AdminReenviarValidacionIdentidadHandler(_repo, _kyverum, opts, _protector, _events, _audit);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000998",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Validación LIGADA a trámite (nunca puebla PersonId/Person — solo lo hace la prevalidación
    /// standalone, HU #10865): el sujeto de esta HU se arma desde las columnas propias de la fila.
    /// </summary>
    private ProcedureInstanceBiometricValidation SeedTramiteValidation(
        Guid instanceId,
        string status = BiometricEstados.Enviado,
        string provider = BiometricProviders.Mock,
        string email = "comprador@old.com",
        int resendCount = 0,
        DateTimeOffset? lastResentAt = null)
    {
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instanceId,
            PersonId = null,
            Person = null,
            PartyRole = BiometricRules.ParteComprador,
            Name = "Ana Ríos",
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            Email = email,
            // Bug #12376, defecto 3 — correo del REGISTRO, tal como quedó al iniciar la validación
            // (antes de cualquier reenvío administrativo que esta prueba pueda disparar).
            RegisteredEmail = email,
            Status = status,
            Provider = provider,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // vencido, escenario típico de reenvío
            Attempts = 2,
            ReconcilePollCount = 2,
            ResendCount = resendCount,
            LastResentAt = lastResentAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        return validation;
    }

    private void StubKyverumOk(string verificationId = "kyv_new", string captureUrl = "https://capture.example.com/new") =>
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(
                verificationId, captureUrl, "whsec_new", "pending",
                $"{{\"id\":\"{verificationId}\"}}", DateTimeOffset.UtcNow.AddHours(24)));

    // ── AC1 — reenvía al correo actual sin cambiarlo, queda registrado en el historial ─────────────

    [Fact]
    public async Task AC1_TramiteEntregado_SinCambiarCorreo_ReenviaAlActual()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, userId);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.EmailActualizado.Should().BeFalse();
        validation.Email.Should().Be("comprador@old.com");
    }

    [Fact]
    public async Task AC1_ReenvioExitoso_QuedaRegistradoEnElHistorialDelTramite()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();
        var antes = DateTimeOffset.UtcNow;

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, userId);
        var (_, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == AdminReenviarValidacionIdentidadHandler.EventoTipo
                && e.TenantId == _tenantId
                && e.ProcedureInstanceId == instanceId
                && e.CreatedBy == userId
                && e.CreatedAt >= antes
                // Habeas Data — el correo NUNCA viaja en claro en el payload del historial.
                && e.Payload != null && !e.Payload.Contains("comprador@old.com")),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── AC2 — actualiza el correo y reenvía al nuevo destinatario ───────────────────────────────────

    [Fact]
    public async Task AC2_TramiteEntregado_ActualizaCorreo_QuedaPersistidoYReenviaAlNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(
            instanceId, validation.Id, _tenantId, "comprador.nuevo@correcto.com", null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result!.EmailActualizado.Should().BeTrue();
        validation.Email.Should().Be("comprador.nuevo@correcto.com");
    }

    // Bug #12376, defecto 3 — el reenvío actualiza el correo OPERATIVO (Email) pero NUNCA el correo del
    // REGISTRO (RegisteredEmail): el tracking del trámite debe poder seguir mostrando el correo original
    // aunque el reenvío haya ido a un destino distinto.
    [Fact]
    public async Task Bug12376_ActualizaCorreo_NoTocaElCorreoDelRegistro()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId, email: "comprador@old.com");
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(
            instanceId, validation.Id, _tenantId, "comprador.nuevo@correcto.com", null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result!.EmailActualizado.Should().BeTrue();
        validation.Email.Should().Be("comprador.nuevo@correcto.com");
        validation.RegisteredEmail.Should().Be("comprador@old.com", "el correo del registro es inmutable");
    }

    [Fact]
    public async Task AC2_MismoCorreoQueElActual_NoCuentaComoActualizado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId, email: "comprador@old.com");
        var handler = BuildHandler();

        // Mismo correo, con distinto casing/espacios — no debe contar como cambio (AC1, no AC2).
        var command = new AdminReenviarValidacionIdentidadCommand(
            instanceId, validation.Id, _tenantId, "  COMPRADOR@OLD.com  ", null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result!.EmailActualizado.Should().BeFalse();
        validation.Email.Should().Be("comprador@old.com");
    }

    // ── AC3 — 'aprobado' / 'anulado' / 'revocado' rechazan la acción ────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData("revocado")]
    [InlineData("REVOCADO")]
    public async Task AC3_TramiteEnEstadoNoAccionable_RechazaElReenvio(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, estado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().Be(TramiteEstadoErrores.IdentidadReenvioNoDisponible);
        result.Should().BeNull();
        validation.Email.Should().Be("comprador@old.com", "el rechazo no debe aplicar ningún cambio");
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_TramiteEntregado_NoQuedaBloqueado_EsElCasoCentralDeLaHu()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    // ── AC4 — el backend responde con éxito inequívoco (el mensaje visible es del frontend, HU #12164) ─

    [Fact]
    public async Task AC4_ReenvioExitoso_DevuelveResultadoInequivoco_MensajeEsResponsabilidadDelFrontend()
    {
        // AC4 (confirmación visible al usuario) es del frontend (HU #12164). La contribución de este
        // handler es que el éxito se distinga sin ambigüedad: Error == null y un Result con los datos
        // que el frontend necesita para armar su mensaje (a qué correo fue, si quedó encolado).
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Validation.Should().NotBeNull();
        result.Queued.Should().BeFalse();
    }

    // ── Guards adicionales reutilizados del núcleo compartido (D9/D10) ──────────────────────────────

    [Fact]
    public async Task TramiteNoEncontrado_DevuelveNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, Guid.NewGuid(), _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidacionNoPerteneceAlTramiteDeLaRuta_DevuelveNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var otraInstanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(otraInstanceId); // ligada a OTRO trámite
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
    }

    [Fact]
    public async Task IdentidadYaAprobada_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId, status: BiometricEstados.Aprobado);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().Be(AdminReenviarValidacionIdentidadHandler.IdentidadAprobada);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Cooldown_BloqueaElReenvio_MismoNucleoCompartidoQueElStandalone()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId, lastResentAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, cooldownMinutos) = await handler.HandleAsync(command, ct);

        error.Should().Be("reenvio_en_cooldown");
        result.Should().BeNull();
        cooldownMinutos.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(BiometricRules.ReenvioCooldownMinutos);
    }

    [Fact]
    public async Task TopeDeReenvios_Bloquea_MismoNucleoCompartidoQueElStandalone()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(
            instanceId, resendCount: BiometricRules.MaxReenvios, provider: BiometricProviders.Kyverum);
        var handler = BuildHandler(isKyverum: true);

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().Be("tope_reenvios");
        result.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kyverum_EnvioExitoso_UsaElNucleoCompartido_SinDuplicarLaIntegracion()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(
            instanceId, status: BiometricEstados.Expirado, provider: BiometricProviders.Kyverum);
        StubKyverumOk();
        var handler = BuildHandler(isKyverum: true);

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        var (result, error, _, _) = await handler.HandleAsync(command, ct);

        error.Should().BeNull();
        validation.Status.Should().Be(BiometricEstados.EnProceso);
        await _kyverum.Received(1).StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Habeas Data — la bitácora técnica también enmascara el correo ───────────────────────────────

    [Fact]
    public async Task Auditoria_NuncaExponeElCorreoEnClaro()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        _repo.GetByIdAsync(instanceId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(instanceId, _tenantId, TramiteEstado.Entregado));
        var validation = SeedTramiteValidation(instanceId);
        var handler = BuildHandler();

        var command = new AdminReenviarValidacionIdentidadCommand(instanceId, validation.Id, _tenantId, null, null);
        await handler.HandleAsync(command, ct);

        await _audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e =>
                e.Stage == IdentityValidationAuditStages.Resend
                && e.ValidationId == validation.Id
                && e.ProcedureInstanceId == instanceId
                && e.Message != null && !e.Message.Contains("comprador@old.com")
                && (e.Detail == null || !e.Detail.Contains("comprador@old.com"))),
            ct);
    }
}
