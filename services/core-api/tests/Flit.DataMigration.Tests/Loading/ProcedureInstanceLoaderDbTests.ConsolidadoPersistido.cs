using System.Net;
using System.Text;
using System.Text.Json;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.DataMigration.Tests.Loading;

/// <summary>
/// HU #13163 — el consolidado que V1 tiene GUARDADO llega a V2 aunque la copia de la base de V1 no
/// lo tenga.
/// <para>
/// Es el caso real de PDN del 2026-09-29 (traspasos 35525 y 35526): la copia se tomó antes de que
/// V1 guardara el consolidado. La instancia 2 leyó la copia, lo vio vacío y no copió nada; V1 en vivo,
/// con <c>consolidated=auto</c>, tampoco lo armó porque ya tenía uno. El trámite se quedó sin
/// consolidado por las dos vías. Estas pruebas corren la instancia 3 contra Postgres de verdad, con
/// V1 y los dos file-managers simulados por HTTP.
/// </para>
/// </summary>
public sealed partial class ProcedureInstanceLoaderDbTests
{
    private const string PreparedFileId = "c1dc5252-a871-4fe9-8a5f-18e05baf3dbf";
    private static readonly byte[] ConsolidadoV1 = Encoding.UTF8.GetBytes("%PDF-1.7 consolidado original de V1");

    [Fact]
    public async Task El_consolidado_guardado_en_V1_llega_aunque_la_copia_no_lo_tenga()
    {
        var (resultado, http, filas) = await CorrerDocumentosAsync(
            v1Id: 900_101, preparedFileId: PreparedFileId, dryRun: false, veces: 1);

        resultado.Status.Should().Be(SnapshotLoadStatus.Materialized, resultado.Reason);
        resultado.Failed.Should().Be(0);
        resultado.Warnings.Should().Contain(w => w.Contains("consolidado guardado en V1 traído") && w.Contains(PreparedFileId));

        var consolidado = filas.Adjuntos.Should().ContainSingle(a => a.Tipo == "consolidado").Subject;
        consolidado.Source.Should().Be("migration", "es el archivo que V1 tenía guardado, no uno generado ahora");
        consolidado.Sha256.Should().Be(FileManagerClient.Sha256Hex(ConsolidadoV1));
        consolidado.StoragePath.Should().Be("v2-subido-1");

        filas.Libreta.Should().ContainSingle(l => l.Columna == "id_attachment_pdf_prepared" && l.Origen == PreparedFileId,
            "se anota en la MISMA columna que usaría la instancia 2, para que ninguna de las dos lo duplique");
        http.SubidasDe("35526.pdf").Should().Be(1);
    }

    [Fact]
    public async Task Correr_la_instancia_dos_veces_no_duplica_el_consolidado()
    {
        var (resultado, http, filas) = await CorrerDocumentosAsync(
            v1Id: 900_102, preparedFileId: PreparedFileId, dryRun: false, veces: 2);

        resultado.Status.Should().Be(SnapshotLoadStatus.Materialized, resultado.Reason);
        filas.Adjuntos.Count(a => a.Tipo == "consolidado").Should().Be(1);
        http.SubidasDe("35526.pdf").Should().Be(1, "la segunda corrida lo encuentra en la libreta y no vuelve a subirlo");
        resultado.Warnings.Should().NotContain(w => w.Contains("consolidado guardado en V1"));
    }

    [Fact]
    public async Task Sin_consolidado_guardado_en_V1_no_se_toca_el_file_manager_de_origen()
    {
        var (resultado, http, filas) = await CorrerDocumentosAsync(
            v1Id: 900_103, preparedFileId: null, dryRun: false, veces: 1);

        resultado.Status.Should().Be(SnapshotLoadStatus.Materialized, resultado.Reason);
        filas.Adjuntos.Should().NotContain(a => a.Tipo == "consolidado");
        http.LlamadasOrigen.Should().Be(0);
        resultado.Warnings.Should().NotContain(w => w.Contains("id_attachment_pdf_prepared"));
    }

