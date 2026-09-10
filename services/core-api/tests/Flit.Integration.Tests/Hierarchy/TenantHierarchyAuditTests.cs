using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12319 (AC3) — la bitácora <c>identity.tenant_hierarchy_audit</c> (DDL 108, HU #12323) la
/// escribe el trigger <c>tr_tenants_hierarchy_audit</c> y la protege
/// <c>tr_tenant_hierarchy_audit_immutable</c>: recibe <c>LINK</c>/<c>UNLINK</c> y rechaza
/// <c>UPDATE</c>/<c>DELETE</c>, todo contra PostgreSQL real.
/// <para>
/// Uso de ejemplo: sembrar padre e hijo con el <c>FlitDbContext</c> real y leer
/// <c>ctx.TenantHierarchyAuditEntries</c>; intentar borrar una fila y capturar la
/// <see cref="PostgresException"/> 23514 del trigger de inmutabilidad.
/// </para>
/// </summary>
public sealed class TenantHierarchyAuditTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid ActorId = new("55555555-5555-4555-8555-555555555555");

    [PostgresFact]
    public async Task Colgar_un_hijo_escribe_LINK_en_la_bitacora()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        var entries = await ReadAuditAsync();

        entries.Should().ContainSingle();
        entries[0].Action.Should().Be(TenantHierarchyAuditEntry.LinkAction);
        entries[0].ParentTenantId.Should().Be(TenantSeed.ParentId);
        entries[0].ChildTenantId.Should().Be(TenantSeed.ChildId);
        entries[0].OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [PostgresFact]
    public async Task Crear_un_tenant_sin_padre_no_escribe_nada()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.Lone());

        (await ReadAuditAsync()).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Desvincular_escribe_UNLINK_y_conserva_el_LINK_anterior()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using (var ctx = NewContext())
        {
            var child = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ChildId);
            child.ParentTenantId = null;
            await ctx.SaveChangesAsync();
        }

        var entries = await ReadAuditAsync();

        entries.Select(e => e.Action).Should().Equal(TenantHierarchyAuditEntry.LinkAction, TenantHierarchyAuditEntry.UnlinkAction);
        entries.Should().AllSatisfy(e =>
        {
            e.ParentTenantId.Should().Be(TenantSeed.ParentId);
            e.ChildTenantId.Should().Be(TenantSeed.ChildId);
        });
    }

    [PostgresFact]
    public async Task Cambiar_de_padre_escribe_UNLINK_del_anterior_y_LINK_del_nuevo()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.GroupParent(id: TenantSeed.LoneId, code: "IT-PARENT-2"));
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using (var ctx = NewContext())
        {
            var child = await ctx.Tenants.SingleAsync(t => t.Id == TenantSeed.ChildId);
            child.ParentTenantId = TenantSeed.LoneId;
            await ctx.SaveChangesAsync();
        }

        var entries = await ReadAuditAsync();

        entries.Should().HaveCount(3);
        entries.Skip(1).Select(e => (e.Action, e.ParentTenantId)).Should().BeEquivalentTo(
        [
            (TenantHierarchyAuditEntry.UnlinkAction, TenantSeed.ParentId),
            (TenantHierarchyAuditEntry.LinkAction, TenantSeed.LoneId),
        ]);
    }

    /// <summary>El actor sale de <c>app.current_user_id</c> de la sesión cuando la aplicación lo fija (como hace el middleware de tenant).</summary>
    [PostgresFact]
    public async Task Actor_sale_de_app_current_user_id_de_la_sesion()
    {
        await SeedAsync(TenantSeed.GroupParent());

        await using (var ctx = NewContext())
        await using (var tx = await ctx.Database.BeginTransactionAsync())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_user_id', {ActorId.ToString()}, true)");
            ctx.Tenants.Add(TenantSeed.ChildOf(TenantSeed.ParentId));
            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var entry = (await ReadAuditAsync()).Should().ContainSingle().Which;
        entry.ActorUserId.Should().Be(ActorId);
    }

    [PostgresFact]
    public async Task Sin_sesion_el_actor_cae_al_created_by_de_la_fila()
    {
        await SeedAsync(TenantSeed.GroupParent());
        var child = TenantSeed.ChildOf(TenantSeed.ParentId);
        child.CreatedBy = ActorId;
        await SeedAsync(child);

        var entry = (await ReadAuditAsync()).Should().ContainSingle().Which;
        entry.ActorUserId.Should().Be(ActorId);
    }

    [PostgresFact]
    public async Task DELETE_sobre_la_bitacora_es_rechazado_por_el_trigger_de_inmutabilidad()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));
        var entryId = (await ReadAuditAsync()).Single().Id;

        await using var ctx = NewContext();
        var attempt = () => ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM identity.tenant_hierarchy_audit WHERE id = {entryId}");

        var pg = (await attempt.Should().ThrowAsync<PostgresException>()).Which;
        pg.SqlState.Should().Be("23514");
        pg.MessageText.Should().Contain("identity.tenant_hierarchy_audit es append-only: no se permite DELETE");
        pg.MessageText.Should().Contain(entryId.ToString());

        (await ReadAuditAsync()).Should().ContainSingle("la fila sigue ahí");
    }

    [PostgresFact]
    public async Task UPDATE_sobre_la_bitacora_es_rechazado_por_el_trigger_de_inmutabilidad()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using var ctx = NewContext();
        var attempt = () => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE identity.tenant_hierarchy_audit SET action = 'UNLINK'");

        var pg = (await attempt.Should().ThrowAsync<PostgresException>()).Which;
        pg.SqlState.Should().Be("23514");
        pg.MessageText.Should().Contain("no se permite UPDATE");
    }

    /// <summary>Sin FK a <c>identity.tenants</c> a propósito: la traza sobrevive al borrado del hijo.</summary>
    [PostgresFact]
    public async Task La_bitacora_sobrevive_al_borrado_del_hijo()
    {
        await SeedAsync(TenantSeed.GroupParent());
        await SeedAsync(TenantSeed.ChildOf(TenantSeed.ParentId));

        await using (var ctx = NewContext())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE id = {TenantSeed.ChildId}");
        }

        var entries = await ReadAuditAsync();
        entries.Should().ContainSingle().Which.ChildTenantId.Should().Be(TenantSeed.ChildId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task SeedAsync(Tenant tenant)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
    }

    private async Task<List<TenantHierarchyAuditEntry>> ReadAuditAsync()
    {
        await using var ctx = NewContext();
        return await ctx.TenantHierarchyAuditEntries.AsNoTracking()
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync();
    }
}
