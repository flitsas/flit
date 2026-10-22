using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12797 — retener el consolidado anterior hasta confirmar la nueva generación, en los tres
/// handlers que lo sustituyen (<see cref="GenerarConsolidadoHandler"/>,
/// <see cref="GenerarConsolidadoMaestroHandler"/>, <see cref="CargarConsolidadoExternoHandler"/>).
/// Storage y repositorio escriben en una bitácora compartida para verificar el ORDEN de operaciones.
/// <para>Uso de ejemplo:</para>
/// <code>
/// var f = new Fixture(Handler.Wizard, conPrevio: true);
/// f.Repo.SaveChangesAsync(default).Returns(Task.FromException(new InvalidOperationException()));
/// var r = await f.EjecutarAsync();   // r.Excepcion != null; el previo sigue en storage y en el grafo
/// </code>
/// </summary>
public sealed class ConsolidadoReemplazoSeguroTests
{
    public enum Handler { Wizard, Maestro, Admin }

    private const string RutaPrevia = "prev/consolidado_v0";

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public bool FallarComposicion { get; set; }
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;
        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => FallarComposicion
            ? throw new InvalidOperationException("merge falló")
            : Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class RecordingStorage(List<string> log) : IAttachmentStorage
    {
        private int _versiones;
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool FallarSubida { get; set; }
        public bool FallarBorrado { get; set; }
        public string? RutaFija { get; set; }
        public List<string> Guardados { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            if (FallarSubida)
            {
                log.Add("save:fail");
                throw new IOException("storage caído");
            }
            var path = RutaFija ?? $"{procedureInstanceId:D}/{tipo}_v{++_versiones}";
            Files[path] = ms.ToArray();
            Guardados.Add(path);
            log.Add($"save:{path}");
            return new StoredFile(path, $"sha-{tipo}-{_versiones}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
            log.Add($"delete:{storagePath}");
            if (FallarBorrado)
                throw new IOException("delete falló");
            Files.Remove(storagePath);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed record Resultado(ConsolidadoDocumentDto? Dto, string? Error, Exception? Excepcion);

    private sealed class Fixture
    {
        public List<string> Log { get; } = [];
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();
        public RecordingStorage Storage { get; }
        public FakeMerger Merger { get; } = new();
        public ProcedureInstance Instance { get; }
        public Handler Kind { get; }
        public ProcedureInstanceAttachment? Previo { get; }
        public string Tipo => Kind == Handler.Maestro ? "consolidado_maestro" : "consolidado";

        public Fixture(Handler kind, bool conPrevio)
        {
            Kind = kind;
            Storage = new RecordingStorage(Log);
            var id = Guid.NewGuid();
            var tenantId = Guid.NewGuid();
            Instance = new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
                Id = id,
                TenantId = tenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-012797",
                Status = TramiteEstado.Borrador,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            foreach (var tipo in new[] { "fur", "certificado_identidad", "certificado_identidad_vendedor", "compraventa", "impronta", "soat", "rtm" })
                Agregar(tipo, $"{id:D}/{tipo}", tipo is "fur" or "certificado_identidad" or "certificado_identidad_vendedor" ? "system" : "user");
            if (conPrevio)
                Previo = Agregar(Tipo, RutaPrevia, "system");

            Repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(Instance);
            Repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(Instance);
            Repo.When(r => r.RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>()))
                .Do(c => Log.Add($"remove_row:{c.Arg<ProcedureInstanceAttachment>().StoragePath}"));
            Repo.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => Log.Add("save_changes"));
        }

        private ProcedureInstanceAttachment Agregar(string tipo, string path, string source)
        {
            Storage.Files[path] = Encoding.UTF8.GetBytes($"%PDF-{tipo}");
            var a = new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = Instance.TenantId,
                ProcedureInstanceId = Instance.Id,
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
                Mimetype = "application/pdf",
                SizeBytes = 10,
                Sha256 = $"sha-{tipo}",
                StoragePath = path,
                Source = source,
                UploadedAt = DateTimeOffset.UtcNow.AddDays(-1),
            };
            Instance.Attachments.Add(a);
            return a;
        }

        public void FallarGuardado() =>
            Repo.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(_ => { Log.Add("save_changes:fail"); return Task.FromException(new InvalidOperationException("db caída")); });

        public bool Bandera => Kind == Handler.Maestro ? Instance.ConsolidadoMaestroVigente : Instance.ConsolidadoWizardVigente;

        public async Task<Resultado> EjecutarAsync()
        {
            try
            {
                switch (Kind)
                {
                    case Handler.Wizard:
                    {
                        var (r, e) = await new GenerarConsolidadoHandler(Repo, Merger, Storage)
                            // Sin force: con la bandera en false ya regenera, y force añadiría un SaveChanges intermedio
                            // (invalidación, Feature #11066) que no es el punto que se ejercita aquí.
                            .HandleAsync(Instance.Id, Instance.TenantId, userId: null, force: false, CancellationToken.None);
                        return new Resultado(r?.Document, e, null);
                    }
                    case Handler.Maestro:
                    {
                        var (r, e) = await new GenerarConsolidadoMaestroHandler(Repo, Merger, Storage)
                            .HandleAsync(Instance.Id, Instance.TenantId, force: true, ct: CancellationToken.None);
                        return new Resultado(r?.Document, e, null);
                    }
                    default:
                    {
                        var input = new CargarConsolidadoExternoInput(
                            "externo.pdf", "application/pdf", 12, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-externo")));
                        var (r, e) = await new CargarConsolidadoExternoHandler(Repo, Storage)
                            .HandleAsync(Instance.Id, Instance.TenantId, input, Guid.NewGuid(), CancellationToken.None);
                        return new Resultado(r, e, null);
                    }
                }
            }
            catch (Exception ex)
            {
                return new Resultado(null, null, ex);
            }
        }

        public List<ProcedureInstanceAttachment> Consolidados() =>
            Instance.Attachments.Where(a => string.Equals(a.Tipo, Tipo, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ───────────────────────── AC1 — éxito ─────────────────────────

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC1_Exito_SubeNuevo_Guarda_YSoloDespuesBorraElAnterior(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: true);

        var r = await f.EjecutarAsync();

        r.Excepcion.Should().BeNull();
        r.Error.Should().BeNull();
        var nuevo = f.Storage.Guardados.Should().ContainSingle().Subject;
        nuevo.Should().NotBe(RutaPrevia, "cada versión tiene su propia clave");

        var iSubida = f.Log.IndexOf($"save:{nuevo}");
        var iFila = f.Log.IndexOf($"remove_row:{RutaPrevia}");
        var iGuardado = f.Log.IndexOf("save_changes");
        var iBorrado = f.Log.IndexOf($"delete:{RutaPrevia}");
        iSubida.Should().BeGreaterThanOrEqualTo(0);
        iSubida.Should().BeLessThan(iFila, "la fila anterior se retira solo con el nuevo ya subido");
        iFila.Should().BeLessThan(iGuardado);
        iGuardado.Should().BeLessThan(iBorrado, "el binario anterior se borra SOLO tras confirmar el guardado");
        f.Log.Should().NotContain($"delete:{nuevo}");

        f.Consolidados().Should().ContainSingle().Which.StoragePath.Should().Be(nuevo);
        f.Storage.Files.Should().ContainKey(nuevo).And.NotContainKey(RutaPrevia);
        f.Bandera.Should().BeTrue();
    }

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC1_BorradoDelAnteriorFallaTrasConfirmar_NoRevientaLaPeticion(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: true);
        f.Storage.FallarBorrado = true;

        var r = await f.EjecutarAsync();

        r.Excepcion.Should().BeNull();
        r.Error.Should().BeNull();
        r.Dto.Should().NotBeNull();
        f.Log.Should().Contain($"delete:{RutaPrevia}");
        f.Consolidados().Should().ContainSingle().Which.StoragePath.Should().Be(f.Storage.Guardados.Single());
        f.Bandera.Should().BeTrue();
    }

    [Fact]
    public async Task AC1_MismaClaveParaNuevoYAnterior_NoBorraElBinarioRecienConfirmado()
    {
        var log = new List<string>();
        var storage = new RecordingStorage(log);
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var instance = new ProcedureInstance { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        var previo = new ProcedureInstanceAttachment { Id = Guid.NewGuid(), Tipo = "consolidado", StoragePath = "k/fija" };
        instance.Attachments.Add(previo);
        var nuevoAdj = new ProcedureInstanceAttachment { Id = Guid.NewGuid(), Tipo = "consolidado", StoragePath = "k/fija" };
        var previos = ConsolidadoReemplazoSeguro.Previos(instance, "consolidado");

        ConsolidadoReemplazoSeguro.RetirarFilas(instance, repo, previos);
        instance.Attachments.Add(nuevoAdj);
        await ConsolidadoReemplazoSeguro.ConfirmarAsync(
            instance, repo, storage, new StoredFile("k/fija", "sha", 1), nuevoAdj, previos, null, null, CancellationToken.None);

        log.Should().NotContain("delete:k/fija");
        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(nuevoAdj);
    }

    // ───────────────────────── AC2 — fallo en cualquier punto ─────────────────────────

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC2_FallaElGuardado_ElAnteriorSigueDescargable_YLaBanderaEnFalse(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: true);
        f.FallarGuardado();

        var r = await f.EjecutarAsync();

        r.Excepcion.Should().BeOfType<InvalidOperationException>();
        var nuevo = f.Storage.Guardados.Should().ContainSingle().Subject;
        f.Log.Should().NotContain($"delete:{RutaPrevia}", "sin confirmación no se toca el anterior");
        f.Storage.Files.Should().ContainKey(RutaPrevia, "el anterior sigue descargable");
        f.Consolidados().Should().ContainSingle().Which.Should().BeSameAs(f.Previo);
        f.Log.IndexOf("save_changes:fail").Should().BeLessThan(f.Log.IndexOf($"delete:{nuevo}"),
            "el binario nuevo, ya huérfano, se compensa después del fallo");
        f.Storage.Files.Should().NotContainKey(nuevo);
        f.Bandera.Should().BeFalse();
    }

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    public async Task AC2_FallaLaComposicionDelPdf_NoSeTocaNada(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: true);
        f.Merger.FallarComposicion = true;

        var r = await f.EjecutarAsync();

        r.Excepcion.Should().NotBeNull();
        f.Log.Should().NotContain(e => e.StartsWith("delete:", StringComparison.Ordinal));
        f.Log.Should().NotContain(e => e.StartsWith("remove_row:", StringComparison.Ordinal));
        f.Storage.Files.Should().ContainKey(RutaPrevia);
        f.Consolidados().Should().ContainSingle().Which.Should().BeSameAs(f.Previo);
        f.Bandera.Should().BeFalse();
    }

    // ───────────────────────── AC3 — fallo del proveedor al subir ─────────────────────────

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC3_FallaLaSubida_SeAbortaSinHuerfanoNiReferenciaRota(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: true);
        f.Storage.FallarSubida = true;

        var r = await f.EjecutarAsync();

        if (kind == Handler.Wizard)
            r.Error.Should().Be("storage_unavailable");
        else
            r.Excepcion.Should().BeOfType<IOException>();
        f.Log.Should().Contain("save:fail");
        f.Log.Should().NotContain(e => e.StartsWith("delete:", StringComparison.Ordinal));
        f.Log.Should().NotContain(e => e.StartsWith("remove_row:", StringComparison.Ordinal));
        f.Log.Should().NotContain("save_changes");
        f.Storage.Guardados.Should().BeEmpty("no quedó objeto nuevo");
        f.Consolidados().Should().ContainSingle().Which.StoragePath.Should().Be(RutaPrevia);
        f.Storage.Files.Should().ContainKey(RutaPrevia);
        f.Bandera.Should().BeFalse();
    }

