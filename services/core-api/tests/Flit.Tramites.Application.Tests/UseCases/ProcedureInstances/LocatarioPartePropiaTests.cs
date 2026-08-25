using Flit.Tramites.Application.Identity;
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
/// El arrendatario como parte propia (<c>LESSEE</c>).
///
/// <para>El sistema ya lo trataba como parte real en todas partes menos donde importaba: el FUR le
/// arma su <c>DocumentParte</c>, el resolver de destinatarios le manda los correos y el ciclo de vida
/// mapea <c>locatario</c> → <c>LESSEE</c>, pero <c>ParteRol</c> no lo tenía y <c>PutActorsHandler</c>
/// respondía <c>invalid_rol</c>. Resultado: la parte del FUR era inalcanzable y la matrícula por
/// leasing emitía el documento sin su observación obligatoria del párrafo 23.</para>
///
/// <para>No valida identidad ni firma: eso es del propietario.</para>
/// </summary>
public sealed class LocatarioPartePropiaTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly PutActorsHandler _put;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();
    private static readonly Guid LesseeEntityId = Guid.NewGuid();

    public LocatarioPartePropiaTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());

        _put = new PutActorsHandler(
            _repo,
            _catalogRepo,
            new BiometricsProviderOptions { Provider = BiometricProviders.Mock },
            kyverumHandler,
            _consentRepo);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("LESSEE", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = LesseeEntityId, Code = "LESSEE", Name = "Arrendatario" });
    }

    private ProcedureInstance Instance(ProcedureType type, CancellationToken ct)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = type,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = type.Id,
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        return instance;
    }

    private static ActorInput Propietario(string doc = "900123456") =>
        new("comprador", "NIT", doc, "Leasing del Valle S.A.", "leasing@x.com", "3001112233")
        {
            Ciudad = "Medellín",
            Direccion = "Calle 1 # 2-3",
        };

    private static ActorInput Locatario(string doc = "79123456") =>
        new("locatario", "CC", doc, "Marta Peñaloza", "marta@x.com", "3009998877")
        {
            Ciudad = "Medellín",
            Direccion = "Carrera 4 # 5-6",
        };

    [Fact]
    public async Task Leasing_PersisteAlArrendatarioConSuPropioRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.MatriculaLeasing, ct);

        var (result, error) = await _put.HandleAsync(
            instance.Id, instance.TenantId, new PutActorsRequest([Propietario(), Locatario()]), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();

        // El literal que ya leen FurCommand, el resolver de destinatarios y el ciclo de vida.
        var locatario = instance.Actors.Should().ContainSingle(a => a.ActorType == "locatario").Subject;
        locatario.ProcedureEntityId.Should().Be(LesseeEntityId);
        locatario.FullName.Should().Be("Marta Peñaloza");

        // Y sigue siendo una parte DISTINTA del propietario, que es lo que el FUR necesita para
        // imprimir «de {propietario} a LOCATARIO …» en vez de «de X a X».
        instance.Actors.Should().ContainSingle(a => a.ActorType == "comprador");
    }

    [Fact]
    public async Task Leasing_RechazaQueElArrendatarioSeaElPropietario()
    {
        // Si fueran la misma persona no habría leasing que registrar, y el FUR imprimiría «de X a X».
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.MatriculaLeasing, ct);

        var (result, error) = await _put.HandleAsync(
            instance.Id,
            instance.TenantId,
            new PutActorsRequest([Propietario("900123456"), new ActorInput(
                "locatario", "NIT", "900123456", "Leasing del Valle S.A.", "otro@x.com", "3001112233")
            {
                Ciudad = "Medellín",
                Direccion = "Calle 1 # 2-3",
            }]),
            ct);

        error.Should().Be(PutActorsHandler.LocatarioIgualAlPropietarioError);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Leasing_LaGuardaDelDuplicadoTambienActuaEntrePasosDistintos()
    {
        // Las dos partes se capturan en pasos separados, así que la comprobación tiene que mirar el
        // conjunto EFECTIVO: lo que trae el PUT más lo que ya estaba guardado.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.MatriculaLeasing, ct);
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = BuyerEntityId,
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Leasing del Valle S.A.",
            Email = "leasing@x.com",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var (_, error) = await _put.HandleAsync(
            instance.Id,
            instance.TenantId,
            new PutActorsRequest([new ActorInput(
                "locatario", "NIT", "900123456", "Leasing del Valle S.A.", "otro@x.com", "3001112233")
            {
                Ciudad = "Medellín",
                Direccion = "Calle 1 # 2-3",
            }]),
            ct);

        error.Should().Be(PutActorsHandler.LocatarioIgualAlPropietarioError);
    }

    [Fact]
    public async Task UnTipoQueNoDeclaraArrendatario_LoRechaza()
    {
        // REGRESIÓN: abrir el rol no lo habilita en todas partes. Una matrícula normal no tiene
        // arrendatario, y colar uno le metería una parte al FUR que ese trámite no contempla.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Matricula, ct);

        var (_, error) = await _put.HandleAsync(
            instance.Id, instance.TenantId, new PutActorsRequest([Propietario(), Locatario()]), ct);

        error.Should().Be("rol_not_allowed");
    }

    [Fact]
    public async Task Traspaso_SigueRechazandoUnArrendatario()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.Traspaso, ct);

        var (_, error) = await _put.HandleAsync(
            instance.Id, instance.TenantId, new PutActorsRequest([Locatario()]), ct);

        error.Should().Be("rol_not_allowed");
    }

    [Fact]
    public async Task Leasing_ElArrendatarioJuridicoNoConvocaLaBiometriaDeSuRepresentante()
    {
        // El disparador del representante legal recorría TODOS los actores. Con el locatario abierto,
        // una arrendataria persona jurídica habría arrastrado a su representante a una validación de
        // identidad que el leasing no le pide: ahí quien firma es el propietario.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(ProcedureTypeFixture.MatriculaLeasing, ct);

        var locatarioJuridico = new ActorInput(
            "locatario", "NIT", "800999888", "Arrendataria S.A.S.", "arrendataria@x.com", "3005556677")
        {
            Ciudad = "Medellín",
            Direccion = "Carrera 4 # 5-6",
            PersonType = "juridical",
            RepresentanteLegal = new ActorRepresentanteLegal("CC", "1020304050", "Ana Gómez", "ana@x.com", "3001234567"),
        };

        var (_, error) = await _put.HandleAsync(
            instance.Id, instance.TenantId, new PutActorsRequest([Propietario(), locatarioJuridico]), ct);

        error.Should().BeNull();
        await _kyverumClient.DidNotReceiveWithAnyArgs().StartVerificationAsync(default!, TestContext.Current.CancellationToken);
    }
}
