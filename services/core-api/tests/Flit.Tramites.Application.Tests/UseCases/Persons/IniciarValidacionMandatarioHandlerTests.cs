using System.Text;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — <see cref="IniciarPrevalidacionHandler.HandleMandatarioAsync"/>: la validación
/// de identidad PROPIA del mandatario usa el mismo flujo de la prevalidación (Kyverum o mock, outbox, webhook) pero queda con
/// party_role <c>mandatario</c> + referencia a su ficha, en el tenant indicado, sin persona ni trámite y sin evaluar la
/// precedencia de envío por documento. <para>Uso: <c>handler.HandleMandatarioAsync(tenantCompania, fichaId, "CC", "123",
/// "Ana", "ana@x.com")</c> ⇒ una fila con <c>MandateSignerId == fichaId</c>.</para>
/// </summary>
public sealed class IniciarValidacionMandatarioHandlerTests
{
    private readonly IPersonRepository _personRepo = Substitute.For<IPersonRepository>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private readonly Guid _companyTenant = Guid.NewGuid();
    private readonly Guid _signerId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IniciarPrevalidacionHandler Build(bool kyverum) =>
        new(
            _personRepo, _repo, _kyverum,
            new BiometricsProviderOptions { Provider = kyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock },
            _protector, _events);

    private Task<(IniciarPrevalidacionResult? Result, string? Error)> Lanzar(
        IniciarPrevalidacionHandler handler, string email = "ana@flit.test") =>
        handler.HandleMandatarioAsync(_companyTenant, _signerId, "CC", "1020304050", "Ana Restrepo", email, Ct);

