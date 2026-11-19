using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// La firma de baúl se COPIA al trámite al radicar. Cubre el capturador (qué se guarda y cuándo), el resolver de
/// identidad (usa la copia y NUNCA el baúl en vivo desde que existe) y el listado (baúl vencido ya no «Rechazado»;
/// el radicado conserva «firmado» por su copia).
/// </summary>
public sealed class FirmaBaulCopiaAlRadicarTests
{
    private const string DocumentoRl = "1090123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();

    // ── Capturador ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Capturar_ConBaulVigente_GuardaUnaCopiaPorSlot_CopiaElArchivo_YMarcaLaInstancia()
    {
        var instance = Tramite(juridico: true);
        var firma = FirmaVigente();
        PrepararStorage(firma.StoragePath, [9, 8, 7]);

        await Capturador(new StubBaul(firma)).CapturarAsync(instance, TestContext.Current.CancellationToken);

        instance.FirmaBaulCopiadaAt.Should().NotBeNull();
        await _repo.Received(1).AddFirmaBaulCopiasAsync(
            Arg.Is<IReadOnlyCollection<ProcedureInstanceFirmaBaulCopia>>(c =>
                c.Count == 2
                && c.Any(x => x.SlotKey == "comprador" && x.PartyRole == "comprador")
                && c.Any(x => x.SlotKey == "vendedor" && x.PartyRole == "vendedor")
                && c.All(x => x.SignatureVaultId == firma.SignatureVaultId
                    && x.VigenciaDesde == firma.VigenciaDesde
                    && x.VigenciaHasta == firma.VigenciaHasta
                    && x.StoragePath == "copia/propia.png"
                    && x.SourceStoragePath == firma.StoragePath
                    && x.StorageSha256 == "sha-copia"
                    && x.CapturedAt <= DateTimeOffset.UtcNow
                    && x.ProcedureInstanceId == instance.Id
                    && x.TenantId == instance.TenantId)),
            Arg.Any<CancellationToken>());
        // La imagen se copia a almacenamiento propio del trámite (no solo se referencia la del baúl).
        await _storage.Received(2).SaveAsync(
            instance.Id, FirmaBaulCopiaCapturer.TipoArchivoCopia, Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capturar_SinNingunaFirmaDeBaul_MarcaLaInstanciaIgual_SinFilas()
    {
        // Aun sin baúl se marca: desde ahí el baúl en vivo ya no cuenta, y no se «adopta» uno que aparezca después.
        var instance = Tramite(juridico: true);

        await Capturador(new StubBaul(null)).CapturarAsync(instance, TestContext.Current.CancellationToken);

        instance.FirmaBaulCopiadaAt.Should().NotBeNull();
        await _repo.DidNotReceive().AddFirmaBaulCopiasAsync(
            Arg.Any<IReadOnlyCollection<ProcedureInstanceFirmaBaulCopia>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capturar_PersonaNatural_NoCopiaNada_AunqueElBaulResuelva()
    {
        // El baúl solo aplica a actores jurídicos (FirmaBaulCobertura.Aplica).
        var instance = Tramite(juridico: false);

        await Capturador(new StubBaul(FirmaVigente())).CapturarAsync(instance, TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().AddFirmaBaulCopiasAsync(
            Arg.Any<IReadOnlyCollection<ProcedureInstanceFirmaBaulCopia>>(), Arg.Any<CancellationToken>());
        instance.FirmaBaulCopiadaAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Capturar_MecanismoIdentidadElegido_NoCopiaElBaul()
    {
        var instance = Tramite(juridico: true, mecanismo: MecanismoFirma.Identidad);

        await Capturador(new StubBaul(FirmaVigente())).CapturarAsync(instance, TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().AddFirmaBaulCopiasAsync(
            Arg.Any<IReadOnlyCollection<ProcedureInstanceFirmaBaulCopia>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capturar_ArchivoIlegible_ConservaLaReferenciaOriginal_SinRomper()
    {
        var instance = Tramite(juridico: true);
        var firma = FirmaVigente();
        _storage.OpenReadAsync(firma.StoragePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(null));

        await Capturador(new StubBaul(firma)).CapturarAsync(instance, TestContext.Current.CancellationToken);

        await _repo.Received(1).AddFirmaBaulCopiasAsync(
            Arg.Is<IReadOnlyCollection<ProcedureInstanceFirmaBaulCopia>>(c =>
                c.Count == 2 && c.All(x => x.StoragePath == firma.StoragePath)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capturar_YaCopiada_NoVuelveAConsultarElBaul()
    {
        var instance = Tramite(juridico: true);
        instance.FirmaBaulCopiadaAt = DateTimeOffset.UtcNow.AddDays(-1);
        var baul = Substitute.For<ISignatureVaultPolicy>();

        await Capturador(baul).CapturarAsync(instance, TestContext.Current.CancellationToken);

        await baul.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), TestContext.Current.CancellationToken);
    }

    // ── IdentityApprovalResolver ──────────────────────────────────────────────

    [Fact]
    public async Task Identidad_TramiteRadicadoConCopia_QuedaAprobadoAunqueElBaulYaNoLaResuelva()
    {
        var instance = Tramite(juridico: true);
        instance.FirmaBaulCopiadaAt = DateTimeOffset.UtcNow.AddDays(-5);
        _repo.ListFirmaBaulCopiasAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(CopiasPara(instance));

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            _repo, instance, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken, new StubBaul(null));

        aprobadas.Should().Contain("comprador").And.Contain("vendedor");
    }

    [Fact]
    public async Task Identidad_TramiteRadicadoSinCopiaDeLaPersona_NoAdoptaElBaulEnVivo()
    {
        var instance = Tramite(juridico: true);
        instance.FirmaBaulCopiadaAt = DateTimeOffset.UtcNow.AddDays(-5); // radicado sin ninguna firma de baúl

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            _repo, instance, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken, new StubBaul(FirmaVigente()));

        aprobadas.Should().NotContain("comprador", "con copia tomada, el baúl en vivo no cuenta");
    }

    [Fact]
    public async Task Identidad_Borrador_SigueLeyendoElBaulEnVivo()
    {
        var instance = Tramite(juridico: true); // FirmaBaulCopiadaAt == null

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            _repo, instance, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken, new StubBaul(FirmaVigente()));

        aprobadas.Should().Contain("comprador").And.Contain("vendedor");
        await _repo.DidNotReceive().ListFirmaBaulCopiasAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Listado ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listado_BaulVencido_YaNoProduceRechazado_QuedaPendiente()
    {
        var instance = Tramite(juridico: true);
        PrepararListado(instance, baulVigente: false);

        var fila = (await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken)).Single();

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task Listado_BaulVencido_PeroVidPropiaRechazada_SiEsRechazado()
    {
        var instance = Tramite(juridico: true);
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Rechazado,
            DocumentType = "CC",
            DocumentNumber = DocumentoRl,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        PrepararListado(instance, baulVigente: false);

        var fila = (await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken)).Single();

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Rechazado);
    }

    [Fact]
    public async Task Listado_TramiteRadicadoConCopia_SigueFirmado_AunqueElBaulVivoYaVencio()
    {
        var instance = Tramite(juridico: true);
        instance.Status = TramiteEstado.Entregado;
        instance.FirmaBaulCopiadaAt = DateTimeOffset.UtcNow.AddDays(-5);
        PrepararListado(instance, baulVigente: false); // el baúl en vivo del tenant ya está vencido
        _repo.ListFirmaBaulCopiasPorInstanciasAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<ProcedureInstanceFirmaBaulCopia>>
            {
                [instance.Id] = CopiasPara(instance),
            });

        var fila = (await new ListProcedureInstancesHandler(_repo)
            .HandleAsync(instance.TenantId, TestContext.Current.CancellationToken)).Single();

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FirmaBaulCopiaCapturer Capturador(ISignatureVaultPolicy baul) =>
        new(_repo, baul, _storage, NullLogger<FirmaBaulCopiaCapturer>.Instance);

    private void PrepararStorage(string origen, byte[] contenido)
    {
        _storage.OpenReadAsync(origen, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(contenido)));
        _storage.SaveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("copia/propia.png", "sha-copia", contenido.Length));
    }

    private void PrepararListado(ProcedureInstance instance, bool baulVigente)
    {
        _repo.ListWithSummaryGraphAsync(
                instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        var claves = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var actor in instance.Actors)
        {
            var sujeto = IdentitySubjectResolver.For(actor);
            claves[BiometricRules.IdentidadKey(instance.TenantId, sujeto.TipoDocumento!, sujeto.NumeroDocumento!)] = baulVigente;
        }

        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(claves);
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    private static List<ProcedureInstanceFirmaBaulCopia> CopiasPara(ProcedureInstance instance)
    {
        var firma = FirmaVigente();
        return instance.Actors.Select(a =>
        {
            var sujeto = IdentitySubjectResolver.For(a);
            return new ProcedureInstanceFirmaBaulCopia
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                SlotKey = a.ActorType,
                PartyRole = a.ActorType,
                DocumentType = sujeto.TipoDocumento!,
                DocumentNumber = sujeto.NumeroDocumento!,
                SignatureVaultId = firma.SignatureVaultId,
                FullName = firma.FullName,
                SignatureHash = firma.SignatureHash,
                VigenciaDesde = firma.VigenciaDesde,
                VigenciaHasta = firma.VigenciaHasta,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-5),
                StoragePath = "copia/propia.png",
                StorageSha256 = "sha-copia",
            };
        }).ToList();
    }

    private static ProcedureInstance Tramite(bool juridico, string? mecanismo = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000777",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var rol in new[] { "comprador", "vendedor" })
        {
            instance.Actors.Add(new ProcedureInstanceActor
            {
                ActorType = rol,
                DocumentType = juridico ? "NIT" : "CC",
                DocumentNumber = juridico ? "900123456" : DocumentoRl,
                PersonType = juridico ? "juridical" : "natural",
                FullName = "Renting SAS",
                Email = "renting@x.com",
                Phone = "3001234567",
                Metadata = juridico
                    ? ActorMetadataReader.Serialize(
                        "Bogotá",
                        "Calle 1 # 2-3",
                        new ActorRepresentanteLegal("CC", DocumentoRl, "Ana Representante", "ana@x.com", null, mecanismo))
                    : "{}",
            });
        }

        return instance;
    }

    private static SignatureVaultMatch FirmaVigente() => new(
        Guid.NewGuid(), "Ana Representante", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        DocumentoRl);

    private sealed class StubBaul(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
