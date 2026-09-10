using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12406 (AC1, AC2, AC3, AC4, AC7) por la ruta de PRODUCTO — <see cref="CreateCompanyHandler"/> y
/// <see cref="UpdateCompanyHandler"/> con <see cref="CompanyWriteRepository"/> real sobre PostgreSQL:
/// (a) el alta con CONCESION / MARCA_BLANCA nace con <c>is_group_parent = true</c>; (c) cambiar el tipo
/// de una cabeza con hijos devuelve 422 explícito (no 500) y no escribe nada; (d) sin hijos se acepta y
/// queda auditado en <c>admin.tenant_config_audit_logs</c> con valor anterior y nuevo; (e) un cliente
/// RENTING sin jerarquía se crea y edita exactamente como antes. Y el alcance (AC4) de la cabeza recién
/// creada expone su clase leída de la base.
/// <para>
/// Uso de ejemplo: <c>await new CreateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(cmd)</c>
/// con <c>TenantType = "CONCESION"</c> → <c>Company.TenantType == "CONCESION"</c> y la fila con
/// <c>is_group_parent = true</c>.
/// </para>
/// </summary>
public sealed class HeadTenantTypeCompanyHandlersTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Actor = new("66666666-6666-4666-8666-666666666666");

    // ── (a) alta con tipo de cabeza ─────────────────────────────────────────────

    [PostgresTheory]
    [InlineData("CONCESION", GroupKind.Concesion)]
    [InlineData("MARCA_BLANCA", GroupKind.MarcaBlanca)]
    public async Task AC1_Alta_con_tipo_de_cabeza_nace_marcada_cabeza_y_su_alcance_expone_la_clase(string tenantType, GroupKind esperada)
    {
        var id = await CreateAsync("Cabeza " + tenantType.Replace('_', ' '), "900100200-1", "IT-HEAD-" + tenantType, tenantType);

        await using var check = NewContext();
        var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
        row.TenantType.Should().Be(tenantType);
        row.IsGroupParent.Should().BeTrue("ck_tenants_group_parent_by_type: la marca viaja en la misma fila");
        row.ParentTenantId.Should().BeNull();

        // AC4: con un hijo colgado, el resolver expone la clase leída de la base.
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.ChildOf(id));
            await ctx.SaveChangesAsync();
        }

        await using var scopeCtx = NewContext();
        var scope = await new DbTenantScopeResolver(scopeCtx, new DbHierarchySwitches(scopeCtx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance)
            .ResolveAsync(id);
        scope.IsGroup.Should().BeTrue();
        scope.GroupKind.Should().Be(esperada);
        scope.ReadTenantIds.Should().BeEquivalentTo([id, TenantSeed.ChildId]);
    }

    [PostgresFact]
    public async Task AC1_Alta_con_tipo_fuera_del_catalogo_devuelve_422_con_el_catalogo_de_cinco()
    {
        await using var ctx = NewContext();
        var result = await new CreateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("X", "900100200-2", "IT-BAD", "FRANQUICIA", true),
            CreatedBy = null,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "tenantType")
            .Which.Message.Should().Be("El tipo de compañía debe ser RENTING, CONCESIONARIO, FLIT, CONCESION o MARCA_BLANCA.");
    }

    // ── (d) cambio de tipo sin hijos: aceptado y auditado ───────────────────────

    [PostgresFact]
    public async Task AC3_Sin_hijos_el_cambio_de_clase_se_acepta_y_queda_auditado_con_valor_anterior_y_nuevo()
    {
        var id = await CreateAsync("Cabeza", "900100200-3", "IT-HEAD", "CONCESION");
        await SeedActorAsync(id);

        var result = await UpdateAsync(id, "Cabeza", "900100200-3", "MARCA_BLANCA");

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        result.Company!.TenantType.Should().Be("MARCA_BLANCA");

        await using var check = NewContext();
        var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
        row.TenantType.Should().Be("MARCA_BLANCA");
        row.IsGroupParent.Should().BeTrue();
        row.UpdatedBy.Should().Be(Actor);

        var audit = await check.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.TenantId == id && a.FieldName == CompanyWriteRepository.AuditTenantTypeField)
            .ToListAsync();
        var fila = audit.Should().ContainSingle().Which;
        fila.EntityName.Should().Be(CompanyWriteRepository.AuditEntityName);
        fila.OldValue.Should().Be("\"CONCESION\"");
        fila.NewValue.Should().Be("\"MARCA_BLANCA\"");
        fila.ChangedBy.Should().Be(Actor);
        fila.Operation.Should().Be(AuditVocabulary.Operations.Update);
        fila.Result.Should().Be(AuditVocabulary.Results.Success);
        fila.Module.Should().Be(AuditVocabulary.Modules.Companies);
        fila.TargetEntityId.Should().Be(id);
    }

    [PostgresFact]
    public async Task AC2_Cabeza_sin_hijos_puede_volver_a_un_tipo_que_no_es_de_cabeza_y_queda_desmarcada()
    {
        var id = await CreateAsync("Cabeza", "900100200-4", "IT-HEAD", "CONCESION");
        await SeedActorAsync(id);

        var result = await UpdateAsync(id, "Cabeza", "900100200-4", "CONCESIONARIO");

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);

        await using var check = NewContext();
        var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
        row.TenantType.Should().Be("CONCESIONARIO");
        row.IsGroupParent.Should().BeFalse("al salir del tipo de cabeza la marca cae en la misma operación");
        (await check.TenantConfigAuditLogs.AsNoTracking().CountAsync(a => a.TenantId == id && a.FieldName == "tenant_type")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC1_Cliente_existente_pasa_a_cabeza_por_edicion_y_nace_marcado()
    {
        var id = await CreateAsync("Suelto", "900100200-5", "IT-LONE", "CONCESIONARIO");
        await SeedActorAsync(id);

        var result = await UpdateAsync(id, "Suelto", "900100200-5", "MARCA_BLANCA");

        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        await using var check = NewContext();
        var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
        row.IsGroupParent.Should().BeTrue();
        row.TenantType.Should().Be("MARCA_BLANCA");
    }

    // ── (c) cambio de tipo con hijos: 422 explícito, nada escrito ───────────────

    [PostgresFact]
    public async Task AC3_Con_hijos_vigentes_el_cambio_de_clase_devuelve_422_explicito_y_no_escribe_nada()
    {
        var id = await CreateAsync("Cabeza", "900100200-6", "IT-HEAD", "CONCESION");
        await SeedActorAsync(id);
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.ChildOf(id));
            await ctx.SaveChangesAsync();
        }

        var result = await UpdateAsync(id, "Cabeza", "900100200-6", "MARCA_BLANCA");

        result.Outcome.Should().Be(UpdateCompanyOutcome.Invalid, "422, nunca 500");
        var error = result.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be("tenantType");
        error.Message.Should().StartWith("No se puede cambiar el tipo de una cabeza de grupo mientras tenga compañías hijas vinculadas.");
        error.Message.Should().Contain("no puede cambiar de CONCESION a MARCA_BLANCA");

        await using var check = NewContext();
        var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
        row.TenantType.Should().Be("CONCESION");
        row.IsGroupParent.Should().BeTrue();
        (await check.TenantConfigAuditLogs.AsNoTracking().AnyAsync(a => a.TenantId == id && a.FieldName == "tenant_type"))
            .Should().BeFalse("todo o nada: si la base rechazó el tipo, tampoco hay auditoría");
    }

    [PostgresFact]
    public async Task AC3_Con_hijos_vigentes_tampoco_puede_dejar_de_ser_cabeza_por_edicion()
    {
        var id = await CreateAsync("Cabeza", "900100200-7", "IT-HEAD", "MARCA_BLANCA");
        await SeedActorAsync(id);
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.ChildOf(id));
            await ctx.SaveChangesAsync();
        }

        var result = await UpdateAsync(id, "Cabeza", "900100200-7", "RENTING");

        result.Outcome.Should().Be(UpdateCompanyOutcome.Invalid);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("no puede cambiar de MARCA_BLANCA a RENTING");

        await using var check = NewContext();
        (await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id)).TenantType.Should().Be("MARCA_BLANCA");
    }

    // ── (e) paridad: cliente RENTING sin jerarquía ──────────────────────────────

    [PostgresFact]
    public async Task AC7_Cliente_RENTING_sin_jerarquia_se_crea_y_edita_como_antes()
    {
        var id = await CreateAsync("Renting S.A.", "900100200-8", "IT-RENT", "RENTING");
        await SeedActorAsync(id);

        await using (var check = NewContext())
        {
            var row = await check.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
            row.TenantType.Should().Be("RENTING");
            row.IsGroupParent.Should().BeFalse();
            row.ParentTenantId.Should().BeNull();
        }

        // Editar razón social y NIT sin tocar el tipo: sin fila de auditoría de tipo, sin marca.
        var result = await UpdateAsync(id, "Renting S.A.S.", "900100200-9", "RENTING");
        result.Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        result.Company!.TenantType.Should().Be("RENTING");
        result.Company.RazonSocial.Should().Be("Renting S.A.S.");

        await using var after = NewContext();
        (await after.Tenants.AsNoTracking().SingleAsync(t => t.Id == id)).IsGroupParent.Should().BeFalse();
        (await after.TenantConfigAuditLogs.AsNoTracking().AnyAsync(a => a.TenantId == id && a.FieldName == "tenant_type")).Should().BeFalse();

        // Pasar de RENTING a CONCESIONARIO (tipos de siempre) sigue aceptándose y ahora queda auditado.
        (await UpdateAsync(id, "Renting S.A.S.", "900100200-9", "CONCESIONARIO")).Outcome.Should().Be(UpdateCompanyOutcome.Updated);
        (await after.TenantConfigAuditLogs.AsNoTracking().CountAsync(a => a.TenantId == id && a.FieldName == "tenant_type")).Should().Be(1);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateAsync(string razonSocial, string nit, string code, string tenantType)
    {
        await using var ctx = NewContext();
        var result = await new CreateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest(razonSocial, nit, code, tenantType, true),
            CreatedBy = null,
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}")));
        return result.Company!.Id;
    }

    private async Task<UpdateCompanyResult> UpdateAsync(Guid tenantId, string razonSocial, string nit, string tenantType)
    {
        await using var ctx = NewContext();
        return await new UpdateCompanyHandler(new CompanyWriteRepository(ctx)).HandleAsync(new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = new UpdateCompanyRequest(razonSocial, nit, tenantType, true),
            ChangedBy = Actor,
        });
    }

    /// <summary><c>tenant_config_audit_logs.changed_by</c> tiene FK a <c>identity.users</c>.</summary>
    private async Task SeedActorAsync(Guid homeTenantId)
    {
        await using var ctx = NewContext();
        if (await ctx.Users.AnyAsync(u => u.Id == Actor))
            return;

        ctx.Users.Add(new User
        {
            Id = Actor,
            Email = "it-actor@flit.test",
            DisplayName = "Actor IT",
            Status = "active",
            HomeTenantId = homeTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
