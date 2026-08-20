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
/// HU #11662 — <b>la compuerta del NIT delega la decisión de envío en la precedencia única.</b>
///
/// <para><b>Qué cambió.</b> Al registrar los actores, una parte jurídica encamina la validación de
/// identidad de su representante legal. Ese camino llevaba dos prechequeos propios: la cobertura del
/// baúl y —el que causó el defecto— una pregunta al directorio de Admin sobre si <i>la compañía</i>
/// tenía algún representante utilizable. Bastaba que <b>otro</b> representante acreditado del NIT
/// tuviera firma para que al representante elegido en el trámite no le llegara nada: sin identidad y
/// sin vía para conseguirla.</para>
///
/// <para>Los dos prechequeos eran además redundantes. <see cref="IniciarKyverumVerifyHandler"/> evalúa
/// la precedencia única de envío (ADR-0039: baúl → identidad vigente → validación en vuelo → enviar),
/// de la cual la cobertura del baúl es literalmente el primer paso. Un prechequeo solo puede suprimir
/// envíos legítimos; nunca añade uno.</para>
///
/// <para><b>Qué se ejercita.</b> El <see cref="PutActorsHandler"/> real contra el
/// <see cref="IniciarKyverumVerifyHandler"/> real —cableado con el baúl, como lo hace la inyección de
/// dependencias de producción—. Lo que se afirma es si el proveedor recibió la petición de
/// verificación, que es lo que de verdad le manda el correo a la persona.</para>
///
/// <para>Uso de ejemplo:
/// <c>await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct)</c>
/// ⇒ el representante declarado recibe la validación si no tiene ya con qué firmar.</para>
/// </summary>
public sealed class ValidacionIdentidadParteJuridicaTests
{
    private const string RlTipoDocumento = "CC";
    private const string RlDocumento = "1090123456";
    private const string RlEmail = "rep@empresa.com";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Kyverum };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly ISignatureVaultPolicy _baul = Substitute.For<ISignatureVaultPolicy>();
    private readonly PutActorsHandler _put;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid OwnerEntityId = Guid.NewGuid();

    public ValidacionIdentidadParteJuridicaTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        // El baúl viaja al handler de Kyverum, que es quien evalúa la precedencia. Es el mismo cableado
        // que hace la inyección de dependencias (ISignatureVaultPolicy está registrado y el parámetro
        // opcional del handler se resuelve del contenedor).
        var kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>(),
            _baul);

        _put = new PutActorsHandler(
            _repo, _catalogRepo, _providerOptions, kyverumHandler, _consentRepo, _baul);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = OwnerEntityId, Code = "OWNER", Name = "Propietario" });

        _kyverumClient.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_1", "https://capture/kyv_1", "whsec_x", "pending", "{}"));
    }

    // ── Regla A — el NIT sin nada registrado y el representante digitado a mano ────────────────

    [Fact]
    public async Task A_EmpresaSinRegistroNiRepresentante_EnviaAlRepresentanteDigitadoEnElTramite()
    {
        // El caso que dejaba al gestor sin salida: la compañía no tiene a nadie que pueda firmar, así que
        // se le pide la identidad al representante que él mismo declaró, con su documento y su correo.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        error.Should().BeNull();
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.Email == RlEmail && r.Documento == RlDocumento && r.Parte == "comprador"),
            Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().ContainSingle()
            .Which.Email.Should().Be(RlEmail);
    }

    [Fact]
    public async Task A_LaValidacionSeAncla_AlDocumentoDelRepresentante_NoAlNitDeLaEmpresa()
    {
        // El NIT no es validable biométricamente: la identidad tiene que quedar anclada a la persona,
        // o el gate de identidad la buscaría por un documento que nadie puede validar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        var validacion = instance.BiometricValidations.Should().ContainSingle().Subject;
        validacion.DocumentNumber.Should().Be(RlDocumento);
        validacion.DocumentType.Should().Be(RlTipoDocumento);
        validacion.DocumentNumber.Should().NotBe("900123456");
    }

    // ── Regla B.I — baúl vigente Y validación de identidad vigente ────────────────────────────

    [Fact]
    public async Task BI_BaulVigenteEIdentidadVigente_NoEnvia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        ConFirmaDelBaul();
        ConIdentidadVigente(tenant);

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task BI_EligiendoElBaul_ConAmbasVigentes_NoEnvia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        ConFirmaDelBaul();
        ConIdentidadVigente(tenant);

        await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Baul)]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Regla B.II — solo una vigente ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BII_SoloBaulVigente_SinEleccion_NoEnvia()
    {
        // Sin elección manda la precedencia del baúl (HU #11031): la firma se plasmará desde ahí, así que
        // pedir identidad sería pedir algo que nadie va a usar.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        ConFirmaDelBaul();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BII_SoloIdentidadVigente_NoEnvia()
    {
        // La identidad vigente de la persona vale en todo el tenant (HU #10350): no se revalida.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        ConIdentidadVigente(tenant);

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        instance.BiometricValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task BII_SoloBaulVigente_PeroEligiendoIdentidad_SiEnvia()
    {
        // El caso especial que separa "tiene firma" de "va a usarla": elegido el sello de identidad, la
        // firma del baúl no se va a consumir y la validación sí hace falta.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
        ConFirmaDelBaul();

        await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Identidad)]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Regla B.III — ninguna vigente ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BIII_NingunaVigente_EnviaAlRepresentanteDelTramite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorJuridico()]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r => r.Email == RlEmail), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BIII_EligiendoElBaulSinFirmaVigente_EnviaIgual()
    {
        // Haber elegido el baúl no basta: sin firma real que plasmar, la validación de identidad es la
        // única salida. Lo contrario dejaría el trámite sin ninguna forma de firmarse.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();

        await _put.HandleAsync(
            id, tenant, new PutActorsRequest([CompradorJuridico(mecanismo: MecanismoFirma.Baul)]), ct);

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Simetría comprador / vendedor ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Simetria_ElVendedorJuridicoRecibeLaValidacionIgualQueElComprador()
    {
        // El disparador itera los actores del PUT: no hay nada por rol. Un traspaso entre dos empresas
        // debe encaminar la identidad de los dos representantes.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite("traspaso");

        var (_, error) = await _put.HandleAsync(
            id,
            tenant,
            new PutActorsRequest([VendedorJuridico(), CompradorJuridico()]),
            ct);

        error.Should().BeNull();
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r => r.Parte == "vendedor"), Arg.Any<CancellationToken>());
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r => r.Parte == "comprador"), Arg.Any<CancellationToken>());
    }

    // ── Idempotencia y filtros de datos ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConValidacionEnVueloYEnlaceVivo_NoDuplicaElEnvio()
    {
        // Reguardar los actores no puede volver a mandarle el correo a la persona.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, instance) = NuevoTramite();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Ana Representante",
            DocumentType = RlTipoDocumento,
            DocumentNumber = RlDocumento,
            Email = RlEmail,
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
        instance.BiometricValidations.Should().ContainSingle();
    }

    [Fact]
    public async Task PersonaNatural_NoDisparaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();

        await _put.HandleAsync(id, tenant, new PutActorsRequest([CompradorNatural()]), ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProveedorMock_NoEnvia()
    {
        // El mock no emite CaptureUrl ni manda correos: mismo criterio que el reenvío de la HU #10880.
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();
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

        await _put.HandleAsync(
            id,
            tenant,
            new PutActorsRequest([CompradorJuridico(rl: new ActorRepresentanteLegal(
                null, null, "Ana Representante", RlEmail, null))]),
            ct);

        await _kyverumClient.DidNotReceive().StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (Guid Id, Guid Tenant, ProcedureInstance Instance) NuevoTramite(string modalidad = "matricula_inicial")
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
            ModalidadEntrada = modalidad,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return (id, tenant, instance);
    }

    /// <summary>El representante del trámite tiene firma del baúl activa y vigente.</summary>
    private void ConFirmaDelBaul() =>
        _baul.ResolveAsync(Arg.Any<Guid>(), RlTipoDocumento, RlDocumento, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Ana Representante", "sha", "path", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), RlDocumento));

    /// <summary>El representante ya validó su identidad y sigue vigente en el tenant (HU #10350).</summary>
    private void ConIdentidadVigente(Guid tenant)
    {
        var now = DateTimeOffset.UtcNow;
        _repo.FindVigenteApprovedByDocumentAsync(
                tenant, RlTipoDocumento, RlDocumento, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureInstanceId = Guid.NewGuid(),
                DocumentType = RlTipoDocumento,
                DocumentNumber = RlDocumento,
                Status = BiometricEstados.Aprobado,
                ValidatedAt = now.AddDays(-1),
                ValidUntil = now.AddDays(29),
                TokenHash = "h",
                ExpiresAt = now.AddHours(1),
                CreatedAt = now.AddDays(-1),
            });
    }

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
                RlTipoDocumento, RlDocumento, "Ana Representante", RlEmail, null, mecanismo));

    private static ActorInput VendedorJuridico() =>
        new(
            "vendedor",
            "NIT",
            "900987654",
            "Empresa Vendedora SAS",
            "contacto@vendedora.com",
            null,
            PersonType: "juridical",
            RepresentanteLegal: new ActorRepresentanteLegal(
                "CC", "1090999888", "Bruno Representante", "rep@vendedora.com", null));

    private static ActorInput CompradorNatural() =>
        new("comprador", "CC", "123456", "Juan Comprador", "juan@x.com", null, PersonType: "natural");
}
