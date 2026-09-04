using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11613 (causa de fondo) — <c>organismo_requerido</c> se producía en trámites que SÍ tienen
/// organismo: <c>CreateProcedureInstanceCommand</c> escribe la COLUMNA <c>transit_office_id</c> desde el
/// request (camino de los borradores originados en ICT) sin escribir ninguna clave
/// <c>transit_office_*</c> en field_values, y <c>TramiteLifecycleService</c> solo promueve field_values →
/// columna al radicar, nunca al revés. El gate del generador lee únicamente field_values.
///
/// <para><b>El relleno NO se persiste</b> (corrección de code review): el trigger
/// <c>tramites.trg_field_value_immutable</c> rechaza cualquier INSERT/UPDATE/DELETE sobre
/// <c>field_values</c> de un trámite radicado — y este camino corre siempre sobre trámites radicados —,
/// así que el INSERT abortaba la transacción y se perdía también el documento recién generado. Los
/// valores viajan en el diccionario en memoria que alimenta al generador. Los tests de abajo verifican
/// justamente eso: que el organismo llega al FUR y que NO se escribe una sola fila.</para>
///
/// <para>Uso de ejemplo:
/// <code>
/// var handler = new GenerarFurHandler(..., transitOfficeResolver: resolver);
/// var (result, error) = await handler.HandleAsync(instanceId, tenantId, ct); // error == null
/// </code>
/// </para>
/// </summary>
public sealed class FurOrganismoBackfillTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumCertificateClient _certClient = Substitute.For<IKyverumCertificateClient>();
    private readonly IRuesCertificateGenerator _ruesGenerator = Substitute.For<IRuesCertificateGenerator>();
    private readonly IRnmcCertificateGenerator _rnmcGenerator = Substitute.For<IRnmcCertificateGenerator>();
    private readonly IProcedureInstancePrendaRepository _prendaRepo = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly FakeStorage _storage = new();

    private readonly GeneradorEspia _generador = new();

    private GenerarFurHandler Handler(ITransitOfficeResolver? resolver) =>
        new(_repo, _generador, _certClient, _ruesGenerator, _rnmcGenerator,
            _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance,
            transitOfficeResolver: resolver);

    /// <summary>
    /// Aserción compartida: la generación NO puede escribir en <c>field_values</c>. Si alguien vuelve a
    /// persistir el relleno (fila nueva o modificación de una existente), este assert cae en rojo antes
    /// de que el trigger de inmutabilidad tumbe la transacción en producción.
    /// </summary>
    private void NoSeEscribioNingunFieldValue(ProcedureInstance instance, int filasIniciales)
    {
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceFieldValue>());
        instance.FieldValues.Should().HaveCount(filasIniciales, "field_values es inmutable en trámites radicados");
        instance.FieldValues.Should().NotContain(f => f.Source == "system");
    }

    [Fact]
    public async Task ConOrganismoEnLaColumna_ElOrganismoLlegaAlDocumento_SinEscribirFieldValues()
    {
        // AC6: trámite con código de organismo disponible ⇒ no se produce organismo_requerido, y el
        // organismo resuelto llega REALMENTE al documento (antes solo se comprobaba que se había
        // llamado a repo.Add, que es exactamente lo que ahora está prohibido).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var officeId = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.TransitOfficeId = officeId; // columna poblada al crear (ICT / OT explícito)
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await Handler(
            new ResolverFake(new ResolvedTransitOffice(officeId, "05001000", "Medellín", "05001")))
            .HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("fur");

        // El dato viaja al generador, que es lo único que importa para el FUR.
        _generador.Ultima!.Organismo.Codigo.Should().Be("05001000");
        _generador.Ultima!.Organismo.Nombre.Should().Be("Medellín");

        // ... y NO queda rastro en field_values (el trigger de inmutabilidad lo rechazaría).
        NoSeEscribioNingunFieldValue(instance, filasIniciales: 0);
    }

    [Fact]
    public async Task SinOrganismoEnColumnaNiFieldValues_SigueRechazando()
    {
        // Edge case / no-regresión: sin organismo por ningún lado el gate se comporta como siempre.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await Handler(new ResolverFake(null)).HandleAsync(id, tenant, ct);

        error.Should().Be("organismo_requerido");
        _storage.Saved.Should().BeEmpty();
        NoSeEscribioNingunFieldValue(instance, filasIniciales: 0);
    }

    [Fact]
    public async Task ConColumnaPeroSinGrantVigente_NoInventaOrganismo()
    {
        // Contrato con ITransitOfficeResolver: si el OT no está habilitado para la empresa o está
        // inactivo en el catálogo, NO se rellena un código inventado — se mantiene el rechazo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.TransitOfficeId = Guid.NewGuid();
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await Handler(new ResolverFake(null)).HandleAsync(id, tenant, ct);

        error.Should().Be("organismo_requerido");
        instance.FieldValues.Should().BeEmpty();
    }

    [Fact]
    public async Task ConFieldValueYaPresente_NoLoPisa()
    {
        // El backfill es un RESPALDO: lo que capturó el operador manda.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.TransitOfficeId = Guid.NewGuid();
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var resolver = new ResolverFake(
            new ResolvedTransitOffice(instance.TransitOfficeId.Value, "05001000", "Medellín", "05001"));
        var (_, error) = await Handler(resolver).HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        resolver.Llamadas.Should().Be(0); // ni siquiera se consulta el catálogo
        instance.FieldValues.Single(f => f.FieldKey == "transit_office_code").ValueText.Should().Be("11001000");
        _generador.Ultima!.Organismo.Codigo.Should().Be("11001000");
        NoSeEscribioNingunFieldValue(instance, filasIniciales: 1);
    }

    [Fact]
    public async Task ConValorDelOperadorParcial_ElRellenoCompletaSinPisarloYNoPersiste()
    {
        // Edge case: el operador capturó el nombre pero no el código. El relleno aporta lo que falta y
        // respeta lo capturado; sigue sin escribir nada.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var officeId = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.TransitOfficeId = officeId;
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_name",
            ValueText = "NOMBRE CAPTURADO",
            Source = "user",
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await Handler(
            new ResolverFake(new ResolvedTransitOffice(officeId, "05001000", "Medellín", "05001")))
            .HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _generador.Ultima!.Organismo.Codigo.Should().Be("05001000");
        _generador.Ultima!.Organismo.Nombre.Should().Be("NOMBRE CAPTURADO");
        NoSeEscribioNingunFieldValue(instance, filasIniciales: 1);
    }

    [Fact]
    public async Task ConCityNameLegibleYDivipolaEnFieldValues_CiudadDelFurEsElNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "25286000",
            Source = "user",
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_name",
            ValueText = "STRIA TTEyTTO MCPAL FUNZA",
            Source = "user",
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_city",
            ValueText = "25286",
            Source = "consultation",
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_city_name",
            ValueText = "FUNZA",
            Source = "consultation",
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await Handler(new ResolverFake(null)).HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _generador.Ultima!.Organismo.Ciudad.Should().Be("FUNZA");
        NoSeEscribioNingunFieldValue(instance, filasIniciales: 4);
    }

    [Fact]
    public async Task ConClaveDuplicadaEnFieldValues_LaGeneracionNoRevienta()
    {
        // No hay índice único sobre (procedure_instance_id, field_key): dos filas de la misma clave
        // hacían que el ToDictionary del handler lanzara ArgumentException y ese trámite se quedaba SIN
        // poder generar documentos nunca más. Gana el valor no vacío más reciente.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "05001000",
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "system",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await Handler(new ResolverFake(null)).HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        _generador.Ultima!.Organismo.Codigo.Should().Be("11001000");
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Delega en el generador mock y retiene los datos con los que se armó el documento.</summary>
    private sealed class GeneradorEspia : IFurDocumentGenerator
    {
        private readonly MockFurDocumentGenerator _inner = new();

        public FurDocumentData? Ultima { get; private set; }

        public GeneratedDocument GenerateFur(FurDocumentData data)
        {
            Ultima = data;
            return _inner.GenerateFur(data);
        }

        public GeneratedDocument GenerateFurFillAll(FurTemplateFormat format = FurTemplateFormat.Automotor) =>
            _inner.GenerateFurFillAll(format);

        public GeneratedDocument GenerateCompraventa(FurDocumentData data)
        {
            Ultima = data;
            return _inner.GenerateCompraventa(data);
        }
    }

    private sealed class ResolverFake(ResolvedTransitOffice? office) : ITransitOfficeResolver
    {
        public int Llamadas { get; private set; }

        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            return Task.FromResult(office);
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
