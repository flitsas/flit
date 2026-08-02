using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11195 — al registrar el actor, si la parte es una empresa (NIT) sin representante utilizable en la
/// configuración de la compañía, se envía la validación de identidad al representante legal DECLARADO EN
/// EL TRÁMITE. Se ejercita el <see cref="PutActorsHandler"/> real contra el
/// <see cref="IniciarKyverumVerifyHandler"/> real: lo que se afirma es que el proveedor recibió (o no
/// recibió) la petición de verificación, que es lo que de verdad le manda el correo a la persona.
/// </summary>
public sealed class ValidacionIdentidadNitSinRepresentanteTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Kyverum };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly IRepresentanteLegalDirectory _directorio = Substitute.For<IRepresentanteLegalDirectory>();
    private readonly ISignatureVaultPolicy _baul = Substitute.For<ISignatureVaultPolicy>();
    private readonly PutActorsHandler _put;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid OwnerEntityId = Guid.NewGuid();

    public ValidacionIdentidadNitSinRepresentanteTests()
    {
        var kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());

        _put = new PutActorsHandler(
            _repo, _catalogRepo, _providerOptions, kyverumHandler, _consentRepo, _directorio, _baul);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = OwnerEntityId, Code = "OWNER", Name = "Propietario" });

        _kyverumClient.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_1", "https://capture/kyv_1", "whsec_x", "pending", "{}"));
    }

    [Fact]
    public async Task AC1_EmpresaSinRepresentanteRegistrado_EnviaLaValidacionAlRepresentanteDelTramite()
    {
        // El caso que dejaba al gestor sin salida: la compañía no tiene a nadie registrado que pueda
        // firmar, así que se le pide la identidad al representante que él mismo declaró en el trámite.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        SinRepresentanteUtilizable();

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        error.Should().BeNull();
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.Email == "rep@empresa.com" && r.Documento == "1090123456" && r.Parte == "comprador"),
            Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().ContainSingle()
            .Which.Email.Should().Be("rep@empresa.com");
    }

    [Fact]
    public async Task AC1_LaValidacionSeAncla_AlDocumentoDelRepresentante_NoAlNitDeLaEmpresa()
    {
        // El NIT no es validable biométricamente: la identidad tiene que quedar anclada a la persona,
        // o el gate de identidad la buscaría por un documento que nadie puede validar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        SinRepresentanteUtilizable();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        var validacion = instance.BiometricValidations.Should().ContainSingle().Subject;
        validacion.DocumentNumber.Should().Be("1090123456");
        validacion.DocumentType.Should().Be("CC");
        validacion.DocumentNumber.Should().NotBe("900123456");
    }

    [Fact]
    public async Task AC2_RepresentanteSinEscrituraNiFirmaVigentes_TambienEnvia()
    {
        // Para la ruta de registro da igual el motivo: si el directorio no puede aportar a nadie que
        // firme, hay que pedirle la identidad al representante del trámite.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_RepresentanteCompleto_NoEnviaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        ConRepresentanteUtilizable();

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_PersonaNatural_NiSiquieraConsultaElDirectorio()
    {
        // La compuerta no debe rozar el camino de persona natural: si llegara a consultarlo, un
        // directorio que responda "no" le dispararía un envío que hoy no existe.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorNatural()]), ct);

        await _directorio.DidNotReceive().TieneRepresentanteUtilizableAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProveedorMock_NoEnvia()
    {
        // El mock no emite CaptureUrl ni manda correos: mismo criterio que el reenvío de la HU #10880.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();
        _providerOptions.Provider = BiometricProviders.Mock;

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepresentanteSinDocumento_NoEnvia()
    {
        // Sin documento del representante no hay a quién validar: el NIT de la empresa no es validable
        // biométricamente. Enviarlo con el NIT crearía una validación que nadie puede completar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();

        await _put.HandleAsync(
            id,
            tenant,
            new PutActorsRequest([CompradorJuridico(rl: new ActorRepresentanteLegal(
                null, null, "Ana Representante", "rep@empresa.com", null))]),
            ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConValidacionYaEnCurso_NoDuplicaElEnvio()
    {
        // Reguardar los actores no puede volver a mandarle el correo a la persona: el handler de Kyverum
        // ya es idempotente por parte y la compuerta se apoya en esa guarda.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        SinRepresentanteUtilizable();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Ana Representante",
            DocumentType = "CC",
            DocumentNumber = "1090123456",
            Email = "rep@empresa.com",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }


    // ── El correo no debe salir cuando la persona ya puede firmar (hallado en validación manual) ──

    [Fact]
    public async Task ConFirmaDelBaulVigente_NoSeEnviaAunqueLaCompaniaNoAporteRepresentante()
    {
        // El caso reportado: el representante del trámite tiene su firma del baúl vigente, pero la
        // compuerta preguntaba por la COMPAÑÍA —que exige escritura vigente— y al responder "no" mandaba
        // un correo de validación a alguien que ya tenía con qué firmar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        SinRepresentanteUtilizable();
        ConFirmaDelBaul();

        var (_, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Baul)]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task SinEleccionExplicita_LaFirmaDelBaulVigenteTambienEvitaElCorreo()
    {
        // Sin elección manda la precedencia del baúl (HU #11031), así que la firma se plasmará desde el
        // baúl igual: pedir identidad seguiría siendo pedir algo que nadie va a usar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();
        ConFirmaDelBaul();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EligiendoIdentidad_SeEnviaAunqueTengaFirmaDelBaulVigente()
    {
        // Simétrico y deliberado: si el gestor eligió el sello de identidad, la firma del baúl no se va a
        // consumir, así que la validación SÍ hace falta. Es lo que separa "tiene firma" de "va a usarla".
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();
        ConFirmaDelBaul();

        await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Identidad)]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EligiendoElBaulSinFirmaVigente_SeEnviaIgual()
    {
        // Haber elegido el baúl no basta: sin firma real que plasmar, la validación de identidad es la
        // única salida. Lo contrario dejaría el trámite sin ninguna forma de firmarse.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        SinRepresentanteUtilizable();

        await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Baul)]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (Guid Id, Guid Tenant, ProcedureInstance Instance) NuevoTramite()
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return (id, tenant, instance);
    }

    private void SinRepresentanteUtilizable() =>
        _directorio.TieneRepresentanteUtilizableAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(false);

    /// <summary>El representante del trámite tiene firma del baúl activa y vigente.</summary>
    private void ConFirmaDelBaul() =>
        _baul.ResolveAsync(Arg.Any<Guid>(), "CC", "1090123456", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Ana Representante", "sha", "path", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "1090123456"));

    private void ConRepresentanteUtilizable() =>
        _directorio.TieneRepresentanteUtilizableAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(true);

    private static ActorInput CompradorJuridico(
        ActorRepresentanteLegal? rl = null, string? mecanismo = null) =>
        new(
            "comprador",
            "NIT",
            "900123456",
            "Empresa Compradora SAS",
            "contacto@empresa.com",
            null,
            PersonType: "juridical",
            RepresentanteLegal: rl ?? new ActorRepresentanteLegal(
                "CC", "1090123456", "Ana Representante", "rep@empresa.com", null, mecanismo));

    private static ActorInput CompradorNatural() =>
        new("comprador", "CC", "123456", "Juan Comprador", "juan@x.com", null, PersonType: "natural");
}