    private void KyverumOk() =>
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(
                "kyv_man", "https://capture.example.com/kyv_man", "whsec_man", "pending", "{}",
                DateTimeOffset.UtcNow.AddHours(24)));

    [Fact]
    public async Task Mock_CreaLaValidacionPropia_ConRolMandatario_FichaYTenantDeLaCompania_SinPersonaNiTramite()
    {
        var (result, error) = await Lanzar(Build(kyverum: false));

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.CaptureUrl.Should().StartWith("/api/v1/public/biometric/");
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.TenantId == _companyTenant
            && v.PartyRole == BiometricRules.ParteMandatario
            && v.MandateSignerId == _signerId
            && v.PersonId == null
            && v.ProcedureInstanceId == null
            && v.Provider == BiometricProviders.Mock
            && v.Status == BiometricEstados.Enviado
            && v.DocumentNumber == "1020304050"
            && v.Email == "ana@flit.test"));
        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationRequested>(e =>
                e.TenantId == _companyTenant && e.Parte == BiometricRules.ParteMandatario),
            Ct);
        await _repo.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task NoCreaPersonaNiEvaluaLaPrecedenciaDeEnvioPorDocumento()
    {
        // Una identidad vigente del mismo documento (comprador, vendedor, prevalidación) NO cuenta ni bloquea: no se consulta.
        await Lanzar(Build(kyverum: false));

        await _personRepo.DidNotReceiveWithAnyArgs().FindOrCreateAsync(
            default, default!, default!, default!, default!, default!, default, default, default, default, Ct);
        await _repo.DidNotReceiveWithAnyArgs().FindVigenteApprovedByDocumentAsync(default, default!, default!, default, Ct);
        await _repo.DidNotReceiveWithAnyArgs().ListInFlightByDocumentAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task CierraLaValidacionEnVueloDelMismoMandatario_AntesDeCrearLaNueva()
    {
        var orden = new List<string>();
        _repo.SupersedeMandatarioInFlightAsync(_signerId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => { orden.Add("cerrar"); return Task.FromResult(1); });
        _repo.When(r => r.Add(Arg.Any<ProcedureInstanceBiometricValidation>())).Do(_ => orden.Add("crear"));

        await Lanzar(Build(kyverum: false));

        orden.Should().Equal("cerrar", "crear");
    }

    [Fact]
    public async Task Kyverum_Ok_QuedaEnProceso_ConElRolMandatarioEnLaSolicitudYElSecretoProtegido()
    {
        KyverumOk();

        var (result, error) = await Lanzar(Build(kyverum: true));

        error.Should().BeNull();
        result!.Queued.Should().BeFalse();
        result.CaptureUrl.Should().Be("https://capture.example.com/kyv_man");
        await _kyverum.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.ProcedureInstanceId == null
                && r.Parte == BiometricRules.ParteMandatario
                && r.Email == "ana@flit.test"
                && r.Documento == "1020304050"),
            Ct);
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.Status == BiometricEstados.EnProceso
            && v.Provider == BiometricProviders.Kyverum
            && v.KyverumVerificationId == "kyv_man"
            && v.MandateSignerId == _signerId
            && v.WebhookSecretEncrypted != null
            && v.WebhookSecretEncrypted != "whsec_man"));
    }

    [Fact]
    public async Task Kyverum_FallaTransitoria_QuedaPendienteDeEnvio_YElResultadoIndicaCola()
    {
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("caído", transient: true));

        var (result, error) = await Lanzar(Build(kyverum: true));

        error.Should().BeNull();
        result!.Queued.Should().BeTrue();
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.Status == BiometricEstados.PendienteEnvio && v.MandateSignerId == _signerId));
        await _events.Received(1).PublishAsync(Arg.Any<IdentityValidationRequested>(), Ct);
    }

    [Fact]
    public async Task Kyverum_FallaDefinitiva_ProveedorError_SinFilaNueva()
    {
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("rechazado", transient: false));

        var (result, error) = await Lanzar(Build(kyverum: true));

        result.Should().BeNull();
        error.Should().Be("proveedor_error");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task DosLanzamientosSimultaneos_ElQuePierdeLaCarreraRecibePrevalidacionActiva_NoDuplica()
    {
        // Índice único parcial por mandatario (DDL 129): SaveChanges lo traduce a IdentityInFlightConflictException.
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IdentityInFlightConflictException());

        var (result, error) = await Lanzar(Build(kyverum: false));

        result.Should().BeNull();
        error.Should().Be("prevalidacion_activa");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SinCorreo_DatosIncompletos_NoLanzaNada(string email)
    {
        var (result, error) = await Lanzar(Build(kyverum: false), email);

        result.Should().BeNull();
        error.Should().Be("datos_incompletos");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
        await _repo.DidNotReceiveWithAnyArgs().SupersedeMandatarioInFlightAsync(default, default, Ct);
    }

    [Fact]
    public async Task LaPrevalidacionStandalone_NoCambia_PartyRoleNuloYSinFicha()
    {
        // El contrato de HandleAsync no se modifica (HU #13246): su fila sigue sin rol ni ficha.
        var person = new Person
        {
            Id = Guid.NewGuid(), TenantId = _companyTenant, DocumentType = "CC", DocumentNumber = "1020304050",
            FullName = "Ana", Email = "ana@flit.test", PersonType = PersonTypes.Natural, CreatedAt = DateTimeOffset.UtcNow,
        };
        _personRepo.FindOrCreateAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(person);

        var (result, error, _) = await Build(kyverum: false).HandleAsync(
            _companyTenant,
            new IniciarPrevalidacionRequest("CC", "1020304050", "Ana", "ana@flit.test"),
            Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.PartyRole == null && v.MandateSignerId == null && v.PersonId == person.Id));
    }

    // ── AC4 — el webhook actualiza SOLO la validación del mandatario ───────────────────────────────

    [Fact]
    public async Task Webhook_Aprobado_ActualizaLaValidacionDelMandatario_YEmiteElEventoConSuRol()
    {
        const string secret = "whsec_abc";
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _companyTenant,
            ProcedureInstanceId = null,
            PersonId = null,
            PartyRole = BiometricRules.ParteMandatario,
            MandateSignerId = _signerId,
            Name = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            Email = "ana@flit.test",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = "kyv_man",
            WebhookSecretEncrypted = _protector.Protect(secret),
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        var handler = new KyverumWebhookHandler(
            _repo, _protector, _kyverum, new IdentityValidationResultApplier(_events),
            Substitute.For<IIdentityValidationAuditLog>(), NullLogger<KyverumWebhookHandler>.Instance);
        var body = Encoding.UTF8.GetBytes(
            "{\"evento\":\"validation.completed\",\"requestId\":\"r1\",\"data\":{\"aprobado\":true,"
            + "\"closedAt\":\"2026-10-01T15:30:00.000Z\",\"subjects\":[{\"id\":\"s1\",\"rol\":\"mandatario\","
            + "\"documento\":\"1020304050\",\"status\":\"aprobado\",\"score\":90,\"firmaSerie\":\"FS-MAN\"}]},"
            + "\"deliveryId\":\"d1\",\"ts\":\"2026-10-01T15:30:01.000Z\"}");

        var (result, error) = await handler.HandleAsync(
            new KyverumWebhookInput(v.Id, body, "sha256=" + KyverumWebhookVerifier.ComputeHmac(body, secret)), Ct);

        error.Should().BeNull();
        result.Should().Be("ok");
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.CertificateHash.Should().Be("FS-MAN");
        v.MandateSignerId.Should().Be(_signerId);
        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationCompleted>(e =>
                e.ValidationId == v.Id && e.Parte == BiometricRules.ParteMandatario
                && e.Estado == BiometricEstados.Aprobado),
            Ct);
        await _repo.Received(1).SaveChangesAsync(Ct);
    }
}
