using System.Reflection;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Hierarchy;

/// <summary>
/// HU #12406 (AC5, AC6, AC7) — el trámite conserva el padre que tenía la compañía radicadora al
/// crearse (<c>tramites.procedure_instances.parent_tenant_id_at_creation</c>), contra PostgreSQL real
/// y por los DOS puntos de persistencia del comando de creación: el handler de trámites
/// (<see cref="CreateProcedureInstanceHandler"/> → <see cref="ProcedureInstanceRepository.AddWithUniqueReferenceAsync"/>)
/// y el repositorio administrativo (<see cref="AdminProcedureInstanceRepository.CreateWithSnapshotAsync"/>).
/// Escenario: <see cref="HierarchyScenario"/> (P cabeza CONCESION; C1, C2 hijos; X ajeno; S aislado).
/// <para>
/// Uso de ejemplo: un usuario de C1 crea un trámite → <c>ParentTenantIdAtCreation == P</c>; el
/// SuperAdmin desvincula a C1 → el trámite sigue diciendo P.
/// </para>
/// </summary>
public sealed class ParentSnapshotTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";

    private static readonly Guid OtherHead = new("a0000000-0000-4000-8000-000000000200");

    // ── AC5 · el trámite conserva el padre de la compañía radicadora ────────────

    [PostgresFact]
    public async Task AC5_Hijo_crea_tramite_por_el_handler_y_queda_con_P_como_padre_al_crear()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();

        var created = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using var check = NewContext();
        var row = await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == created);
        row.TenantId.Should().Be(HierarchyScenario.C1);
        row.ParentTenantIdAtCreation.Should().Be(HierarchyScenario.P);
        row.ReferenceNumber.Should().NotBeNullOrEmpty("el INSERT es el mismo de siempre: radicado por secuencia");
    }

    [PostgresFact]
    public async Task AC5_Cliente_sin_padre_deja_el_valor_nulo()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();

        var deS = await CreateViaHandlerAsync(HierarchyScenario.S, type.Id);
        var deP = await CreateViaHandlerAsync(HierarchyScenario.P, type.Id);
        var deX = await CreateViaHandlerAsync(HierarchyScenario.X, type.Id);

        await using var check = NewContext();
        var rows = await check.ProcedureInstances.AsNoTracking()
            .Where(p => p.Id == deS || p.Id == deP || p.Id == deX)
            .ToListAsync();

        rows.Should().HaveCount(3);
        rows.Should().AllSatisfy(r => r.ParentTenantIdAtCreation.Should().BeNull(
            "ni el aislado, ni el ajeno, ni la propia cabeza tienen padre"));
    }

    [PostgresFact]
    public async Task AC5_El_repositorio_administrativo_tambien_conserva_el_padre_en_el_mismo_INSERT()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();

        Guid deC2, deS;
        await using (var ctx = NewContext())
        {
            var repo = new AdminProcedureInstanceRepository(ctx);
            deC2 = (await repo.CreateWithSnapshotAsync(
                new NewProcedureInstance(HierarchyScenario.C2, type.Id, string.Empty, null, HierarchyScenario.UserOf(HierarchyScenario.C2)),
                "{}", null)).Id;
            deS = (await repo.CreateWithSnapshotAsync(
                new NewProcedureInstance(HierarchyScenario.S, type.Id, string.Empty, null, HierarchyScenario.UserOf(HierarchyScenario.S)),
                "{}", null)).Id;
        }

        await using var check = NewContext();
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == deC2)).ParentTenantIdAtCreation
            .Should().Be(HierarchyScenario.P);
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == deS)).ParentTenantIdAtCreation
            .Should().BeNull();
    }

    [PostgresFact]
    public async Task AC5_Modificarlo_en_una_entidad_rastreada_es_rechazado_por_EF_antes_de_llegar_a_la_base()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();
        var created = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using (var ctx = NewContext())
        {
            var row = await ctx.ProcedureInstances.SingleAsync(p => p.Id == created);
            row.ParentTenantIdAtCreation = HierarchyScenario.X;

            var act = () => ctx.SaveChangesAsync();
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*ParentTenantIdAtCreation*", "AfterSaveBehavior.Throw: la propiedad es de solo inserción");
        }

        await using var check = NewContext();
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == created)).ParentTenantIdAtCreation
            .Should().Be(HierarchyScenario.P);
    }

    [PostgresFact]
    public async Task AC5_La_ruta_de_actualizacion_del_repositorio_no_toca_el_valor()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();
        var created = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        // UpdateAsync (db.Update sobre entidad desconectada) es la ruta de escritura genérica del
        // wizard: marca modificadas TODAS las columnas guardables. La de solo inserción no viaja.
        await using (var ctx = NewContext())
        {
            var detached = await ctx.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == created);
            detached.Plate = "ZZZ999";
            detached.ParentTenantIdAtCreation = HierarchyScenario.X; // un bug lo intentaría así

            var repo = new ProcedureInstanceRepository(ctx);
            await repo.UpdateAsync(detached, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var row = await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == created);
        row.Plate.Should().Be("ZZZ999", "el resto de la fila sí se actualiza");
        row.ParentTenantIdAtCreation.Should().Be(HierarchyScenario.P, "la columna de solo inserción no viaja en el UPDATE");
    }

    [PostgresFact]
    public async Task AC5_Un_UPDATE_directo_en_SQL_es_rechazado_por_el_trigger_de_inmutabilidad()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();
        var created = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var update = new NpgsqlCommand(
            "UPDATE tramites.procedure_instances SET parent_tenant_id_at_creation = NULL WHERE id = @id", connection);
        update.Parameters.AddWithValue("id", created);

        var pg = (await update.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which;
        pg.SqlState.Should().Be(CheckViolation);
        pg.MessageText.Should().Contain("parent_tenant_id_at_creation es inmutable");

        // Y un UPDATE que mencione la columna con el MISMO valor no dispara el rechazo (IS DISTINCT FROM).
        await using var same = new NpgsqlCommand(
            "UPDATE tramites.procedure_instances SET parent_tenant_id_at_creation = @p WHERE id = @id", connection);
        same.Parameters.AddWithValue("p", HierarchyScenario.P);
        same.Parameters.AddWithValue("id", created);
        (await same.ExecuteNonQueryAsync()).Should().Be(1);
    }

    /// <summary>
    /// Barrido estático (AC5 «ninguna ruta de escritura posterior modifica ese valor»): en el código
    /// de producto la propiedad solo se ASIGNA en los dos puntos de persistencia de la creación.
    /// </summary>
    [PostgresFact]
    public async Task AC5_En_el_codigo_de_producto_solo_la_creacion_asigna_la_propiedad()
    {
        var src = LocateSourceDirectory();
        var asignaciones = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var lines = await File.ReadAllLinesAsync(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("ParentTenantIdAtCreation =", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                    asignaciones.Add($"{Path.GetRelativePath(src, file).Replace('\\', '/')}:{i + 1}");
            }
        }

        asignaciones.Select(a => a[..a.LastIndexOf(':')]).Distinct().Should().BeEquivalentTo(
        [
            "Flit.Infrastructure/Persistence/Repositories/ProcedureInstanceRepository.cs",
            "Flit.Infrastructure/Persistence/Repositories/AdminProcedureInstanceRepository.cs",
        ], "cualquier otra asignación es una ruta de escritura posterior no autorizada");
    }

    // ── AC6 · el desvínculo no altera el valor conservado ───────────────────────

    [PostgresFact]
    public async Task AC6_Desvincular_al_hijo_no_altera_el_padre_conservado_en_el_tramite()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();
        var antes = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using (var ctx = NewContext())
        {
            var c1 = await ctx.Tenants.SingleAsync(t => t.Id == HierarchyScenario.C1);
            c1.ParentTenantId = null;
            await ctx.SaveChangesAsync();
        }

        var despues = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using var check = NewContext();
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == antes)).ParentTenantIdAtCreation
            .Should().Be(HierarchyScenario.P, "conserva el padre que tenía al crearse");
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == despues)).ParentTenantIdAtCreation
            .Should().BeNull("creado ya sin padre");
    }

    [PostgresFact]
    public async Task AC6_Vincular_al_hijo_a_otra_cabeza_no_altera_el_padre_conservado()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();
        var antes = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using (var ctx = NewContext())
        {
            var other = TenantSeed.GroupParent(id: OtherHead, code: "IT-P2");
            other.TaxId = "900000000200000";
            ctx.Tenants.Add(other);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var c1 = await ctx.Tenants.SingleAsync(t => t.Id == HierarchyScenario.C1);
            c1.ParentTenantId = OtherHead;
            await ctx.SaveChangesAsync();
        }

        var despues = await CreateViaHandlerAsync(HierarchyScenario.C1, type.Id);

        await using var check = NewContext();
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == antes)).ParentTenantIdAtCreation
            .Should().Be(HierarchyScenario.P);
        (await check.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == despues)).ParentTenantIdAtCreation
            .Should().Be(OtherHead, "el padre conservado es el del momento de crear cada trámite");
    }

    // ── AC7 · paridad de un cliente sin jerarquía ───────────────────────────────

    [PostgresFact]
    public async Task AC7_Los_tramites_del_cliente_aislado_son_los_de_siempre_y_los_nuevos_nacen_sin_padre()
    {
        var type = await SeedScenarioWithEnabledTypeAsync();

        await using var before = NewContext();
        var sembrados = await before.ProcedureInstances.AsNoTracking()
            .Where(p => p.TenantId == HierarchyScenario.S).OrderBy(p => p.CreatedAt).Select(p => p.Id).ToListAsync();
        sembrados.Should().BeEquivalentTo(HierarchyScenario.ProceduresOf(HierarchyScenario.S));

        var nuevo = await CreateViaHandlerAsync(HierarchyScenario.S, type.Id);

        await using var after = NewContext();
        var deS = await after.ProcedureInstances.AsNoTracking()
            .Where(p => p.TenantId == HierarchyScenario.S).ToListAsync();
        deS.Select(p => p.Id).Should().BeEquivalentTo(sembrados.Append(nuevo));
        deS.Should().AllSatisfy(p => p.ParentTenantIdAtCreation.Should().BeNull());

        // Q01 con el alcance de S: mismo conteo con y sin la HU (la columna nueva no filtra nada).
        var conAlcance = await after.ProcedureInstances.AsNoTracking()
            .Where(p => p.TenantId == HierarchyScenario.S).CountAsync();
        conAlcance.Should().Be(3);
    }

    [PostgresFact]
    public void AC7_Ningun_DTO_de_respuesta_de_las_consultas_cubiertas_expone_los_campos_nuevos()
    {
        var contratos = TenantParityTests.ContratosDeRespuesta().Select(r => (Type)((ITheoryDataRow)r).GetData()[0]!).ToList();
        contratos.Should().NotBeEmpty();

        foreach (var dto in contratos)
        {
            dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
                .Should().NotContain(["GroupKind", "ParentTenantIdAtCreation"], $"{dto.Name} no cambia de contrato en esta HU");
        }

        // Ni el resumen que devuelve el propio comando de creación.
        typeof(ProcedureInstanceSummary).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Id", "ReferenceNumber", "Status", "ProcedureTypeId", "TenantId", "CreatedAt", "SubmittedAt", "DraftFinalizedAt"]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra el escenario y devuelve un tipo publicado y habilitado para el asistente. El catálogo
    /// preservado puede no tener ninguno habilitado: en ese caso se habilita el elegido y se restaura
    /// al final de la prueba (regla del arnés: quien muta una tabla preservada, la restaura).
    /// </summary>
    private async Task<ProcedureType> SeedScenarioWithEnabledTypeAsync()
    {
        var type = await HierarchyScenario.SeedAsync(Fixture);

        await using var ctx = NewContext();
        var row = await ctx.ProcedureTypes.SingleAsync(t => t.Id == type.Id);
        if (row.PublicationStatus != PublicationStatus.Published || !row.WizardEnabled)
        {
            _restoreType = (row.Id, row.PublicationStatus, row.WizardEnabled);
            row.PublicationStatus = PublicationStatus.Published;
            row.WizardEnabled = true;
            await ctx.SaveChangesAsync();
        }

        return type;
    }

    private (Guid Id, string Status, bool Wizard)? _restoreType;

    public override async ValueTask DisposeAsync()
    {
        if (_restoreType is { } r && PostgresAvailability.IsAvailable)
        {
            await using var ctx = NewContext();
            var row = await ctx.ProcedureTypes.SingleAsync(t => t.Id == r.Id);
            row.PublicationStatus = r.Status;
            row.WizardEnabled = r.Wizard;
            await ctx.SaveChangesAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>El comando de creación real, con los repositorios reales sobre la base efímera.</summary>
    private async Task<Guid> CreateViaHandlerAsync(Guid tenantId, Guid procedureTypeId)
    {
        await using var ctx = NewContext();
        var handler = new CreateProcedureInstanceHandler(new ProcedureInstanceRepository(ctx), new ProcedureTypeRepository(ctx));

        var (result, error) = await handler.HandleAsync(
            new CreateProcedureInstanceRequest(tenantId, procedureTypeId, HierarchyScenario.UserOf(tenantId), TransitOfficeId: null));

        error.Should().BeNull();
        result.Should().NotBeNull();
        return result!.Id;
    }

    private static string LocateSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Infrastructure");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "src");
            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró services/core-api/src desde " + AppContext.BaseDirectory);
    }
}
