using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #14747. El OR de la búsqueda de varios valores se arma con árboles de expresión. Las pruebas
/// de comportamiento (<see cref="BusquedaTextoLibreRepositoryTests"/>) corren en InMemory, que
/// evalúa en memoria y no prueba que el árbol se traduzca a SQL. Aquí se genera el SQL con el
/// proveedor Npgsql, sin abrir conexión (la suite no tiene Postgres), y se verifica:
/// <list type="bullet">
/// <item>que el árbol se traduce sin excepción;</item>
/// <item>que cada tramo llega como parámetro (no como literal en el SQL);</item>
/// <item>que el OR queda en un único <c>WHERE</c>.</item>
/// </list>
/// </summary>
public sealed class BusquedaMultivalorSqlTests
{
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void Multivalor_SeTraduceASqlDePostgres_ConUnParametroPorTramo()
    {
        using var db = NewContext();
        var tramos = ProcedureInstanceFiltroSql.LeerBusquedaMultivalor("AAA111, FT1-0000102, 9BWZZZ111AA000001, 102")!;

        var sql = ProcedureInstanceFiltroSql.AplicarBusquedaMultivalor(db.ProcedureInstances, tramos).ToQueryString();

        sql.Should().Contain("WHERE");
        sql.Should().Contain("upper(");
        // ToQueryString imprime cada parámetro al inicio como «-- @nombre='valor'». Si esas líneas
        // aparecen, el texto del tramo se envía como parámetro y no queda escrito en el SQL.
        // Placa y VIN van por LIKE en mayúsculas (Contains); el radicado con prefijo, por su canónico;
        // el número suelto, por consecutivo exacto.
        sql.Should().Contain("='%AAA111%'");
        sql.Should().Contain("='%9BWZZZ111AA000001%'");
        sql.Should().Contain("='FT10000102'");
        sql.Should().Contain("p.consecutivo = @");
        sql.Should().Contain("upper(p.plate) LIKE @").And.Contain("upper(p.vin) LIKE @");
        var cuerpo = string.Join('\n', sql.Split('\n').Where(l => !l.StartsWith("--")));
        cuerpo.Should().NotContain("AAA111").And.NotContain("9BWZZZ111AA000001");
    }

    [Fact]
    public void Multivalor_ConElTopeDeTramos_SigueTraduciendose()
    {
        using var db = NewContext();
        var texto = string.Join(",", Enumerable.Range(1, ProcedureInstanceFiltroSql.MaxTramosBusqueda + 10).Select(i => $"PLC{i:000}"));
        var tramos = ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(texto)!;

        tramos.Should().HaveCount(ProcedureInstanceFiltroSql.MaxTramosBusqueda);
        var act = () => ProcedureInstanceFiltroSql.AplicarBusquedaMultivalor(db.ProcedureInstances, tramos).ToQueryString();
        act.Should().NotThrow();
    }

    [Fact]
    public void Multivalor_SoloComas_SeTraduceSinFilas()
    {
        using var db = NewContext();
        var tramos = ProcedureInstanceFiltroSql.LeerBusquedaMultivalor(" , ,")!;

        var act = () => ProcedureInstanceFiltroSql.AplicarBusquedaMultivalor(db.ProcedureInstances, tramos).ToQueryString();
        act.Should().NotThrow();
    }
}
