using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #13445 (revisión DB H1) — las marcas «Tiene prenda» / «Tiene transformación» del listado
/// deben TRADUCIRSE a SQL con Npgsql. Los tests de comportamiento corren sobre EF InMemory, que
/// evalúa cualquier expresión en memoria y no detecta un método sin traducción (p. ej.
/// <c>string.StartsWith(char)</c>, que en Npgsql 10 lanza y devolvía 500 al filtrar).
/// Uso de ejemplo:
/// <code>db.ProcedureInstances.Where(ProcedureInstanceFiltroSql.TienePrenda(db)).ToQueryString();</code>
/// No necesita base: el contexto Npgsql es solo-modelo (mismo patrón que
/// <see cref="RadicadoPrefijoFamiliaTests"/>).
/// </summary>
public sealed class ProcedureInstanceFiltroSqlTraduccionTests
{
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void TienePrenda_SeTraduceASql()
    {
        using var db = NewContext();

        var sql = db.ProcedureInstances.Where(ProcedureInstanceFiltroSql.TienePrenda(db)).ToQueryString();

        sql.Should().Contain("procedure_instance_prenda");
        sql.Should().Contain("LIKE '[%'", "el prefijo del detalle RUNT se evalúa en el servidor");
    }

    [Fact]
    public void NoTienePrenda_LaNegacionTambienSeTraduce()
    {
        using var db = NewContext();

        var sql = db.ProcedureInstances
            .Where(ProcedureInstanceFiltroSql.Negar(ProcedureInstanceFiltroSql.TienePrenda(db)))
            .ToQueryString();

        sql.Should().Contain("NOT");
    }

    [Fact]
    public void TieneTransformacion_SeTraduceASql()
    {
        using var db = NewContext();

        var act = () => db.ProcedureInstances
            .Where(ProcedureInstanceFiltroSql.TieneTransformacion).ToQueryString();

        act.Should().NotThrow();
    }
}
