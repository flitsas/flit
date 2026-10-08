using System.Text.Json;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Endpoints;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// HU #13391 (Épica #13216, ADR-0070 adenda v4) — el lote de maestros de la bandeja OT contra PostgreSQL real, con
/// la misma composición que arma <c>POST /api/v1/admin/ot/consolidados/lotes</c>: organismo resuelto con
/// <see cref="OtClientProcedureRepository.ResolveTransitOfficeIdAsync"/>, <see cref="CrearLoteConsolidadosHandler"/>
/// con el resolver real <see cref="OtBandejaSeleccionResolver"/> y <see cref="ConsolidadoLoteRepository"/>. Cubre
/// AC1 (lote <c>ot_bandeja</c> del tenant OT con su organismo e ítems con la compañía cliente), AC6 (ids inyectados de
/// otro organismo fuera) y AC9 (auditoría minimizada en la misma transacción; si falla, no queda lote). Escenario
/// base <see cref="HierarchyScenario"/>: el tenant <c>O</c> es el organismo <c>Ot1</c>.
/// <para>Uso de ejemplo:
/// <code>
/// var r = await Handler(ctx).HandleAsync(new CrearLoteConsolidadosCommand { Origen = "ot_bandeja", TenantId = O,
///     OtTransitOfficeId = Ot1, TipoDocumento = "consolidado_maestro", ... }, ct);
/// </code></para>
/// </summary>
public sealed class CrearLoteConsolidadosOtIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid O = HierarchyScenario.O;
    private static readonly Guid UsuarioOt = Guid.Parse("0e000000-0000-4000-8000-0000000133a1");

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CrearLoteConsolidadosHandler Handler(FlitDbContext ctx) =>
        new(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen(
                [new OtBandejaSeleccionResolver(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()))]),
            new ConsolidadoLoteCipher(_dataProtection));

    /// <summary>Lo que hace el endpoint para el ot_admin: el organismo sale del perfil del tenant del token.</summary>
    private async Task<CrearLoteConsolidadosCommand> ComandoOtAdminAsync(LoteSeleccion seleccion)
    {
        await using var ctx = NewContext();
        var organismo = await new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher())
            .ResolveTransitOfficeIdAsync(O, null, Ct);
        return new CrearLoteConsolidadosCommand
        {
            Origen = ConsolidadoExportOrigin.OtBandeja,
            TenantId = O,
            OtTransitOfficeId = organismo,
            UsuarioId = UsuarioOt,
            RolCodigo = "ot_admin",
            TipoDocumento = ConsolidadoExportDocumentType.ConsolidadoMaestro,
            Seleccion = seleccion,
            ConfirmaEfectos = true,
            ClientIp = System.Net.IPAddress.Parse("10.13.39.1"),
            UserAgent = "pruebas-13391",
        };
    }

    /// <summary>Escenario + usuario del OT (dueño del lote) + parámetros del motor (el reset del arnés los vacía).</summary>
    private async Task SembrarAsync()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User
            {
                Id = UsuarioOt,
                Email = "it-ot-13391@flit.test",
                DisplayName = "Admin OT 13391",
                Status = "active",
                HomeTenantId = O,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active) VALUES (uuidv7(), true);
            """);
    }

    [PostgresFact]
    public async Task AC1_AC9_FiltroDeLaBandeja_CreaLoteOtBandejaDelTenantOt_ConOrganismo_ItemsDeCadaCliente_YAuditoriaMinimizada()
    {
        await SembrarAsync();
        var excluido = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X);
        var body = new OtBandejaSearchRequest
        {
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, [HierarchyScenario.SharedPlate, "ZZZ999"])],
            // El texto libre de la barra (aquí la placa compartida) se audita solo como {presente, longitud}.
            Busqueda = $"  {HierarchyScenario.SharedPlate} ",
            Page = 1,
            PageSize = 25,
        };
        var seleccion = new SeleccionPorFiltro(new OtBandejaLoteFiltro(body.ToFilter()), [excluido]);

        CrearLoteConsolidadosResultado r;
        var comando = await ComandoOtAdminAsync(seleccion);
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(comando, Ct);

        r.Creado.Should().BeTrue(r.Error);
        comando.OtTransitOfficeId.Should().Be(HierarchyScenario.Ot1, "el organismo del ot_admin sale del perfil de su tenant");

        await using var db = NewContext();
        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote!.Id, Ct);
        lote.Origin.Should().Be(ConsolidadoExportOrigin.OtBandeja);
        lote.TenantId.Should().Be(O);
        lote.OtTransitOfficeId.Should().Be(HierarchyScenario.Ot1);
        lote.DocumentType.Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        lote.Status.Should().Be(ConsolidadoExportStatus.EnCola);
        lote.RequestedRoleCode.Should().Be("ot_admin");

        // Los cinco clientes tienen un trámite entregado a Ot1 con la placa compartida; X va excluido.
        var esperados = HierarchyScenario.Clients.Where(c => c != HierarchyScenario.X).ToList();
        lote.TotalItems.Should().Be(esperados.Count);
        var items = await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == lote.Id).ToListAsync(Ct);
        items.Select(i => (i.ProcedureInstanceId, i.TenantId)).Should().BeEquivalentTo(
            esperados.Select(c => (HierarchyScenario.DeliveredProcedureOf(c), c)), "cada ítem con la compañía cliente de su trámite");
        items.Should().NotContain(i => i.TenantId == O);

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == lote.Id, Ct);
        audit.Event.Should().Be(ConsolidadoExportAuditEvent.LoteCreado);
        audit.ActorTenantId.Should().Be(O);
        audit.ActorRoleCode.Should().Be("ot_admin");
        audit.ReachedTenantIds.Should().BeEquivalentTo(esperados);
        audit.ExcludedCount.Should().Be(1);
        audit.FilterSummary.Should().NotContain(HierarchyScenario.SharedPlate, "ni placas ni documentos en la auditoría");
        var resumen = JsonDocument.Parse(audit.FilterSummary!).RootElement;
        resumen.GetProperty("organismo").GetString().Should().Be(HierarchyScenario.Ot1.ToString("D"));
        resumen.GetProperty("origenFiltro").GetString().Should().Be(ConsolidadoExportOrigin.OtBandeja);
        var condicion = resumen.GetProperty("filtro").GetProperty("condiciones")[0];
        condicion.GetProperty("campo").GetString().Should().Be("placa");
        condicion.GetProperty("operador").GetString().Should().Be(QueryOperator.EsAlguno);
        condicion.GetProperty("cantidad").GetInt32().Should().Be(2);
        var busqueda = resumen.GetProperty("filtro").GetProperty("busqueda");
        busqueda.GetProperty("presente").GetBoolean().Should().BeTrue();
        busqueda.GetProperty("longitud").GetInt32().Should().Be(HierarchyScenario.SharedPlate.Length);
    }

    /// <summary>
    /// HU #13390 (L3, Habeas Data) — la fila <c>lote_creado</c> no retiene texto libre metido en campos de catálogo:
    /// el estado válido queda literal, la basura del <c>status</c> y un <c>sortBy</c>/<c>sortDir</c> fuera de la lista
    /// blanca quedan como <c>{presente, longitud}</c>. El lote congela lo mismo que sin la basura.
    /// </summary>
    [PostgresFact]
    public async Task L3_FilterSummaryDeLoteCreado_MinimizaStatusYOrdenFueraDeCatalogo()
    {
        await SembrarAsync();
        const string pii = "1037654321 JUAN PEREZ";
        var body = new OtBandejaSearchRequest
        {
            Status = $"entregado,{pii}",
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, [HierarchyScenario.SharedPlate])],
            SortBy = pii,
            SortDir = "PEREZ",
            Page = 1,
            PageSize = 25,
        };
        var seleccion = new SeleccionPorFiltro(new OtBandejaLoteFiltro(body.ToFilter()), []);

        CrearLoteConsolidadosResultado r;
        var comando = await ComandoOtAdminAsync(seleccion);
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(comando, Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(HierarchyScenario.Clients.Count, "el resumen auditado no cambia qué trámites entran");

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT filter_summary::text FROM tramites.consolidado_export_audit WHERE batch_id = @b AND event = 'lote_creado'", cn);
        cmd.Parameters.AddWithValue("b", r.Lote.Id);
        var resumenTexto = (string?)await cmd.ExecuteScalarAsync(Ct);

        resumenTexto.Should().NotBeNull();
        resumenTexto.Should().NotContain("1037654321").And.NotContain("JUAN").And.NotContain("PEREZ");
        var filtro = JsonDocument.Parse(resumenTexto!).RootElement.GetProperty("filtro");
        var status = filtro.GetProperty("status");
        status[0].GetString().Should().Be(TramiteEstado.Entregado);
        status[1].GetProperty("presente").GetBoolean().Should().BeTrue();
        status[1].GetProperty("longitud").GetInt32().Should().Be(pii.Length);
        filtro.GetProperty("sortBy").GetProperty("longitud").GetInt32().Should().Be(pii.Length);
        filtro.GetProperty("sortDir").GetProperty("longitud").GetInt32().Should().Be("PEREZ".Length);
    }

    [PostgresFact]
    public async Task AC6_IdsDeLaBandejaYDeOtroOrganismo_SoloCongelaLosDeLaBandeja()
    {
        await SembrarAsync();
        var deLaBandeja = new[] { HierarchyScenario.C1, HierarchyScenario.C2, HierarchyScenario.X }
            .Select(HierarchyScenario.DeliveredProcedureOf).ToList();
        var deOtroOrganismo = await SembrarEnOt2Async(2);

        CrearLoteConsolidadosResultado r;
        var comando = await ComandoOtAdminAsync(new SeleccionPorIds([.. deLaBandeja, .. deOtroOrganismo]));
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(comando, Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(3);
        await using var db = NewContext();
        (await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == r.Lote.Id)
                .Select(i => i.ProcedureInstanceId).ToListAsync(Ct))
            .Should().BeEquivalentTo(deLaBandeja);
        (await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == r.Lote.Id, Ct))
            .IdsCount.Should().Be(5, "la auditoría registra cuántos ids llegaron, no cuáles");
    }

    [PostgresFact]
    public async Task AC9_SiLaAuditoriaFalla_NoQuedaLoteNiItems()
    {
        await SembrarAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            CREATE OR REPLACE FUNCTION public.tmp_falla_auditoria_13391() RETURNS trigger AS $$
            BEGIN
                IF NEW.event = 'lote_creado' THEN RAISE EXCEPTION 'auditoria no disponible (prueba HU13391)'; END IF;
                RETURN NEW;
            END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER tmp_falla_auditoria_13391 BEFORE INSERT ON tramites.consolidado_export_audit
                FOR EACH ROW EXECUTE FUNCTION public.tmp_falla_auditoria_13391();
            """);
        CrearLoteConsolidadosResultado r;
        try
        {
            var comando = await ComandoOtAdminAsync(new SeleccionPorIds([HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1)]));
            await using var ctx = NewContext();
            r = await Handler(ctx).HandleAsync(comando, Ct);
        }
        finally
        {
            await ExecAsync(cn,
                """
                DROP TRIGGER IF EXISTS tmp_falla_auditoria_13391 ON tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS public.tmp_falla_auditoria_13391();
                """);
        }

        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteNoCreado);
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(0);
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_batch_items")).Should().Be(0);
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    /// <summary>Trámites entregados a <c>Ot2</c> (otro organismo) del cliente C1.</summary>
    private async Task<IReadOnlyList<Guid>> SembrarEnOt2Async(int cuantos)
    {
        await using var ctx = NewContext();
        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync(Ct);
        var ids = new List<Guid>();
        for (var i = 0; i < cuantos; i++)
        {
            var p = new Flit.Tramites.Domain.Entities.ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = HierarchyScenario.C1,
                ProcedureTypeId = type,
                ReferenceNumber = $"{713391 + i}",
                Status = TramiteEstado.Entregado,
                TransitOfficeId = HierarchyScenario.Ot2,
                Plate = $"OT2{i:D3}",
                CreatedByUserId = HierarchyScenario.UserOf(HierarchyScenario.C1),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            };
            ctx.ProcedureInstances.Add(p);
            ids.Add(p.Id);
        }

        await ctx.SaveChangesAsync(Ct);
        return ids;
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }
}
