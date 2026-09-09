using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12182 — el predicado de la marca de prenda del listado
/// (<see cref="ProcedureInstanceRepository.ListInstanceIdsConPrendaVigenteAsync"/>).
///
/// <para>Lo que se fija aquí son las dos exclusiones, que es donde está el error fácil: mirar
/// también las filas <c>reemplazada</c> diría «tiene prenda» de un trámite al que se le quitó, y
/// contar <c>omitir</c>/<c>sin_prenda</c> marcaría precisamente los que decidieron no tenerla.</para>
/// </summary>
public sealed class PrendaVigenteDelListadoRepositoryTests
{
    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstancePrenda Fila(Guid instanceId, string decision, string estado) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureInstanceId = instanceId,
        Decision = decision,
        Estado = estado,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData(PrendaDecision.Solicitar)]
    [InlineData(PrendaDecision.Registrar)]
    [InlineData(PrendaDecision.Levantar)]
    public async Task DecisionVigenteQueEsUnHechoDeGravamen_Marca(string decision)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var db = NewContext($"prenda-marca-{decision}");
        db.ProcedureInstancePrendas.Add(Fila(id, decision, PrendaEstado.Vigente));
        await db.SaveChangesAsync(ct);

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([id], ct);

        conPrenda.Should().Contain(id);
    }

    [Theory]
    [InlineData(PrendaDecision.Omitir)]
    [InlineData(PrendaDecision.SinPrenda)]
    public async Task DecidirNoTenerla_NoMarca(string decision)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var db = NewContext($"prenda-no-{decision}");
        db.ProcedureInstancePrendas.Add(Fila(id, decision, PrendaEstado.Vigente));
        await db.SaveChangesAsync(ct);

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([id], ct);

        conPrenda.Should().BeEmpty();
    }

    [Fact]
    public async Task PrendaReemplazada_NoMarca()
    {
        // Se registró un gravamen y después se quitó: la fila vieja sigue en la tabla como
        // `reemplazada`. Mirarla diría que el trámite tiene prenda cuando ya no la tiene.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var db = NewContext("prenda-reemplazada");
        db.ProcedureInstancePrendas.Add(Fila(id, PrendaDecision.Registrar, PrendaEstado.Reemplazada));
        db.ProcedureInstancePrendas.Add(Fila(id, PrendaDecision.SinPrenda, PrendaEstado.Vigente));
        await db.SaveChangesAsync(ct);

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([id], ct);

        conPrenda.Should().BeEmpty();
    }

    [Fact]
    public async Task DosVigentesEnLaMismaInstancia_DevuelveElIdUnaSolaVez()
    {
        // ADR-0055: constitución y levantamiento pueden estar vigentes a la vez.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var db = NewContext("prenda-dual");
        db.ProcedureInstancePrendas.Add(Fila(id, PrendaDecision.Registrar, PrendaEstado.Vigente));
        db.ProcedureInstancePrendas.Add(Fila(id, PrendaDecision.Levantar, PrendaEstado.Vigente));
        await db.SaveChangesAsync(ct);

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([id], ct);

        conPrenda.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task SoloDevuelveLosIdsPreguntados()
    {
        var ct = TestContext.Current.CancellationToken;
        var preguntado = Guid.NewGuid();
        var ajeno = Guid.NewGuid();
        await using var db = NewContext("prenda-acotada");
        db.ProcedureInstancePrendas.Add(Fila(preguntado, PrendaDecision.Registrar, PrendaEstado.Vigente));
        db.ProcedureInstancePrendas.Add(Fila(ajeno, PrendaDecision.Registrar, PrendaEstado.Vigente));
        await db.SaveChangesAsync(ct);

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([preguntado], ct);

        conPrenda.Should().ContainSingle().Which.Should().Be(preguntado);
    }

    [Fact]
    public async Task SinIds_NoConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext("prenda-vacia");

        var conPrenda = await new ProcedureInstanceRepository(db)
            .ListInstanceIdsConPrendaVigenteAsync([], ct);

        conPrenda.Should().BeEmpty();
    }
}
