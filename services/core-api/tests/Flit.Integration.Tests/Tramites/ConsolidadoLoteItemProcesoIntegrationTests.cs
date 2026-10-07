using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13375 — <see cref="ConsolidadoLoteItemProceso"/> contra PostgreSQL real con los CHECK del DDL 134: cierre del ítem
/// (incluido / omitido / reintento) condicionado a <c>procesando</c> y contadores del lote en la MISMA sentencia.
/// <para>Uso de ejemplo: <c>await new ConsolidadoLoteItemProceso(ctx).MarcarIncluidoAsync(new(lote, item, adjunto, "generado", ahora), ct)</c>
/// ⇒ <c>true</c> si cerró el ítem; <c>false</c> si ya no estaba en <c>procesando</c> (lote cancelado, cierre repetido).</para>
/// </summary>
public sealed class ConsolidadoLoteItemProcesoIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b3000000-0000-7000-8000-000000013375");
    private static readonly Guid UserU = new("0199c300-0000-7000-8000-000000013375");
    private static readonly Guid TramiteT = new("0199c300-0000-7000-8000-0000000133e1");
    private static readonly Guid LoteL = new("0199c300-0000-7000-8000-0000000133f1");
    private static readonly Guid ItemI = new("0199c300-0000-7000-8000-0000000133a1");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync(string itemStatus = ConsolidadoExportItemStatus.Procesando, short attempts = 0)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13375", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO identity.users (id, email, display_name, status, created_at)
            VALUES (@u, 'lote13375@it.test', 'Usuario lote', 'active', now());
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            VALUES (@t, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13375-1', 'borrador', 'SINTVIN13375A', @u, now());
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_proceso', 1, '\x010203'::bytea, now(), @u);
            INSERT INTO tramites.consolidado_export_batch_items
                (id, tenant_id, batch_id, procedure_instance_id, position, status, attempts, lease_until, claimed_by,
                 reference_number, plate, created_by)
            VALUES (@i, @c, @l, @t, 0, @s, @a, CASE WHEN @s = 'procesando' THEN now() + interval '10 minutes' END,
                    'slot-1', 'IT-13375-1', 'ABC123', @u);
            """,
            cn);
        cmd.Parameters.AddWithValue("u", UserU);
        cmd.Parameters.AddWithValue("c", Company);
        cmd.Parameters.AddWithValue("t", TramiteT);
        cmd.Parameters.AddWithValue("l", LoteL);
        cmd.Parameters.AddWithValue("i", ItemI);
        cmd.Parameters.AddWithValue("s", itemStatus);
        cmd.Parameters.AddWithValue("a", attempts);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<Dictionary<string, object?>> RowAsync(string sql)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("i", ItemI);
        cmd.Parameters.AddWithValue("l", LoteL);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        (await r.ReadAsync(Ct)).Should().BeTrue();
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var k = 0; k < r.FieldCount; k++)
            row[r.GetName(k)] = r.IsDBNull(k) ? null : r.GetValue(k);
        return row;
    }

    private Task<Dictionary<string, object?>> ItemAsync() => RowAsync(
        """
        SELECT status, attempts, next_attempt_at, lease_until, attachment_id, storage_path, size_bytes, delivery_mode,
               omission_code, omission_reason, processed_at, reference_number, plate
          FROM tramites.consolidado_export_batch_items WHERE id = @i
        """);

    private Task<Dictionary<string, object?>> LoteAsync() => RowAsync(
        "SELECT included_count, omitted_count, generated_count FROM tramites.consolidado_export_batches WHERE id = @l");

    private static readonly LoteItemAdjunto Adjunto =
        new(new Guid("0199c300-0000-7000-8000-0000000133b1"), "fm/consolidado-13375", 4096, "sha", "consolidado.pdf");

    [PostgresFact]
    public async Task AC1_Incluido_GuardaSnapshotYModo_YSubeIncluidosYGenerados_UnaSolaVez()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var proceso = new ConsolidadoLoteItemProceso(ctx);

        (await proceso.MarcarIncluidoAsync(new LoteItemIncluido(LoteL, ItemI, Adjunto, ConsolidadoExportDeliveryMode.Generado, Ahora), Ct))
            .Should().BeTrue();
        (await proceso.MarcarIncluidoAsync(new LoteItemIncluido(LoteL, ItemI, Adjunto, ConsolidadoExportDeliveryMode.Generado, Ahora), Ct))
            .Should().BeFalse("el ítem ya no está en procesando: el segundo cierre no cuenta dos veces");

        var item = await ItemAsync();
        item["status"].Should().Be(ConsolidadoExportItemStatus.Incluido);
        item["attachment_id"].Should().Be(Adjunto.AttachmentId);
        item["storage_path"].Should().Be(Adjunto.StoragePath);
        item["size_bytes"].Should().Be(Adjunto.SizeBytes);
        item["delivery_mode"].Should().Be(ConsolidadoExportDeliveryMode.Generado);
        item["reference_number"].Should().Be("IT-13375-1", "el radicado congelado se conserva");
        item["plate"].Should().Be("ABC123", "la placa congelada se conserva");
        ((DateTime)item["processed_at"]!).Should().Be(Ahora.UtcDateTime);
        item["lease_until"].Should().BeNull();

        var lote = await LoteAsync();
        lote["included_count"].Should().Be(1);
        lote["generated_count"].Should().Be(1);
        lote["omitted_count"].Should().Be(0);
    }

    [PostgresFact]
    public async Task AC1_Existente_NoSubeGenerados()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        (await new ConsolidadoLoteItemProceso(ctx).MarcarIncluidoAsync(
            new LoteItemIncluido(LoteL, ItemI, Adjunto, ConsolidadoExportDeliveryMode.Existente, Ahora), Ct)).Should().BeTrue();

        var lote = await LoteAsync();
        lote["included_count"].Should().Be(1);
        lote["generated_count"].Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_AC3_Omitido_GuardaCodigoYTexto_YSubeOmitidos()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        (await new ConsolidadoLoteItemProceso(ctx).MarcarOmitidoAsync(
            new LoteItemOmitido(LoteL, ItemI, ConsolidadoLoteOmisiones.AccesoRevocado, "Acceso revocado", 0, Ahora), Ct))
            .Should().BeTrue();

        var item = await ItemAsync();
        item["status"].Should().Be(ConsolidadoExportItemStatus.Omitido);
        item["omission_code"].Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        item["omission_reason"].Should().Be("Acceso revocado");
        item["delivery_mode"].Should().BeNull();
        var lote = await LoteAsync();
        lote["omitted_count"].Should().Be(1);
        lote["included_count"].Should().Be(0);
    }

    [PostgresFact]
    public async Task AC4_Reprogramar_VuelveAPendienteConIntentosYNextAttemptAt_SinTocarContadores()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var siguiente = Ahora.AddSeconds(30);

        (await new ConsolidadoLoteItemProceso(ctx).ReprogramarAsync(new LoteItemReintento(LoteL, ItemI, 1, siguiente), Ct))
            .Should().BeTrue();

        var item = await ItemAsync();
        item["status"].Should().Be(ConsolidadoExportItemStatus.Pendiente);
        item["attempts"].Should().Be((short)1);
        ((DateTime)item["next_attempt_at"]!).Should().Be(siguiente.UtcDateTime);
        item["lease_until"].Should().BeNull();
        item["processed_at"].Should().BeNull();
        var lote = await LoteAsync();
        lote["omitted_count"].Should().Be(0);
        lote["included_count"].Should().Be(0);
    }

    [PostgresFact]
    public async Task AC4_AlAgotarIntentos_OmiteErrorTecnicoConLosIntentos()
    {
        await SeedAsync(attempts: 2);
        await using var ctx = NewContext();

        (await new ConsolidadoLoteItemProceso(ctx).MarcarOmitidoAsync(
            new LoteItemOmitido(LoteL, ItemI, ConsolidadoLoteOmisiones.ErrorTecnico,
                "No se pudo generar el consolidado, intente de nuevo", 3, Ahora), Ct)).Should().BeTrue();

        var item = await ItemAsync();
        item["omission_code"].Should().Be(ConsolidadoLoteOmisiones.ErrorTecnico);
        item["attempts"].Should().Be((short)3);
    }

    [PostgresFact]
    public async Task AC2_AC6_AccessChecker_SqlReal_MembresiaEnLaCompaniaDelLote_YRolRetirado()
    {
        await SeedAsync();
        var asignacion = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            var accion = await ctx.RbacActions.FirstOrDefaultAsync(a => a.Slug == ConsolidadoLotePermisos.Descargar, Ct);
            if (accion is null)
            {
                var modulo = new SecurityModule { Id = Guid.NewGuid(), Code = "it-13375", Name = "IT 13375", CreatedAt = DateTimeOffset.UtcNow };
                ctx.SecurityModules.Add(modulo);
                accion = new RbacAction
                {
                    Id = Guid.NewGuid(),
                    ModuleId = modulo.Id,
                    Slug = ConsolidadoLotePermisos.Descargar,
                    Name = "Descargar",
                    HttpMethod = "POST",
                    RoutePattern = "/api/v1/tramites/consolidados/lotes",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                ctx.RbacActions.Add(accion);
            }

            var rol = new Role { Id = Guid.NewGuid(), Code = "it_13375_radicador", Name = "IT 13375", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            ctx.Roles.Add(rol);
            ctx.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = rol.Id, PermissionId = accion.Id, CreatedAt = DateTimeOffset.UtcNow });
            ctx.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = asignacion,
                UserId = UserU,
                RoleId = rol.Id,
                TenantId = Company,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var contexto = new LoteItemContexto(LoteL, ItemI, ConsolidadoExportOrigin.Tramites, Company, Company, null, UserU,
            "it_13375_radicador", TramiteT, ConsolidadoExportDocumentType.Consolidado);

        await using (var ctx = NewContext())
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            (await new ConsolidadoLoteAccessChecker(ctx, cache).TieneAccesoAsync(contexto, Ct)).Should().BeTrue();
        }

        await using (var ctx = NewContext())
        {
            var ura = await ctx.UserRoleAssignments.SingleAsync(a => a.Id == asignacion, Ct);
            ura.DeletedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            (await new ConsolidadoLoteAccessChecker(ctx, cache).TieneAccesoAsync(contexto, Ct))
                .Should().BeFalse("se retiró el rol en la compañía del lote después de crearlo");
        }
    }

    [PostgresFact]
    public async Task Contrato_ItemCancelado_NingunCierreLoSobrescribe()
    {
        await SeedAsync(ConsolidadoExportItemStatus.Cancelado);
        await using var ctx = NewContext();
        var proceso = new ConsolidadoLoteItemProceso(ctx);

        (await proceso.MarcarIncluidoAsync(new LoteItemIncluido(LoteL, ItemI, Adjunto, ConsolidadoExportDeliveryMode.Existente, Ahora), Ct)).Should().BeFalse();
        (await proceso.MarcarOmitidoAsync(new LoteItemOmitido(LoteL, ItemI, ConsolidadoLoteOmisiones.SinAdjuntos, "x", 0, Ahora), Ct)).Should().BeFalse();
        (await proceso.ReprogramarAsync(new LoteItemReintento(LoteL, ItemI, 1, Ahora), Ct)).Should().BeFalse();

        (await ItemAsync())["status"].Should().Be(ConsolidadoExportItemStatus.Cancelado);
        var lote = await LoteAsync();
        lote["included_count"].Should().Be(0);
        lote["omitted_count"].Should().Be(0);
    }
}
