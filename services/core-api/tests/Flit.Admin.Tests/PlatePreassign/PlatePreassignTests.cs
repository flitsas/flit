using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// Inventario de preasignación de placa (HU #10650, Feature #10587): ciclo de vida de la placa
/// (máquina de estados), reglas del rango y repositorio (creación con explosión + listados).
/// </summary>
public sealed class PlatePreassignTests
{
    // ---------- Ciclo de vida de la placa ----------

    [Theory]
    [InlineData("disponible", "preasignada")]
    [InlineData("disponible", "bloqueada")]
    [InlineData("preasignada", "utilizada")]
    [InlineData("preasignada", "revocada")]
    [InlineData("bloqueada", "disponible")]
    [InlineData("revocada", "disponible")]
    public void PlateStateMachine_TransicionesValidas(string from, string to)
    {
        PlateStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData("disponible", "utilizada")]
    [InlineData("utilizada", "disponible")]
    [InlineData("preasignada", "bloqueada")]
    [InlineData("bloqueada", "preasignada")]
    public void PlateStateMachine_TransicionesInvalidas(string from, string to)
    {
        PlateStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void PlateStateMachine_UtilizadaEsTerminal()
    {
        PlateStateMachine.TransitionsFrom(PlateState.Utilizada).Should().BeEmpty();
        PlateState.Todos.Should().HaveCount(5);
        PlateState.EsValido("disponible").Should().BeTrue();
        PlateState.EsValido("desconocido").Should().BeFalse();
    }

    // ---------- Reglas del rango ----------

    [Fact]
    public void PlateRangeRules_ValidaPrefijoYRango()
    {
        PlateRangeRules.Validate("ABC", 100, 200).Should().BeNull();
        PlateRangeRules.Validate("AB", 100, 200).Should().NotBeNull();       // prefijo corto
        PlateRangeRules.Validate("ABC", 200, 100).Should().NotBeNull();      // from > to
        PlateRangeRules.Validate("ABC", -1, 200).Should().NotBeNull();       // fuera de rango
        PlateRangeRules.Validate("ABC", 0, 999).Should().BeNull();           // límite exacto (1000)
        PlateRangeRules.Validate("abc", 100, 200).Should().NotBeNull();      // minúsculas
    }

    [Fact]
    public void PlateRangeRules_FormateaYExplota()
    {
        PlateRangeRules.Format("ABC", 7).Should().Be("ABC007");
        var plates = PlateRangeRules.Enumerate("ABC", 100, 102).ToList();
        plates.Should().Equal("ABC100", "ABC101", "ABC102");
    }

    // ---------- Repositorio ----------

    [Fact]
    public async Task CreateRange_ExplotaEnPlacasDisponibles()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var repo = new PlateRangeRepository(act);
            var result = await repo.CreateRangeAsync(company, office, "ABC", 100, 109, null, TestContext.Current.CancellationToken);
            result.Success.Should().BeTrue();
            result.PlatesCreated.Should().Be(10);
        }

        await using var verify = NewContext(db);
        var repo2 = new PlateRangeRepository(verify);
        var details = await repo2.ListDetailsAsync(company, office, null, TestContext.Current.CancellationToken);
        details.Should().HaveCount(10);
        details.Should().OnlyContain(d => d.State == PlateState.Disponible);

        var ranges = await repo2.ListRangesAsync(company, null, TestContext.Current.CancellationToken);
        ranges.Should().ContainSingle();
        ranges[0].TotalPlates.Should().Be(10);
        ranges[0].AvailablePlates.Should().Be(10);
    }

    [Fact]
    public async Task CreateRange_RechazaRangoInvalido()
    {
        await using var ctx = NewContext(NewDbName());
        var repo = new PlateRangeRepository(ctx);
        var result = await repo.CreateRangeAsync(Guid.NewGuid(), Guid.NewGuid(), "AB", 100, 200, null, TestContext.Current.CancellationToken);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.PlatesCreated.Should().Be(0);
    }

    [Fact]
    public async Task CreateRange_RechazaSolapamientoEnElMismoOT()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            (await repo.CreateRangeAsync(company, office, "ABC", 100, 110, null, TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        // Se solapa en ABC105–ABC110 para el mismo OT.
        var overlap = await repo2.CreateRangeAsync(company, office, "ABC", 105, 115, null, TestContext.Current.CancellationToken);
        overlap.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ListDetails_FiltraPorEstado()
    {
        var db = NewDbName();
        var company = Guid.NewGuid();
        var office = Guid.NewGuid();

        await using (var a = NewContext(db))
        {
            var repo = new PlateRangeRepository(a);
            await repo.CreateRangeAsync(company, office, "XYZ", 500, 502, null, TestContext.Current.CancellationToken);
        }

        await using var b = NewContext(db);
        var repo2 = new PlateRangeRepository(b);
        var disponibles = await repo2.ListDetailsAsync(company, office, PlateState.Disponible, TestContext.Current.CancellationToken);
        disponibles.Should().HaveCount(3);
        var utilizadas = await repo2.ListDetailsAsync(company, office, PlateState.Utilizada, TestContext.Current.CancellationToken);
        utilizadas.Should().BeEmpty();
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-plate-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