    // ───────────────────────── AC4 — primera generación fallida ─────────────────────────

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC4_PrimeraGeneracionFallaAlGuardar_QuedaSinConsolidadoNiAdjuntoCorrupto(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: false);
        f.FallarGuardado();

        var r = await f.EjecutarAsync();

        r.Excepcion.Should().NotBeNull();
        f.Consolidados().Should().BeEmpty("estado expuesto: inexistente");
        var nuevo = f.Storage.Guardados.Should().ContainSingle().Subject;
        f.Storage.Files.Should().NotContainKey(nuevo, "el binario huérfano se compensa");
        f.Bandera.Should().BeFalse();
    }

    [Theory]
    [InlineData(Handler.Wizard)]
    [InlineData(Handler.Maestro)]
    [InlineData(Handler.Admin)]
    public async Task AC4_PrimeraGeneracionFallaAlSubir_QuedaSinConsolidado(Handler kind)
    {
        var f = new Fixture(kind, conPrevio: false);
        f.Storage.FallarSubida = true;

        var r = await f.EjecutarAsync();

        (r.Error is not null || r.Excepcion is not null).Should().BeTrue();
        f.Consolidados().Should().BeEmpty();
        f.Log.Should().NotContain("save_changes");
        f.Log.Should().NotContain(e => e.StartsWith("delete:", StringComparison.Ordinal));
        f.Bandera.Should().BeFalse();
    }
}
