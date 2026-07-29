using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// Tests unitarios de <see cref="IniciarPrevalidacionHandler"/> — HU #10866 (Feature #10864) +
/// HU #11005 (CF-01/D1, ADR-0036, Feature #11004). Cobertura de AC1 (persona natural standalone sin
/// trámite) y del guard CF-01 que rechaza persona jurídica ANTES de tocar Person (422 en el endpoint).
/// </summary>
public sealed class IniciarPrevalidacionHandlerTests
{
    private readonly IPersonRepository _personRepo = Substitute.For<IPersonRepository>();
    private readonly IProcedureInstanceRepository _procedureRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private readonly Guid _tenantId = Guid.NewGuid();

    private IniciarPrevalidacionHandler BuildHandler(bool isKyverum = false)
    {
        var opts = new BiometricsProviderOptions
        {
            Provider = isKyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock,
        };
        return new IniciarPrevalidacionHandler(
            _personRepo, _procedureRepo, _kyverum, opts, _protector, _events);
    }

    private static IniciarPrevalidacionRequest NaturalRequest(
        string docType = "CC", string docNum = "1234567890",
        string name = "Juan Pérez", string email = "juan@example.com") =>
        new(docType, docNum, name, email, PersonTypes.Natural);

    private static IniciarPrevalidacionRequest JuridicalRequest(
        string name = "Empresa SAS",
        string docType = "NIT", string docNum = "900123456",
        string rlDocType = "CC", string rlDocNum = "55667788",
        string rlName = "Ana García", string? rlEmail = "ana@empresa.com") =>
        new(docType, docNum, name, "empresa@example.com", PersonTypes.Juridical,
            rlDocType, rlDocNum, rlName, rlEmail);

