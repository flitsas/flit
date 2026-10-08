using System.IO.Compression;
using System.Text;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13384 AC8 (Épica #13216, ADR-0070 D2/D4/D8, CF-05/CF-13) — el lote del Super Admin de punta a punta contra
/// PostgreSQL real, reutilizando la siembra y las consultas de esta clase (#13378):
/// <list type="number">
///   <item>Carril de ítems real: <see cref="ConsolidadoLoteRepository.ReclamarSiguienteItemAsync"/> +
///   <see cref="ProcesarItemLoteHandler"/> con <see cref="SuperAdminLoteItemOrigen"/> + el cierre condicionado de
///   <see cref="ConsolidadoLoteItemProceso"/> (asignación parcial de partes de #13377).</item>
///   <item>Cierre del carril (<see cref="ConsolidadoLoteRepository.CerrarCarrilAsync"/>, #13377).</item>
///   <item>Empaquetado y cifrado real (<see cref="EmpaquetarParteHandler"/> + <see cref="ConsolidadoLoteCipher"/>,
///   #13378) hasta el estado terminal con <c>lote_finalizado</c>.</item>
///   <item>Descarga (<see cref="DescargarParteHandler"/>, #13379): solo el dueño; otro Super Admin u otro usuario, 404.</item>
/// </list>
/// Solo son dobles el entregador (devuelve el maestro guardado de cada trámite), la revalidación de acceso (la real la
/// cubre <c>AccessCheckerSuperAdminTests</c>) y los almacenamientos (en memoria).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await repo.ReclamarSiguienteItemAsync("it-13384", 600, ct);
/// await procesar.HandleAsync(ProcesarItemLoteCommand.Con(r.Lote, r.Item, settings), ct);
/// </code>
/// </remarks>
public sealed partial class ConsolidadoLoteEmpaquetadoIntegrationTests
{
    private static readonly Guid CompaniaA = new("a1338400-0000-7000-8000-00000013384a");
    private static readonly Guid CompaniaB = new("b1338400-0000-7000-8000-00000013384b");

    /// <summary>
    /// Dado max_pdfs_per_part = 3 y un lote del Super Admin con 4 trámites de dos compañías (A, B, A, B) más uno omitido,
    /// cuando el lote termina, produce exactamente 2 partes (3 + 1), sin carpetas por compañía, con los nombres y el
    /// omitidos.csv de #13306; y un usuario distinto del Super Admin que lo pidió recibe 404 al pedir sus partes.
    /// </summary>
    [PostgresFact]
    public async Task HU13384_AC8_LoteSuperAdminDeDosCompanias_DosPartesPlanasConNombresYOmitidos_YSoloElDuenoDescarga()
    {
        // ── Siembra ──────────────────────────────────────────────────────────────────────────
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(CompaniaA, "IT-CIA-13384-A", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompaniaB, "IT-CIA-13384-B", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active, max_pdfs_per_part, retention_hours)
            VALUES (uuidv7(), true, 3, @r);
            """,
            ("r", RetencionHoras));

        var dueno = Guid.CreateVersion7();
        var otroSuperAdmin = Guid.CreateVersion7();
        var otroUsuario = Guid.CreateVersion7();
        foreach (var (u, s) in new[] { (dueno, "dueno"), (otroSuperAdmin, "otro-sa"), (otroUsuario, "otro") })
        {
            await ExecAsync(
                "INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES (@u, @e, 'Usuario lote', 'active', now())",
                ("u", u), ("e", $"lote13384-{s}@it.test"));
        }

        var dp = new EphemeralDataProtectionProvider();
        var cipher = new ConsolidadoLoteCipher(dp);
        var lote = Guid.CreateVersion7();
        // A, B, A, B incluidos e intercalados; el 5.º (de B) pierde el acceso y se omite.
        var companias = new[] { CompaniaA, CompaniaB, CompaniaA, CompaniaB, CompaniaB };
        var radicados = new[] { "R13384-1", "R13384-2", "R13384-3", "R13384-4", "R13384-5" };
        var placas = new[] { "AAA101", "BBB202", "AAA303", "BBB404", "BBB505" };
        var tramites = companias.Select(_ => Guid.NewGuid()).ToArray();
        await ExecAsync(
            """
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by, created_at)
            VALUES (@l, NULL, @u, 'SuperAdmin', 'superadmin', 'consolidado_maestro', 'ids', 'en_cola', 5, @dek, now(), @u, now());
            """,
            ("l", lote), ("u", dueno), ("dek", cipher.GenerarDekEnvuelta()));
        for (var k = 0; k < companias.Length; k++)
        {
            await ExecAsync(
                """
                INSERT INTO tramites.procedure_instances
                    (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                VALUES (@t, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), @r, 'borrador',
                        'VIN' || upper(substr(md5(@t::text), 1, 14)), @u, now());
                INSERT INTO tramites.consolidado_export_batch_items
                    (id, tenant_id, batch_id, procedure_instance_id, position, reference_number, plate, created_by)
                VALUES (uuidv7(), @c, @l, @t, @pos, @r, @p, @u);
                """,
                ("t", tramites[k]), ("c", companias[k]), ("r", radicados[k]), ("p", placas[k]), ("u", dueno),
                ("l", lote), ("pos", k));
        }

        // ── Dobles: entregador (maestro guardado de cada trámite) y almacenamientos en memoria ──
        var pdfs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var entregas = new List<(Guid Tramite, Guid Compania)>();
        var entregador = Substitute.For<ILoteItemEntregador>();
        entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var req = ci.Arg<LoteItemEntregaRequest>();
            entregas.Add((req.ProcedureInstanceId, req.TenantId));
            var ruta = $"fm/maestro-{req.ProcedureInstanceId:N}";
            var contenido = Encoding.ASCII.GetBytes($"%PDF-1.7 maestro {req.ProcedureInstanceId:N}");
            pdfs[ruta] = contenido;
            return LoteItemEntregaResult.Incluido(
                new LoteItemAdjunto(Guid.CreateVersion7(), ruta, contenido.Length, new string('a', 64), "consolidado_maestro.pdf"),
                LoteDeliveryMode.Existente);
        });
        var acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
        acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<LoteItemContexto>().ProcedureInstanceId != tramites[4]);

        var adjuntos = Substitute.For<IAttachmentStorage>();
        adjuntos.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => pdfs.TryGetValue(ci.Arg<string>(), out var b) ? new MemoryStream(b) : (Stream?)null);
        var cifradas = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var partesStorage = Substitute.For<IConsolidadoLoteParteStorage>();
        partesStorage.SubirAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var bytes = await File.ReadAllBytesAsync(ci.ArgAt<string>(2), Ct);
                var ruta = $"fm/parte-{ci.ArgAt<Guid>(0):N}-{ci.ArgAt<int>(1)}";
                cifradas[ruta] = bytes;
                return new StoredFile(ruta, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length);
            });
        partesStorage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => cifradas.TryGetValue(ci.Arg<string>(), out var b) ? new MemoryStream(b) : (Stream?)null);
        var adjuntoActual = Substitute.For<IConsolidadoLoteAdjuntoActual>();
        var dirTemporal = Path.Combine(Path.GetTempPath(), "it-13384-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirTemporal);

        try
        {
            // ── 1. Carril real de ítems (reclamo + proceso + cierre de cada ítem) ───────────────
            var procesados = 0;
            while (true)
            {
                await using var ctx = NewContext();
                var repo = new ConsolidadoLoteRepository(ctx);
                var reclamado = await repo.ReclamarSiguienteItemAsync("it-13384", 600, Ct);
                if (reclamado is null)
                    break;
                var settings = (await repo.ObtenerSettingsAsync(Ct))!;
                var handler = new ProcesarItemLoteHandler(
                    new LoteItemOrigenPorOrigen([new SuperAdminLoteItemOrigen(acceso, entregador)]),
                    new ConsolidadoLoteItemProceso(ctx),
                    NullLogger<ProcesarItemLoteHandler>.Instance,
                    repo);
                await handler.HandleAsync(ProcesarItemLoteCommand.Con(reclamado.Lote, reclamado.Item, settings), Ct);
                procesados++;
            }

            procesados.Should().Be(5);
            entregas.Should().BeEquivalentTo(
                Enumerable.Range(0, 4).Select(k => (tramites[k], companias[k])),
                "cada ítem se entrega con la compañía de su trámite; el omitido no llega al entregador");

            // ── 2. Cierre del carril (#13377) ──────────────────────────────────────────────────
            await using (var ctx = NewContext())
                (await new ConsolidadoLoteRepository(ctx).CerrarCarrilAsync(lote, Ct)).Should().Be(new CierreCarrilResultado(true, 1, 2));

            // ── 3. Empaquetado y cifrado de cada parte (#13378) hasta el terminal ──────────────
            var desenlaces = new List<EmpaquetarParteDesenlace>();
            while (true)
            {
                await using var ctx = NewContext();
                var empaquetado = new ConsolidadoLoteEmpaquetado(ctx);
                var reclamada = await empaquetado.ReclamarSiguienteParteAsync(900, Ct);
                if (reclamada is null)
                    break;
                var handler = new EmpaquetarParteHandler(
                    empaquetado, cipher, partesStorage, adjuntos, adjuntoActual, new ConsolidadoLoteTemporales(dirTemporal),
                    NullLogger<EmpaquetarParteHandler>.Instance, new ConsolidadoLoteRepository(ctx));
                desenlaces.Add(await handler.HandleAsync(reclamada, 3, Ct));
            }

            desenlaces.Should().Equal(EmpaquetarParteDesenlace.Cerrada, EmpaquetarParteDesenlace.Cerrada);
            (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.CompletadoConOmitidos);
            (await PartesDelLoteAsync(lote)).Should().Be(2, "exactamente 2 partes: 3 PDF + 1 PDF");
            (await PartesAuditadasAsync(lote)).Should().Be(2, "lote_finalizado lleva parts_count = 2");
            (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteFinalizado)).Should().Be(1);
            (await ScalarAsync<string>(
                "SELECT string_agg(part_number || ':' || status || ':' || pdf_count || ':' || omitted_count, ',' ORDER BY part_number) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l",
                ("l", lote))).Should().Be("1:cerrada:3:0,2:cerrada:1:1");

            // ── 4. Descarga (#13379): otro Super Admin y otro usuario ⇒ 404; el dueño ⇒ 200 ─────
            DescargarParteHandler Descargas(Flit.Infrastructure.Persistence.FlitDbContext c) => new(new ConsolidadoLoteLectura(c), partesStorage, cipher);
            for (var n = 1; n <= 2; n++)
            {
                await using var c = NewContext();
                (await Descargas(c).PrepararAsync(new DescargarParteQuery(lote, n, otroSuperAdmin, "SuperAdmin"), Ct))
                    .Estado.Should().Be(DescargarParteEstado.NoEncontrada, "otro Super Admin no ve el lote");
                (await Descargas(c).PrepararAsync(new DescargarParteQuery(lote, n, otroUsuario, "Radicador"), Ct))
                    .Estado.Should().Be(DescargarParteEstado.NoEncontrada, "otro usuario no ve el lote");
            }

            (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(0, "un 404 no se audita");

            var nombresPdf = Enumerable.Range(0, 4).Select(k => ConsolidadoLoteNombres.Pdf(radicados[k], placas[k])).ToArray();
            var esperado = new Dictionary<int, (string[] Pdfs, string Csv)>
            {
                [1] = ([nombresPdf[0], nombresPdf[1], nombresPdf[2]], "radicado;placa;motivo\r\n"),
                [2] = ([nombresPdf[3]], "radicado;placa;motivo\r\nR13384-5;BBB505;Acceso revocado\r\n"),
            };
            var prohibidos = new[]
            {
                CompaniaA.ToString(), CompaniaB.ToString(), CompaniaA.ToString("N"), CompaniaB.ToString("N"),
                "IT-CIA-13384-A", "IT-CIA-13384-B",
            };

            DateTimeOffset creado;
            await using (var ctx = NewContext())
                creado = (await new ConsolidadoLoteLectura(ctx).ObtenerDelDuenoAsync(lote, dueno, Ct))!.CreatedAt;

            for (var n = 1; n <= 2; n++)
            {
                await using var c = NewContext();
                var handler = Descargas(c);
                var r = await handler.PrepararAsync(
                    new DescargarParteQuery(lote, n, dueno, "SuperAdmin", System.Net.IPAddress.Loopback, "it/13384"), Ct);
                r.Estado.Should().Be(DescargarParteEstado.Lista, "el dueño descarga (200)");
                using var claro = new MemoryStream();
                await using (r.Descarga!)
                {
                    r.Descarga!.NombreArchivo.Should().Be(ConsolidadoLoteNombres.Zip(creado, n, 2));
                    r.Descarga.NombreArchivo.Should().EndWith($"_parte-0{n}-de-02.zip");
                    await handler.EscribirAsync(r.Descarga, claro, Ct);
                }

                claro.Position = 0;
                using var zip = new ZipArchive(claro, ZipArchiveMode.Read);
                var entradas = zip.Entries.Select(e => e.FullName).ToList();
                entradas.Should().Equal([.. esperado[n].Pdfs, ConsolidadoLoteNombres.OmitidosCsv],
                    "PDF en el orden de la parte y el omitidos.csv, todo en la raíz");
                entradas.Should().OnlyContain(e => !e.Contains('/') && !e.Contains('\\'), "sin carpetas por compañía");
                foreach (var p in prohibidos)
                    entradas.Should().NotContain(e => e.Contains(p, StringComparison.OrdinalIgnoreCase), "ni nombre ni id de compañía");

                using var csv = new MemoryStream();
                await using (var s = zip.GetEntry(ConsolidadoLoteNombres.OmitidosCsv)!.Open())
                    await s.CopyToAsync(csv, Ct);
                var bytes = csv.ToArray();
                bytes.Take(3).Should().Equal(Encoding.UTF8.Preamble.ToArray(), "UTF-8 con BOM");
                Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Should().Be(esperado[n].Csv);
            }

            (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(2);
            (await ScalarAsync<long>(
                """
                SELECT count(*) FROM tramites.consolidado_export_audit
                 WHERE batch_id = @l AND event = 'parte_descargada' AND actor_tenant_id IS NULL AND actor_user_id = @u
                """,
                ("l", lote), ("u", dueno))).Should().Be(2, "parte_descargada sin compañía y a nombre del dueño");
        }
        finally
        {
            try
            {
                Directory.Delete(dirTemporal, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
