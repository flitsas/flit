using Flit.Admin.Application.Improntas.ListImprontas;
using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Tests del listado paginado/filtrable del historial de improntas (HU #10468 / ADR-0022).
/// Proveedor InMemory, mismo patrón que <c>ListTransitOfficeTenantsHandlerTests</c>: handler +
/// repositorio real contra un contexto EF Core InMemory. Nombrados <c>AC&lt;n&gt;_...</c> según el
/// acceptance criteria que cubren.
/// </summary>
public sealed class ListImprontasHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FlitUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task AC1_ConImprontasExistentes_RespondeListadoPaginadoOrdenadoPorFechaDescendente()
    {
        var db = NewDbName();
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow.AddDays(-1);

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000001", createdAt: older),
                TestContext.Current.CancellationToken);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000002", createdAt: newer),
                TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery { Page = 1, PageSize = 20 },
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.Data.Should().HaveCount(2);
        // Más reciente primero.
        result.Data[0].Radicado.Should().Be("IMPR-00000002");
        result.Data[1].Radicado.Should().Be("IMPR-00000001");
    }

    [Fact]
    public async Task AC1_ProyectaMetadataYNuncaElBinarioDelPdf()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000010"), TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery(), TestContext.Current.CancellationToken);

        var item = result.Data.Should().ContainSingle().Subject;
        // ImprontaGenerationListItem no declara PdfContent — este assert documenta la
        // disciplina de proyección exigida por ADR-0022 (nunca arrastrar el binario).
        typeof(ImprontaGenerationListItem).GetProperty("PdfContent").Should().BeNull();
        item.PdfSizeBytes.Should().Be(4);
        item.Radicado.Should().Be("IMPR-00000010");
    }

    [Fact]
    public async Task AC3_FiltrosSinResultados_Responde200ConItemsVacioYTotalEnCero()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000020", placa: "ABC123"),
                TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery { Placa = "ZZZ999" }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_PageSizeFueraDeRango_LoAcotaAlMaximoConfiguradoEnLugarDeConsultaIlimitada()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            for (var i = 0; i < 5; i++)
            {
                await repository.SaveAsync(
                    BuildValidGeneration(radicado: $"IMPR-0000010{i}"), TestContext.Current.CancellationToken);
            }
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery { PageSize = 1_000_000 }, TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(ListImprontasHandler.MaxPageSize);
    }

    [Fact]
    public async Task FiltroPorPlaca_CoincidenciaParcialInsensibleAMayusculas_DevuelveSoloCoincidencias()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000030", placa: "ABC123"),
                TestContext.Current.CancellationToken);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000031", placa: "XYZ999"),
                TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery { Placa = "abc" }, TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle().Which.Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task FiltroPorRangoDeFecha_ExcluyeGeneracionesFueraDelRango()
    {
        var db = NewDbName();
        var dentro = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var fuera = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        await using (var seed = NewContext(db))
        {
            var repository = new ImprontaRepository(seed);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000040", createdAt: dentro),
                TestContext.Current.CancellationToken);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000041", createdAt: fuera),
                TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListImprontasHandler(new ImprontaRepository(ctx));

        var result = await handler.HandleAsync(
            new ListImprontasQuery
            {
                CreatedFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                CreatedTo = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            },
            TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle().Which.Radicado.Should().Be("IMPR-00000040");
    }

    // ---------- Helpers ----------

    private static ImprontaGeneration BuildValidGeneration(
        string radicado,
        string placa = "ABC123",
        DateTimeOffset? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Radicado = radicado,
            HashSha256 = new string('a', 64),
            FechaImpresa = DateTimeOffset.UtcNow,
            Placa = placa,
            NumMotor = "MTR-123",
            OrgNombre = "FLIT SAS",
            OrgNit = "900000000-1",
            OrgCiudad = "Bogotá",
            Operador = "Operador X",
            PdfContent = [0x25, 0x50, 0x44, 0x46],
            PdfSizeBytes = 4,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    private static string NewDbName() => $"flit-list-improntas-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