    [Fact]
    public async Task En_simulacion_avisa_que_lo_traeria_sin_escribir_ni_subir()
    {
        var (resultado, http, filas) = await CorrerDocumentosAsync(
            v1Id: 900_104, preparedFileId: PreparedFileId, dryRun: true, veces: 1);

        resultado.Status.Should().Be(SnapshotLoadStatus.Simulated, resultado.Reason);
        resultado.Warnings.Should().Contain(w => w.Contains("se traería") && w.Contains(PreparedFileId));
        filas.Adjuntos.Should().BeEmpty();
        filas.Libreta.Should().BeEmpty();
        http.SubidasDe("35526.pdf").Should().Be(0);
        http.Subidas.Should().Be(0, "en simulación no se sube nada, ni siquiera las piezas generadas");
    }

    [Fact]
    public async Task Si_el_file_manager_de_V1_no_conoce_el_id_se_reporta_y_el_resto_sigue()
    {
        var (resultado, _, filas) = await CorrerDocumentosAsync(
            v1Id: 900_105, preparedFileId: "id-que-no-existe", dryRun: false, veces: 1);

        resultado.Status.Should().Be(SnapshotLoadStatus.Materialized, resultado.Reason);
        resultado.Failed.Should().Be(1);
        resultado.Warnings.Should().Contain(w => w.Contains("no conoce ese id") && w.Contains("id-que-no-existe"));
        resultado.Materialized.Should().Be(1, "la portada que V1 generó se guarda igual");
        filas.Adjuntos.Should().NotContain(a => a.Tipo == "consolidado");
    }

    // ------------------------------------------------------------------ andamiaje

    private sealed record FilaAdjunto(string Tipo, string Source, string Sha256, string StoragePath);

    private sealed record FilaLibreta(string Columna, string Origen);

    private sealed record Filas(IReadOnlyList<FilaAdjunto> Adjuntos, IReadOnlyList<FilaLibreta> Libreta);

    /// <summary>
    /// Migra la data plana de un traspaso entregado cuya COPIA no tiene consolidado y corre la
    /// instancia 3 <paramref name="veces"/> veces contra un V1 que sí declara uno guardado.
    /// </summary>
    private static async Task<(SnapshotLoadResult Resultado, HttpFalso Http, Filas Filas)> CorrerDocumentosAsync(
        long v1Id, string? preparedFileId, bool dryRun, int veces)
    {
        var conexion = ConexionV2();
        if (conexion is null)
        {
            Assert.Skip("Sin ConnectionStrings__Core: no hay base contra la que probar.");
        }

        var ct = TestContext.Current.CancellationToken;
        await using var db = Abrir(conexion);
        var escenario = await Escenario.PrepararAsync(db, ct);

        try
        {
            var mapeado = escenario.Mapear(v1Id, TramiteEstado.Entregado);
            var datos = await new ProcedureInstanceLoader(db, escenario.Libreta, "test-consolidado")
                .LoadAsync(mapeado, dryRun: false, force: false, ct);
            datos.Status.Should().Be(LoadStatus.Migrated, datos.Reason);

            var attachmentMap = new AttachmentMapStore(db);
            await attachmentMap.EnsureCreatedAsync(ct);

            var http = new HttpFalso(v1Id, preparedFileId);
            var origen = new FileManagerClient(http.Cliente("http://origen.test/"), "api/v1/files", null);
            var destino = new FileManagerClient(http.Cliente("http://destino.test/"), "api/v1/files", null);
            var snapshot = new V1SnapshotClient(
                http.Cliente("http://v1.test/"), new V1SnapshotEndpoint { BaseUrl = "http://v1.test/" });

            var loader = new SnapshotLoader(
                V1ProcedureKind.Transfer, db, escenario.Libreta, attachmentMap, snapshot, destino,
                escenario.SystemUserId, "test-consolidado",
                new AttachmentCopier(
                    db, attachmentMap, origen, destino, CopyMode.Copy, escenario.SystemUserId, "test-consolidado"));

            // La COPIA de V1: el traspaso entregado, sin id_attachment_pdf_prepared.
            var registro = new V1SourceRecord
            {
                Id = v1Id,
                SourceTable = "vehicle_transfer_master",
                ProcessStatus = 5,
                Columns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                StatusHistory = [],
            };

            SnapshotLoadResult resultado = null!;
            for (var i = 0; i < veces; i++)
            {
                resultado = await loader.LoadAsync(registro, dryRun, force: false, ct);
            }

            var adjuntos = await db.ProcedureInstanceAttachments.AsNoTracking()
                .Where(a => a.ProcedureInstanceId == mapeado.Instance.Id)
                .Select(a => new FilaAdjunto(a.Tipo, a.Source, a.Sha256, a.StoragePath))
                .ToListAsync(ct);
            var libreta = await db.Database
                .SqlQueryRaw<FilaLibreta>(
                    "SELECT v1_column AS \"Columna\", source_file_id AS \"Origen\" FROM migration.migration_attachment_map "
                    + "WHERE v1_table = 'vehicle_transfer_master' AND v1_id = {0} AND v1_column NOT LIKE 'snapshot:%'",
                    v1Id)
                .ToListAsync(ct);

            return (resultado, http, new Filas(adjuntos, libreta));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM migration.migration_attachment_map WHERE tenant_id = {0}", [escenario.TenantId], ct);
            await escenario.LimpiarAsync(db, ct);
        }
    }

