using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4/D7/D8) — consulta, descarga y purga del lote contra PostgreSQL real con los CHECK
/// de los DDL 133/134:
/// <list type="bullet">
///   <item>AC5 purga: <see cref="PurgarLotesExpiradosHandler"/> sobre <see cref="ConsolidadoLoteLectura"/> +
///   <see cref="ConsolidadoLoteRepository"/>: el vencido (incluido el <c>fallido</c>) queda <c>expirado</c> con
///   <c>purged_at</c>, DEK NULL, partes <c>purgada</c> y una fila <c>lote_purgado</c>; el vigente no se toca; una descarga
///   posterior es <see cref="DescargarParteEstado.Expirada"/> (410).</item>
///   <item>AC1 <c>actual</c>: el activo gana; sin activo, el último terminal retenido (aunque haya vencido); tras la purga,
///   nada (204).</item>
///   <item>AC3 IDOR: la lectura filtra por <c>requested_by_user_id</c>: un compañero de la misma compañía no ve el lote ni
///   su «actual»; la fila <c>parte_descargada</c> cumple los CHECK (también en un lote de Super Admin sin compañía).</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var ctx = NewContext();
/// var r = await new PurgarLotesExpiradosHandler(new ConsolidadoLoteLectura(ctx), new ConsolidadoLoteRepository(ctx)).HandleAsync(ct);
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteConsultaPurgaIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b3000000-0000-7000-8000-000000013379");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Siembra ─────────────────────────────────────────────────────────────────────────────────────────

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private async Task SembrarCompaniaAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13379", false, null));
        await ctx.SaveChangesAsync(Ct);
    }

    private async Task<Guid> UsuarioAsync(string sufijo)
    {
        var u = Guid.CreateVersion7();
        await ExecAsync(
            "INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES (@u, @e, 'Usuario lote', 'active', now())",
            ("u", u), ("e", $"lote13379-{sufijo}@it.test"));
        return u;
    }

    /// <summary>
    /// Lote del usuario. Terminal ⇒ <c>finished_at</c> y <c>expires_at</c> = now + <paramref name="horasParaExpirar"/>.
    /// <paramref name="partesCerradas"/> partes <c>cerrada</c> con binario. Super Admin ⇒ sin compañía (Q8).
    /// </summary>
    private async Task<Guid> LoteAsync(
        Guid usuario, string estado, double horasParaExpirar = 23, short partesCerradas = 0, int minutosAtras = 0,
        bool superAdmin = false)
    {
        var lote = Guid.CreateVersion7();
        var terminal = !ConsolidadoExportStatus.EsActivo(estado);
        var conDek = estado != ConsolidadoExportStatus.Fallido;
        await ExecAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, parts_count, dek_wrapped, effects_acknowledged_at, created_by, created_at, started_at,
               finished_at, expires_at)
            VALUES (@l, {(superAdmin ? "NULL" : "@c")}, @u, @rol, @origen, 'consolidado', 'ids', @s, 0, @pc,
                    {(conDek ? "'\\x010203'::bytea" : "NULL")}, now(), @u, now() - make_interval(mins => @m), now(),
                    {(terminal ? "now(), now() + make_interval(secs => @exp)" : "NULL, NULL")});
            """,
            ("l", lote), ("c", Company), ("u", usuario), ("rol", superAdmin ? "SuperAdmin" : "Radicador"),
            ("origen", superAdmin ? ConsolidadoExportOrigin.Superadmin : ConsolidadoExportOrigin.Tramites),
            ("s", estado), ("pc", partesCerradas), ("m", minutosAtras), ("exp", horasParaExpirar * 3600));

        for (short n = 1; n <= partesCerradas; n++)
        {
            await ExecAsync(
                """
                INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, status, pdf_count, omitted_count,
                    storage_path, stored_sha256, stored_size_bytes, plain_size_bytes, closed_at)
                VALUES (@l, @n, 'cerrada', 0, 0, 'fm/parte-' || @n, repeat('b', 64), 110, 100, now())
                """,
                ("l", lote), ("n", n));
        }

        return lote;
    }

    private Task<long> AuditoriasAsync(Guid lote, string evento) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = @e", ("l", lote), ("e", evento));

    // ── AC5 — purga a las 24 h ──────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_PurgaElVencidoYElFallidoVencido_DekNull_PartesPurgadas_LotePurgado_YNoTocaElVigente()
    {
        await SembrarCompaniaAsync();
        var u = await UsuarioAsync("purga");
        var otro = await UsuarioAsync("purga-otro");
        var vencido = await LoteAsync(u, ConsolidadoExportStatus.Completado, horasParaExpirar: -0.1, partesCerradas: 2);
        var fallido = await LoteAsync(otro, ConsolidadoExportStatus.Fallido, horasParaExpirar: -1);
        var vigente = await LoteAsync(await UsuarioAsync("vigente"), ConsolidadoExportStatus.Completado, partesCerradas: 1);

        PurgaLotesResultado r;
        await using (var ctx = NewContext())
            r = await new PurgarLotesExpiradosHandler(new ConsolidadoLoteLectura(ctx), new ConsolidadoLoteRepository(ctx)).HandleAsync(Ct);

        r.Should().Be(new PurgaLotesResultado(2, 2, 0));
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", vencido)))
            .Should().Be(ConsolidadoExportStatus.Expirado);
        (await ScalarAsync<bool>(
                "SELECT dek_wrapped IS NULL AND purged_at IS NOT NULL FROM tramites.consolidado_export_batches WHERE id = @l",
                ("l", vencido)))
            .Should().BeTrue();
        (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND status = 'purgada' AND purged_at IS NOT NULL",
                ("l", vencido)))
            .Should().Be(2);
        (await AuditoriasAsync(vencido, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(1);
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", fallido)))
            .Should().Be(ConsolidadoExportStatus.Expirado, "también se purgan los fallido, que tienen expires_at");
        (await AuditoriasAsync(fallido, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(1);
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", vigente)))
            .Should().Be(ConsolidadoExportStatus.Completado);
        (await AuditoriasAsync(vigente, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(0);

        // Idempotente: un segundo ciclo no encuentra nada.
        await using (var ctx = NewContext())
            (await new PurgarLotesExpiradosHandler(new ConsolidadoLoteLectura(ctx), new ConsolidadoLoteRepository(ctx)).HandleAsync(Ct))
                .Should().Be(new PurgaLotesResultado(0, 0, 0));

        // Una descarga posterior recibe 410 sin tocar el almacenamiento.
        var storage = Substitute.For<IConsolidadoLoteParteStorage>();
        await using (var ctx = NewContext())
        {
            var descarga = await new DescargarParteHandler(new ConsolidadoLoteLectura(ctx), storage, Substitute.For<IConsolidadoLoteCipher>())
                .PrepararAsync(new DescargarParteQuery(vencido, 1, u, "Radicador"), Ct);
            descarga.Estado.Should().Be(DescargarParteEstado.Expirada);
        }

        await storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
    }

    // ── AC1 — actual con un lote retenido ───────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Actual_ElActivoGana_SinActivoElUltimoRetenido_InclusoVencido_YTrasLaPurgaNada()
    {
        await SembrarCompaniaAsync();
        var u = await UsuarioAsync("actual");
        var retenido = await LoteAsync(u, ConsolidadoExportStatus.CompletadoConOmitidos, horasParaExpirar: -0.05, partesCerradas: 1, minutosAtras: 30);

        await using (var ctx = NewContext())
        {
            var lectura = new ConsolidadoLoteLectura(ctx);
            (await lectura.ObtenerActualDelDuenoAsync(u, Ct))!.Id.Should().Be(retenido, "sin activo, el último retenido aunque haya vencido");
            var consultado = await new ConsultarLoteConsolidadosHandler(lectura).ActualAsync(new ObtenerLoteActualQuery(u), Ct);
            consultado!.Lote.ExpiresAt.Should().NotBeNull();
            consultado.Partes.Should().BeEmpty("vencido: ya no ofrece partes");
        }

        var activo = await LoteAsync(u, ConsolidadoExportStatus.EnProceso, minutosAtras: 60);
        await using (var ctx = NewContext())
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct))!.Id.Should().Be(activo, "el activo gana aunque sea más antiguo");

        await ExecAsync(
            "UPDATE tramites.consolidado_export_batches SET status = 'cancelado', finished_at = now(), expires_at = now() + interval '1 hour' WHERE id = @l",
            ("l", activo));
        await using (var ctx = NewContext())
        {
            (await new ConsolidadoLoteRepository(ctx).PurgarAsync(retenido, DateTimeOffset.UtcNow, Ct)).Should().BeTrue();
            (await new ConsolidadoLoteRepository(ctx).PurgarAsync(activo, DateTimeOffset.UtcNow, Ct)).Should().BeTrue();
        }

        await using (var ctx = NewContext())
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct)).Should().BeNull("purgado ⇒ 204");
    }

    [PostgresFact]
    public async Task AC1_Actual_RetenidoVigente_ListaSusPartesCerradasConNombreYTamano()
    {
        await SembrarCompaniaAsync();
        var u = await UsuarioAsync("partes");
        var lote = await LoteAsync(u, ConsolidadoExportStatus.Completado, partesCerradas: 2);

        await using var ctx = NewContext();
        var r = await new ConsultarLoteConsolidadosHandler(new ConsolidadoLoteLectura(ctx)).ActualAsync(new ObtenerLoteActualQuery(u), Ct);

        r!.Lote.Id.Should().Be(lote);
        r.Partes.Select(p => (p.Numero, p.Bytes)).Should().Equal((1, 100L), (2, 100L));
        r.Partes[1].NombreArchivo.Should().EndWith("_parte-02-de-02.zip");
    }

    // ── AC3 — IDOR por sub ──────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_IdorPorSub_UnCompaneroDeLaMismaCompania_NoVeElLoteNiSuActual_YLaDescargaEs404()
    {
        await SembrarCompaniaAsync();
        var dueno = await UsuarioAsync("dueno");
        var companero = await UsuarioAsync("companero");
        var lote = await LoteAsync(dueno, ConsolidadoExportStatus.Completado, partesCerradas: 1);
        var storage = Substitute.For<IConsolidadoLoteParteStorage>();

        await using var ctx = NewContext();
        var lectura = new ConsolidadoLoteLectura(ctx);

        (await lectura.ObtenerDelDuenoAsync(lote, dueno, Ct)).Should().NotBeNull();
        (await lectura.ObtenerDelDuenoAsync(lote, companero, Ct)).Should().BeNull();
        (await lectura.ObtenerActualDelDuenoAsync(companero, Ct)).Should().BeNull();
        var descarga = await new DescargarParteHandler(lectura, storage, Substitute.For<IConsolidadoLoteCipher>())
            .PrepararAsync(new DescargarParteQuery(lote, 1, companero, "Radicador"), Ct);
        descarga.Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        await storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_ParteDescargada_SeRegistraConLosCheck_EnLoteDeCompaniaYDeSuperAdmin()
    {
        await SembrarCompaniaAsync();
        var gestor = await UsuarioAsync("gestor");
        var superAdmin = await UsuarioAsync("superadmin");
        var deCompania = await LoteAsync(gestor, ConsolidadoExportStatus.Completado, partesCerradas: 1);
        var deSuperAdmin = await LoteAsync(superAdmin, ConsolidadoExportStatus.Completado, partesCerradas: 1, superAdmin: true);

        await using (var ctx = NewContext())
        {
            var lectura = new ConsolidadoLoteLectura(ctx);
            foreach (var (lote, usuario, rol) in new[] { (deCompania, gestor, "Radicador"), (deSuperAdmin, superAdmin, "SuperAdmin") })
            {
                var batch = (await lectura.ObtenerDelDuenoAsync(lote, usuario, Ct))!;
                (await lectura.RegistrarDescargaAsync(
                        new ParteDescargadaRegistro(batch, 1, rol, DateTimeOffset.UtcNow, System.Net.IPAddress.Loopback, "it/13379"), Ct))
                    .Should().BeTrue();
            }
        }

        (await AuditoriasAsync(deCompania, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(1);
        (await AuditoriasAsync(deSuperAdmin, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(1);
        (await ScalarAsync<bool>(
                "SELECT actor_tenant_id IS NULL AND part_number = 1 FROM tramites.consolidado_export_audit WHERE batch_id = @l",
                ("l", deSuperAdmin)))
            .Should().BeTrue();
    }
}
