using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Companies;

/// <summary>
/// HU #13998 y HU #14004 (Feature #13996) — los filtros nuevos del listado de compañías
/// (<c>codigo</c>, <c>tenantType</c>) y de la auditoría de la compañía (<c>campo</c>,
/// <c>valorAnterior</c>, <c>operador</c>, rango de fechas) contra PostgreSQL real: los tests de
/// aplicación corren con InMemory y no prueban que Npgsql traduzca <c>Guid.ToString()</c>, el
/// <c>Users.Any(...)</c> correlacionado ni el <c>Contains</c> sobre un arreglo de tipos.
/// </summary>
public sealed class FiltrosPorColumnaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = Guid.Parse("f0010b13-9980-7000-8000-00000000f001");
    private static readonly Guid TenantB = Guid.Parse("f0020b13-9980-7000-8000-00000000f002");
    private static readonly Guid TenantC = Guid.Parse("f0030b13-9980-7000-8000-00000000f003");
    private static readonly Guid UserAna = Guid.Parse("0199b000-0000-7000-8000-00000000fa01");
    private static readonly Guid UserLuis = Guid.Parse("7a2bc000-0000-7000-8000-00000000fa02");
    // Sembrados en UTC (Npgsql no escribe otro offset). Dia2 es 23:30 Bogotá = 04:30 UTC del día 3:
    // si el rango se calculara en UTC en vez de hora Bogotá, quedaría fuera del día 2.
    private static readonly DateTimeOffset Dia1 = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
    private static readonly DateTimeOffset Dia2 = new DateTimeOffset(2026, 10, 2, 23, 30, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(TenantA, "FCOL-0042", isGroupParent: false, parentId: null, tenantType: "RENTING"));
        ctx.Tenants.Add(TenantSeed.New(TenantB, "fcol-0142", isGroupParent: false, parentId: null, tenantType: "CONCESIONARIO"));
        ctx.Tenants.Add(TenantSeed.New(TenantC, "FCOL-0900", isGroupParent: false, parentId: null, tenantType: "FLIT"));
        ctx.Users.Add(NewUser(UserAna, "ana.filtros@example.test", "Ana Filtros"));
        ctx.Users.Add(NewUser(UserLuis, "luis.otro@example.test", "Luis Otro"));
        // La auditoría tiene FK a tenants sin navegación en EF: se guardan las compañías primero.
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        ctx.TenantConfigAuditLogs.Add(NewAudit(0xA1, TenantA, "legal_name", "\"Razón vieja\"", Dia1, UserAna));
        ctx.TenantConfigAuditLogs.Add(NewAudit(0xA2, TenantA, "tax_id", "\"900123\"", Dia2, UserLuis));
        // Mismo campo y operador en OTRA compañía: nunca debe aparecer en la auditoría de A.
        ctx.TenantConfigAuditLogs.Add(NewAudit(0xB1, TenantB, "legal_name", "\"Razón vieja\"", Dia1, UserAna));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task HU13998_CodigoParcialSinMayusculas_Y_VariosTipos_SeTraducenEnPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new CompanyReadRepository(NewContext());

        var porCodigo = await repo.ListAsync(Filtro(codigo: "COL-0"), ct);
        porCodigo.Items.Select(c => c.Id).Should().BeEquivalentTo([TenantA, TenantB, TenantC]);

        var codigoYTipos = await repo.ListAsync(Filtro(codigo: "fcol-0", tipos: ["RENTING", "CONCESIONARIO"]), ct);
        codigoYTipos.Items.Select(c => c.Id).Should().BeEquivalentTo([TenantA, TenantB]);
        codigoYTipos.TotalCount.Should().Be(2);

        var codigoParcial = await repo.ListAsync(Filtro(codigo: "0042"), ct);
        codigoParcial.Items.Select(c => c.Id).Should().Equal(TenantA);
    }

    [PostgresFact]
    public async Task HU14004_OperadorPorNombreCorreoOId_Y_TextosYFechas_SeTraducenEnPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new TenantAuditLogRepository(NewContext());

        var porNombre = await repo.ListPagedAsync(TenantA, 1, 20, new TenantAuditLogFilter(Operator: "ANA FIL"), ct);
        porNombre.Items.Should().ContainSingle().Which.FieldName.Should().Be("legal_name");

        var porCorreo = await repo.ListPagedAsync(TenantA, 1, 20, new TenantAuditLogFilter(Operator: "luis.otro@"), ct);
        porCorreo.Items.Should().ContainSingle().Which.FieldName.Should().Be("tax_id");

        var porId = await repo.ListPagedAsync(TenantA, 1, 20, new TenantAuditLogFilter(Operator: UserAna.ToString()[..8].ToUpperInvariant()), ct);
        porId.Items.Should().ContainSingle().Which.FieldName.Should().Be("legal_name");

        var campoYValor = await repo.ListPagedAsync(TenantA, 1, 20, new TenantAuditLogFilter(Field: "LEGAL", OldValue: "razón"), ct);
        campoYValor.Items.Should().ContainSingle();
        campoYValor.TotalCount.Should().Be(1);

        // Día 2 completo en hora Bogotá: [2026-10-02 00:00 -05, 2026-10-03 00:00 -05).
        var dia2 = await repo.ListPagedAsync(TenantA, 1, 20, new TenantAuditLogFilter(
            ChangedFromInclusive: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-5)),
            ChangedToExclusive: new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(-5))), ct);
        dia2.Items.Should().ContainSingle().Which.FieldName.Should().Be("tax_id");
    }

    private static CompanyListFilter Filtro(string? codigo = null, IReadOnlyList<string>? tipos = null) => new()
    {
        Codigo = codigo,
        TenantTypes = tipos,
        ExcludeTransitOffices = false,
        Page = 1,
        PageSize = 50,
    };

    private static User NewUser(Guid id, string email, string name) => new()
    {
        Id = id,
        Email = email,
        DisplayName = name,
        Status = "active",
        HomeTenantId = TenantA,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TenantConfigAuditLog NewAudit(int n, Guid tenant, string field, string oldValue, DateTimeOffset at, Guid by) => new()
    {
        Id = Guid.Parse($"0199b000-0000-7000-8000-0000000ac0{n:x2}"),
        TenantId = tenant,
        EntityName = "tenant",
        FieldName = field,
        OldValue = oldValue,
        NewValue = "\"nuevo\"",
        ChangedAt = at,
        ChangedBy = by,
        Operation = "update",
        Result = "success",
        Module = "companies",
    };
}
