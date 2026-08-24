using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11612 — la portada del consolidado mostraba SIEMPRE un guion en "Compañía radicadora":
/// <c>ExpedienteCoverInfoBuilder</c> lee la clave <c>company_name</c> de <c>field_values</c> y en el
/// sistema no existía ningún productor de esa clave. La fuente autoritativa es el tenant dueño del
/// trámite (<c>identity.tenants.legal_name</c>), resuelto por <see cref="ICompaniaRadicadoraDirectory"/>.
///
/// <para><b>El nombre NO se persiste</b> (corrección de code review): escribir <c>company_name</c> en
/// <c>field_values</c> revienta contra el trigger <c>tramites.trg_field_value_immutable</c>, que
/// prohibe insertar/actualizar/borrar en trámites radicados — y el consolidado se genera sobre
/// trámites radicados: el INSERT abortaba la transacción y se perdía también el consolidado recién
/// generado. El valor viaja por parámetro hasta la portada. Los tests fijan las dos mitades: el nombre
/// llega a la portada y NO se escribe una sola fila.</para>
///
/// <para>Uso de ejemplo:
/// <code>
/// var handler = new GenerarConsolidadoMaestroHandler(repo, merger, storage,
///     companiaRadicadoraDirectory: new DirectorioFake("FLIT SAS"));
/// var (result, error) = await handler.HandleAsync(id, tenantId, ct: ct);
/// // merger.UltimaPortada!.CompaniaRadicadora == "FLIT SAS"
/// </code>
/// </para>
/// </summary>
public sealed class CompaniaRadicadoraPortadaTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly MergerEspia _merger = new();
    private readonly FakeStorage _storage = new();

    private GenerarConsolidadoMaestroHandler Maestro(ICompaniaRadicadoraDirectory directorio) =>
        new(_repo, _merger, _storage, companiaRadicadoraDirectory: directorio);

    private GenerarConsolidadoHandler Wizard(ICompaniaRadicadoraDirectory directorio) =>
        new(_repo, _merger, _storage, companiaRadicadoraDirectory: directorio);

    // ---------------------------------------------------------------- AC1

    [Fact]
    public async Task AC1_ConCompaniaRegistrada_LaPortadaLlevaSuNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await Maestro(new DirectorioFake("TRANSPORTES DEL VALLE S.A.S."))
            .HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        // Este assert es el que FALLA si el campo vuelve a quedar vacío (AC5).
        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("TRANSPORTES DEL VALLE S.A.S.");
    }

    [Fact]
    public async Task AC1_ElNombreNoSePersisteEnFieldValues()
    {
        // Contrato bloqueante: `field_values` es INMUTABLE en trámites radicados (trigger
        // trg_field_value_immutable). Si alguien vuelve a persistir el nombre, este test cae en rojo
        // antes de que en producción el INSERT tumbe la transacción y se pierda el consolidado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        await Maestro(new DirectorioFake("FLIT SAS")).HandleAsync(id, tenantId, ct: ct);

        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("FLIT SAS");
        instance.FieldValues.Should().BeEmpty("el nombre solo vive en memoria");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceFieldValue>());
    }

    [Fact]
    public async Task AC1_EnElConsolidadoDelWizard_TampocoSePersiste()
    {
        // El otro generador de portada sigue exactamente la misma regla.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        await Wizard(new DirectorioFake("FLIT SAS")).HandleAsync(id, tenantId, ct);

        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("FLIT SAS");
        instance.FieldValues.Should().BeEmpty();
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceFieldValue>());
    }

    // ---------------------------------------------------------------- AC2

    [Theory]
    [InlineData("FLIT SAS")]
    [InlineData("MOVILIDAD ANDINA LTDA")]
    public async Task AC2_ElValorEsPorTramite_NoUnaConstante(string razonSocial)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var directorio = new DirectorioFake(razonSocial);
        await Maestro(directorio).HandleAsync(id, tenantId, ct: ct);

        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be(razonSocial);
        // Aislamiento multi-tenant: se pregunta EXCLUSIVAMENTE por el tenant dueño del trámite.
        directorio.TenantsConsultados.Should().Equal(tenantId);
    }

    // ---------------------------------------------------------------- AC3

    [Fact]
    public async Task AC3_SinCompaniaRadicadora_LaPortadaQuedaVacia_YNoInterrumpeLaGeneracion()
    {
        // Directorio que no resuelve (tenant sin razón social): el campo sigue nulo y el generador de
        // portada lo imprime como '-' (FlitCoverPageGenerator.Val), sin excepción.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await Maestro(new DirectorioFake(null)).HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        _merger.UltimaPortada!.CompaniaRadicadora.Should().BeNull();
        instance.FieldValues.Should().BeEmpty(); // no se inventa la clave
    }

    [Fact]
    public async Task AC3_SinDirectorioCableado_ElComportamientoEsElPrevio()
    {
        // Default inerte: sin el puerto registrado nada cambia (no hay regresión en composiciones
        // que no lo cablean, como los tests preexistentes del consolidado).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        _merger.UltimaPortada!.CompaniaRadicadora.Should().BeNull();
    }

    // ---------------------------------------------------------------- AC4 (retroactividad)

    [Fact]
    public async Task AC4_TramiteAnteriorConConsolidadoVigente_ConservaElGuionHastaQueSeInvalide()
    {
        // LIMITACIÓN ACEPTADA A CONCIENCIA. Sin marcador persistido no hay forma de distinguir "hay
        // que regenerar una vez" de "hay que regenerar siempre": condicionar el atajo de caché a que
        // falte la compañía regeneraría el consolidado en CADA acceso. Se prefiere respetar la caché,
        // así que un trámite antiguo con el maestro VIGENTE sigue mostrando el guion hasta que el
        // consolidado se invalide por las vías normales (regenerar el FUR, cambiar de estado, adjuntar
        // documentos...). El AC4 del Bug #11612 queda cubierto solo desde esa invalidación.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"), ("consolidado_maestro", "maestro.pdf"));
        instance.ConsolidadoMaestroVigente = true;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var directorio = new DirectorioFake("FLIT SAS");
        var (result, error) = await Maestro(directorio).HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse("la caché manda: no se regenera por la compañía");
        directorio.TenantsConsultados.Should().BeEmpty("ni se consulta el directorio si no se compone PDF");
        _merger.UltimaPortada.Should().BeNull();
    }

    [Fact]
    public async Task AC4_TramiteAnteriorAlInvalidarseElConsolidado_YaLlevaElNombre()
    {
        // La otra mitad de la limitación: en cuanto el consolidado se invalida por las vías normales
        // (aquí, el maestro deja de estar vigente), la portada ya sale con la compañía. No hace falta
        // ninguna migración ni marcador para reparar los expedientes existentes.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"), ("consolidado_maestro", "maestro.pdf"));
        instance.ConsolidadoMaestroVigente = false;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await Maestro(new DirectorioFake("FLIT SAS")).HandleAsync(id, tenantId, ct: ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("FLIT SAS");
        instance.FieldValues.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_ConsolidadoDelWizardVigente_TampocoSeRegeneraPorLaCompania()
    {
        // Mismo criterio en el consolidado del wizard: la caché queda como estaba antes del cambio.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"), ("consolidado", "consolidado.pdf"));
        instance.ConsolidadoWizardVigente = true;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await Wizard(new DirectorioFake("FLIT SAS")).HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        instance.FieldValues.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_ConsolidadoDelWizardForzado_LlevaElNombreEnLaPortada()
    {
        // `force: true` es una de las vías normales de regeneración: ahí sí se resuelve la compañía.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"), ("consolidado", "consolidado.pdf"));
        instance.ConsolidadoWizardVigente = true;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await Wizard(new DirectorioFake("FLIT SAS")).HandleAsync(id, tenantId, userId: null, force: true, ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("FLIT SAS");
        instance.FieldValues.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- Contrato del backfill

    [Fact]
    public async Task LoQueEscribioElOperadorManda_NoSePisa()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ("fur", "fur.pdf"));
        instance.FieldValues.Add(FieldValue(id, tenantId, "company_name", "RAZÓN SOCIAL CAPTURADA", "user"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var directorio = new DirectorioFake("FLIT SAS");
        await Maestro(directorio).HandleAsync(id, tenantId, ct: ct);

        _merger.UltimaPortada!.CompaniaRadicadora.Should().Be("RAZÓN SOCIAL CAPTURADA");
        directorio.TenantsConsultados.Should().BeEmpty();
        instance.FieldValues.Single(f => f.FieldKey == "company_name").Source.Should().Be("user");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceFieldValue>());
    }

    [Fact]
    public void ElAliasHistoricoRadicadora_TambienCuentaComoCompania()
    {
        // ExpedienteCoverInfoBuilder acepta `radicadora` como valor de reserva: si está poblado, no
        // hay hueco que rellenar.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.FieldValues.Add(FieldValue(id, tenantId, "radicadora", "COMPAÑÍA HEREDADA", "user"));

        CompaniaRadicadoraResolver.Falta(instance).Should().BeFalse();
        // El alias poblado gana incluso frente al valor resuelto del directorio.
        ExpedienteCoverInfoBuilder.FromInstance(instance, "FLIT SAS").CompaniaRadicadora
            .Should().Be("COMPAÑÍA HEREDADA");
    }

    [Fact]
    public void FilaPresentePeroVacia_LaPortadaUsaElValorResuelto()
    {
        // La fila existe pero vacía (no se puede completar: field_values es inmutable). La portada cae
        // al valor resuelto en memoria en vez de imprimir el guion.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.FieldValues.Add(FieldValue(id, tenantId, "company_name", "   ", "user"));

        CompaniaRadicadoraResolver.Falta(instance).Should().BeTrue();
        ExpedienteCoverInfoBuilder.FromInstance(instance, "FLIT SAS").CompaniaRadicadora.Should().Be("FLIT SAS");
    }

    [Fact]
    public void ConClaveDuplicada_LaPortadaNoRevienta()
    {
        // Sin índice único sobre (procedure_instance_id, field_key), dos filas de la misma clave
        // hacían que el ToDictionary de la portada lanzara ArgumentException. Gana el valor no vacío
        // más reciente.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        var vieja = FieldValue(id, tenantId, "company_name", "COMPAÑÍA VIEJA", "system");
        vieja.CreatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        instance.FieldValues.Add(vieja);
        instance.FieldValues.Add(FieldValue(id, tenantId, "company_name", "COMPAÑÍA NUEVA", "system"));

        var portada = ExpedienteCoverInfoBuilder.FromInstance(instance);

        portada.CompaniaRadicadora.Should().Be("COMPAÑÍA NUEVA");
    }

    // ---------------------------------------------------------------- Helpers

    private static ProcedureInstanceFieldValue FieldValue(
        Guid instanceId, Guid tenantId, string key, string? value, string source) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            FieldKey = key,
            ValueText = value,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private ProcedureInstance Instance(Guid id, Guid tenantId, params (string tipo, string filename)[] attachments)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000123",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var (tipo, filename) in attachments)
        {
            var path = $"{id:D}/{tipo}";
            var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
            _storage.Files[path] = content;
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = tipo,
                Filename = filename,
                Mimetype = "application/pdf",
                SizeBytes = content.Length,
                Sha256 = $"sha-{tipo}",
                StoragePath = path,
                Source = "user",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        return instance;
    }

    private sealed class DirectorioFake(string? razonSocial) : ICompaniaRadicadoraDirectory
    {
        public List<Guid> TenantsConsultados { get; } = [];

        public Task<string?> GetRazonSocialAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            TenantsConsultados.Add(tenantId);
            return Task.FromResult(razonSocial);
        }
    }

    private sealed class MergerEspia : IExpedienteConsolidadoMerger
    {
        public ExpedienteCoverInfo? UltimaPortada { get; private set; }

        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request)
        {
            UltimaPortada = request.Cover;
            return Merge(request.Parts.Select(p => p.Pdf).ToList());
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];

        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Files[path] = bytes;
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Files.TryGetValue(storagePath, out var bytes)
                ? Task.FromResult<Stream?>(new MemoryStream(bytes))
                : Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