    /// <summary>
    /// V1 y los dos file-managers en un solo handler, separados por host. Cuenta lo que importa para
    /// las aserciones: cuántas veces se tocó el origen y cuántas se subió al destino.
    /// </summary>
    private sealed class HttpFalso(long v1Id, string? preparedFileId) : HttpMessageHandler
    {
        public int LlamadasOrigen { get; private set; }

        public int Subidas { get; private set; }

        private int creados;

        private readonly List<string> creadosPorNombre = [];

        /// <summary>Cuántos archivos con ese nombre se crearon en el file-manager de destino.</summary>
        public int SubidasDe(string filename) => creadosPorNombre.Count(n => n == filename);

        public HttpClient Cliente(string baseUrl) => new(this, disposeHandler: false) { BaseAddress = new Uri(baseUrl) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var ruta = uri.AbsolutePath;

            if (uri.Host == "v1.test" && ruta.EndsWith($"/{v1Id}/snapshot", StringComparison.Ordinal))
            {
                return Json(Snapshot());
            }

            if (uri.Host == "origen.test")
            {
                LlamadasOrigen++;
                if (ruta == $"/api/v1/files/{PreparedFileId}/presigned-url")
                {
                    return Json(new
                    {
                        filename = "35526.pdf",
                        metadata = new { sha256 = FileManagerClient.Sha256Hex(ConsolidadoV1) },
                        presignedUrl = new { url = "http://origen.test/descarga/consolidado" },
                    });
                }

                if (ruta == "/descarga/consolidado")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ConsolidadoV1) };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (uri.Host == "destino.test" && request.Method == HttpMethod.Post && ruta == "/api/v1/files")
            {
                creados++;
                using var cuerpo = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                creadosPorNombre.Add(cuerpo.RootElement.GetProperty("filename").GetString() ?? string.Empty);
                return Json(new
                {
                    id = $"v2-subido-{creados}",
                    presignedUrl = new { url = $"http://destino.test/put/{creados}", method = "PUT" },
                });
            }

            if (uri.Host == "destino.test" && request.Method == HttpMethod.Put)
            {
                Subidas++;
                await Task.CompletedTask;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        /// <summary>Un snapshot mínimo: una portada generada y el consolidado que V1 tiene guardado.</summary>
        private object Snapshot()
        {
            var portada = Encoding.UTF8.GetBytes("%PDF-1.7 portada");
            return new
            {
                transferId = v1Id,
                plate = "TST001",
                statusId = 5,
                statusName = "Delivered",
                generatedAt = DateTimeOffset.UtcNow,
                persistedConsolidated = new { preparedFileId, draftFileId = (string?)null },
                pieces = new[]
                {
                    new
                    {
                        key = "coverPage",
                        documentTypeCode = "portada",
                        name = "Portada",
                        origin = "generated",
                        sourceFileId = (string?)null,
                        filename = "portada.pdf",
                        mimeType = "application/pdf",
                        sizeBytes = (long)portada.Length,
                        sha256 = FileManagerClient.Sha256Hex(portada),
                        contentBase64 = Convert.ToBase64String(portada),
                    },
                },
                issues = Array.Empty<object>(),
            };
        }

        private static HttpResponseMessage Json(object cuerpo) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json"),
        };
    }
}