    private Person StubPerson(string personType = PersonTypes.Natural,
        string? legalRepDocType = null, string? legalRepDocNum = null,
        string? legalRepName = null, string? legalRepEmail = null)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DocumentType = "CC",
            DocumentNumber = "1234567890",
            FullName = "Juan Pérez",
            Email = "juan@example.com",
            PersonType = personType,
            LegalRepDocumentType = legalRepDocType,
            LegalRepDocumentNumber = legalRepDocNum,
            LegalRepName = legalRepName,
            LegalRepEmail = legalRepEmail,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _personRepo.FindOrCreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(person);
        _personRepo.FindActiveStandaloneValidationAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        return person;
    }

    private void StubKyverumOk(string verificationId = "kyv_abc", string captureUrl = "https://capture.example.com/kyv_abc")
    {
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(
                verificationId, captureUrl, "whsec_test",
                "pending", $"{{\"verification_id\":\"{verificationId}\"}}",
                DateTimeOffset.UtcNow.AddHours(24)));
    }

    // ── AC1: persona natural standalone — proveedor mock ─────────────────────────

    [Fact]
    public async Task AC1_Natural_Mock_CreatesValidationWithNullInstanceId_AndNullPartyRole()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        var handler = BuildHandler(isKyverum: false);

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Validation.Should().NotBeNull();
        result.Validation.PartyRole.Should().BeNull("prevalidación standalone no tiene parte");

        // Verificar que se persistió la validación con ProcedureInstanceId=null y PersonId seteado
        _procedureRepo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.ProcedureInstanceId == null
            && v.PartyRole == null
            && v.Provider == BiometricProviders.Mock
            && v.Status == BiometricEstados.Enviado));
    }

    [Fact]
    public async Task AC1_Natural_Mock_ReturnsMagicLinkAsCaptureUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        var handler = BuildHandler(isKyverum: false);

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().BeNull();
        result!.CaptureUrl.Should().StartWith("/api/v1/public/biometric/", "la URL de captura mock apunta al magic-link público");
        result.Queued.Should().BeFalse();
    }

    [Fact]
    public async Task AC1_Natural_Mock_EmitsIdentityValidationRequestedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        var handler = BuildHandler(isKyverum: false);

        await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        await _events.Received(1).PublishAsync(
            Arg.Is<IdentityValidationRequested>(e =>
                e.TenantId == _tenantId
                && e.ProcedureInstanceId == null
                && e.Parte == null
                && e.Provider == BiometricProviders.Mock),
            ct);
    }

    [Fact]
    public async Task AC1_Natural_Mock_UpsertPersonCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = StubPerson();
        var handler = BuildHandler(isKyverum: false);
        var req = NaturalRequest(docType: "CC", docNum: "9876543", name: "Pedro Rojas", email: "pedro@x.com");

        await handler.HandleAsync(_tenantId, req, ct);

        await _personRepo.Received(1).FindOrCreateAsync(
            _tenantId, "CC", "9876543", "Pedro Rojas", "pedro@x.com",
            PersonTypes.Natural, null, null, null, null, ct);
    }

    // ── AC1: persona natural standalone — proveedor Kyverum ──────────────────────

    [Fact]
    public async Task AC1_Natural_Kyverum_ReturnsCaptureUrlFromProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        StubKyverumOk(captureUrl: "https://kyverum.example.com/capture/001");
        var handler = BuildHandler(isKyverum: true);

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().BeNull();
        result!.CaptureUrl.Should().Be("https://kyverum.example.com/capture/001");
        result.Queued.Should().BeFalse();
        result.Validation.Status.Should().Be(BiometricEstados.EnProceso);
    }

    [Fact]
    public async Task AC1_Natural_Kyverum_CallsStartVerificationWithNullProcedureInstanceId()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        StubKyverumOk();
        var handler = BuildHandler(isKyverum: true);

        await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        await _kyverum.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.ProcedureInstanceId == null
                && r.Parte == null
                && r.TipoDoc == "CC"
                && r.Documento == "1234567890"),
            ct);
    }

    [Fact]
    public async Task AC1_Kyverum_TransitoryError_Returns202Queued()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("proveedor caído", transient: true));
        var handler = BuildHandler(isKyverum: true);

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().BeNull();
        result!.Queued.Should().BeTrue();
        result.Validation.Status.Should().Be(BiometricEstados.PendienteEnvio);

        _procedureRepo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.ProcedureInstanceId == null && v.Status == BiometricEstados.PendienteEnvio));
    }

    [Fact]
    public async Task AC1_Kyverum_DefinitiveError_ReturnsProveedor_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("datos inválidos", transient: false));
        var handler = BuildHandler(isKyverum: true);

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().Be("proveedor_error");
        result.Should().BeNull();
    }

    // ── CF-01/D1 (ADR-0036, Feature #11004): rechazo server-side de persona jurídica ────

    [Fact]
    public async Task CF01_Juridical_ReturnsPrevalidacionSoloNatural_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler(isKyverum: false);
        var req = JuridicalRequest();

        var (result, error) = await handler.HandleAsync(_tenantId, req, ct);

        error.Should().Be("prevalidacion_solo_natural");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CF01_Juridical_RejectedBeforeTouchingPerson_NoUpsertNoAdd()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler(isKyverum: false);

        await handler.HandleAsync(_tenantId, JuridicalRequest(), ct);

        // Guard evaluado ANTES del upsert de Person (ADR-0036): no debe crear/actualizar el registro
        // de una persona jurídica que de todas formas se va a rechazar.
        await _personRepo.DidNotReceive().FindOrCreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        _procedureRepo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task CF01_Juridical_Kyverum_AlsoRejected_NoProviderCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler(isKyverum: true);

        var (result, error) = await handler.HandleAsync(_tenantId, JuridicalRequest(), ct);

        error.Should().Be("prevalidacion_solo_natural");
        result.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Guards de validación ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "123", "Juan", "j@x.com")]
    [InlineData("CC", "", "Juan", "j@x.com")]
    [InlineData("CC", "123", "", "j@x.com")]
    [InlineData("CC", "123", "Juan", "")]
    public async Task DatosIncompletos_WhenRequiredFieldMissing(string docType, string docNum, string name, string email)
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, new IniciarPrevalidacionRequest(docType, docNum, name, email), ct);

        error.Should().Be("datos_incompletos");
        result.Should().BeNull();
    }

    [Fact]
    public async Task PrevalidacionSoloNatural_WhenJuridicalWithoutRL_StillRejectedByCF01First()
    {
        // CF-01 (ADR-0036): el guard de persona natural se evalúa ANTES que cualquier chequeo de datos
        // del representante legal — jurídica se rechaza siempre, tenga o no datos de RL completos.
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler();
        var req = new IniciarPrevalidacionRequest("NIT", "900123456", "Empresa SAS", "emp@x.com", PersonTypes.Juridical);

        var (result, error) = await handler.HandleAsync(_tenantId, req, ct);

        error.Should().Be("prevalidacion_solo_natural");
        result.Should().BeNull();
    }

    [Fact]
    public async Task PrevalidacionActiva_WhenActiveValidationExists_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = StubPerson();
        _personRepo.FindActiveStandaloneValidationAsync(person.Id, ct)
            .Returns(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                Status = BiometricEstados.EnProceso,
            });
        var handler = BuildHandler();

        var (result, error) = await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        error.Should().Be("prevalidacion_activa");
        result.Should().BeNull();
        _procedureRepo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    // ── Invariantes de la entidad ────────────────────────────────────────────────

    [Fact]
    public async Task Validation_AlwaysHasPersonIdSet_AndNullProcedureInstanceId()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = StubPerson();
        var handler = BuildHandler(isKyverum: false);

        await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        _procedureRepo.Received(1).Add(Arg.Is<ProcedureInstanceBiometricValidation>(v =>
            v.PersonId == person.Id
            && v.ProcedureInstanceId == null));
    }

    [Fact]
    public async Task SaveChangesCalledOnce_AfterSuccessfulCreation()
    {
        var ct = TestContext.Current.CancellationToken;
        StubPerson();
        var handler = BuildHandler(isKyverum: false);

        await handler.HandleAsync(_tenantId, NaturalRequest(), ct);

        await _procedureRepo.Received(1).SaveChangesAsync(ct);
    }
}
