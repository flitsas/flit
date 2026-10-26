using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitGrants;

/// <summary>
/// Filtros del historial de auditoría de la compañía (HU #14004): fechas como día local
/// America/Bogota inclusivo, «contiene» sin distinguir mayúsculas, aislamiento por tenant y
/// 422 con rango invertido. Handler real sobre el repositorio EF real (InMemory).
///
/// Uso de ejemplo:
/// <code>
/// var q = new GetTenantAuditLogQuery { TenantId = id, FechaDesde = new DateOnly(2026, 3, 1), Campo = "name" };
/// var page = await new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx)).HandleAsync(q);
/// </code>
/// </summary>
public sealed class TenantAuditLogFilterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Bogota = TimeSpan.FromHours(-5);
    private readonly WebApplicationFactory<Program> _factory;

    public TenantAuditLogFilterTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC1_FiltraPorRangoDeFechasComoDiaLocalBogotaInclusivo()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        await Seed(db,
            Row(tenant, "a", new DateTimeOffset(2026, 3, 9, 23, 59, 0, Bogota)),   // antes del rango
            Row(tenant, "b", new DateTimeOffset(2026, 3, 10, 0, 0, 0, Bogota)),    // inicio exacto
            Row(tenant, "c", new DateTimeOffset(2026, 3, 11, 23, 59, 59, Bogota)), // último segundo del día hasta
            Row(tenant, "d", new DateTimeOffset(2026, 3, 12, 0, 0, 0, Bogota)));   // fuera

        var result = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            FechaDesde = new DateOnly(2026, 3, 10),
            FechaHasta = new DateOnly(2026, 3, 11),
        });

        result.TotalCount.Should().Be(2);
        result.Data.Select(e => e.NewValue).Should().BeEquivalentTo("b", "c");
    }

    [Fact]
    public async Task AC1_FechaUsaDiaBogotaNoUtc()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        // 2026-03-11 02:00 UTC = 2026-03-10 21:00 Bogotá -> pertenece al día 10.
        await Seed(db, Row(tenant, "x", new DateTimeOffset(2026, 3, 11, 2, 0, 0, TimeSpan.Zero)));

        var dia10 = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            FechaDesde = new DateOnly(2026, 3, 10),
            FechaHasta = new DateOnly(2026, 3, 10),
        });
        var dia11 = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            FechaDesde = new DateOnly(2026, 3, 11),
            FechaHasta = new DateOnly(2026, 3, 11),
        });

        dia10.TotalCount.Should().Be(1);
        dia11.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task AC1_TextosContienenSinDistinguirMayusculas_Y_CombinanConAnd()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        var user = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await Seed(db,
            Row(tenant, "n1", now, field: "legal_name", oldValue: "ACME Motors", changedBy: user),
            Row(tenant, "n2", now, field: "legal_name", oldValue: "Otra", changedBy: user),
            Row(tenant, "n3", now, field: "is_active", oldValue: "ACME Motors", changedBy: user));

        var soloCampo = await Run(db, new GetTenantAuditLogQuery { TenantId = tenant, Campo = "LEGAL" });
        soloCampo.Data.Select(e => e.NewValue).Should().BeEquivalentTo("n1", "n2");

        var soloValor = await Run(db, new GetTenantAuditLogQuery { TenantId = tenant, ValorAnterior = "acme" });
        soloValor.Data.Select(e => e.NewValue).Should().BeEquivalentTo("n1", "n3");

        var ambos = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            Campo = "legal",
            ValorAnterior = "ACME",
        });
        ambos.Data.Should().ContainSingle().Which.NewValue.Should().Be("n1");
        ambos.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task AC1_OperadorBuscaPorIdNombreOCorreo_SinDistinguirMayusculas()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();
        await using (var ctx = NewContext(db))
        {
            ctx.Users.Add(NewUser(ana, "Ana Pérez", "ana@flit.test"));
            ctx.Users.Add(NewUser(luis, "Luis Gómez", "luis@otro.test"));
            ctx.TenantConfigAuditLogs.Add(Row(tenant, "porAna", DateTimeOffset.UtcNow, changedBy: ana));
            ctx.TenantConfigAuditLogs.Add(Row(tenant, "porLuis", DateTimeOffset.UtcNow, changedBy: luis));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await Run(db, new GetTenantAuditLogQuery { TenantId = tenant, Operador = "ANA PÉ" }))
            .Data.Should().ContainSingle().Which.NewValue.Should().Be("porAna");
        (await Run(db, new GetTenantAuditLogQuery { TenantId = tenant, Operador = "@OTRO.test" }))
            .Data.Should().ContainSingle().Which.NewValue.Should().Be("porLuis");
        (await Run(db, new GetTenantAuditLogQuery { TenantId = tenant, Operador = luis.ToString()[..8].ToUpperInvariant() }))
            .Data.Should().ContainSingle().Which.NewValue.Should().Be("porLuis");
    }

    [Fact]
    public async Task AC1_TotalCountYPaginacionSeCalculanSobreElResultadoFiltrado()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(0, 7).Select(i => Row(tenant, $"m{i}", now.AddMinutes(i), field: "match"))
            .Concat(Enumerable.Range(0, 5).Select(i => Row(tenant, $"o{i}", now, field: "other")))
            .ToArray();
        await Seed(db, rows);

        var page2 = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            Campo = "match",
            Page = 2,
            PageSize = 5,
        });

        page2.TotalCount.Should().Be(7);
        page2.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task AC1_SinFiltros_DevuelveTodo_Y_FiltrosEnBlancoSeIgnoran()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        await Seed(db, Row(tenant, "a", DateTimeOffset.UtcNow), Row(tenant, "b", DateTimeOffset.UtcNow));

        var result = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            Campo = "  ",
            ValorAnterior = "",
            Operador = " ",
        });

        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task AC2_FechaDesdePosteriorAFechaHasta_LanzaValidacion()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx));

        var act = () => handler.HandleAsync(new GetTenantAuditLogQuery
        {
            TenantId = Guid.NewGuid(),
            FechaDesde = new DateOnly(2026, 3, 12),
            FechaHasta = new DateOnly(2026, 3, 11),
        }, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<InvalidAuditLogFilterException>()).Which;
        ex.Errors.Should().ContainSingle().Which.Field.Should().Be("fechaDesde");
        ex.Message.Should().Be(GetTenantAuditLogHandler.InvalidDateRangeMessage);
    }

    [Fact]
    public async Task AC2_MismoDia_NoEsInvalido()
    {
        var (db, tenant) = (NewDbName(), Guid.NewGuid());
        var result = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = tenant,
            FechaDesde = new DateOnly(2026, 3, 11),
            FechaHasta = new DateOnly(2026, 3, 11),
        });

        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2_Endpoint_RangoInvertido_Responde422ConMensaje()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(
            $"/api/v1/admin/companies/{Guid.NewGuid()}/audit-log?fechaDesde=2026-03-12&fechaHasta=2026-03-11",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("fechaDesde").And.Contain(GetTenantAuditLogHandler.InvalidDateRangeMessage);
    }

    [Fact]
    public async Task AC3_FiltrosQueCoincidenConOtraCompania_SoloDevuelvenLaConsultada()
    {
        var (db, mine, other) = (NewDbName(), Guid.NewGuid(), Guid.NewGuid());
        var user = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 3, 10, 12, 0, 0, Bogota);
        await Seed(db,
            Row(mine, "mio", when, field: "legal_name", oldValue: "ACME", changedBy: user),
            Row(other, "ajeno", when, field: "legal_name", oldValue: "ACME", changedBy: user));

        var result = await Run(db, new GetTenantAuditLogQuery
        {
            TenantId = mine,
            FechaDesde = new DateOnly(2026, 3, 10),
            FechaHasta = new DateOnly(2026, 3, 10),
            Campo = "legal",
            ValorAnterior = "acme",
            Operador = user.ToString(),
        });

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle().Which.NewValue.Should().Be("mio");
    }

    [Fact]
    public async Task AC3_Autorizacion_SeMantiene_SinTokenYNoSuperAdmin()
    {
        var url = $"/api/v1/admin/companies/{Guid.NewGuid()}/audit-log?campo=x";

        var anon = await _factory.CreateClient().GetAsync(url, TestContext.Current.CancellationToken);
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));
        var forbidden = await client.GetAsync(url, TestContext.Current.CancellationToken);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<GetTenantAuditLogResult> Run(string db, GetTenantAuditLogQuery query)
    {
        await using var ctx = NewContext(db);
        return await new GetTenantAuditLogHandler(new TenantAuditLogRepository(ctx))
            .HandleAsync(query, TestContext.Current.CancellationToken);
    }

    private static async Task Seed(string db, params TenantConfigAuditLog[] rows)
    {
        await using var ctx = NewContext(db);
        ctx.TenantConfigAuditLogs.AddRange(rows);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static TenantConfigAuditLog Row(
        Guid tenant,
        string newValue,
        DateTimeOffset changedAt,
        string field = "field",
        string? oldValue = null,
        Guid? changedBy = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            EntityName = "tenant_settings",
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = changedAt,
            ChangedBy = changedBy,
        };

    private static Flit.Infrastructure.Persistence.Entities.Identity.User NewUser(
        Guid id, string name, string email) =>
        new() { Id = id, DisplayName = name, Email = email };

    private static string NewDbName() => $"flit-audit-filter-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
