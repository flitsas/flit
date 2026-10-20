using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13231 (Epic #13217) — core-identity lee y escribe con <see cref="IdentityDbContext"/>, que no tiene migraciones;
/// core-api migra con <see cref="FlitDbContext"/>. Esta prueba asegura que, para cada tabla y vista de identidad, los dos
/// modelos coinciden columna por columna (nombre, tipo, nulabilidad) y en la llave primaria. Sin base de datos.
/// </summary>
public sealed class IdentityModelParityTests
{
    private const string AnyConnection = "Host=localhost;Database=parity";

    [Fact]
    public void CadaTablaDeIdentidad_EsIgualEnLosDosContextos()
    {
        using var flit = new FlitDbContext(Options<FlitDbContext>());
        using var identity = new IdentityDbContext(Options<IdentityDbContext>());

        var flitTables = Tables(flit);
        var identityTables = Tables(identity);

        identityTables.Keys.Should().Contain(
            ["identity.users", "identity.tenants", "security.roles", "security.user_role_assignments", "platform.tenant_products",
             "security.user_credentials", "identity.oidc_authorizations", "admin.v_active_network_domains"]);
        // HU #13355: la outbox del SDK es de cada servicio (en core-api, OutboxMessage ya es tramites.outbox); core-api crea
        // identity.outbox con una migración sin modelo, y la prueba de integración comprueba que exista.
        foreach (var (table, shape) in identityTables.Where(t => t.Key != "identity.outbox"))
        {
            flitTables.Should().ContainKey(table, "core-api migra todas las tablas que usa core-identity");
            shape.Should().BeEquivalentTo(flitTables[table], $"{table} tiene que ser igual en los dos contextos");
        }
    }

    [Fact]
    public void ElContextoDeIdentidad_NoConoceTablasDeNegocio()
    {
        using var identity = new IdentityDbContext(Options<IdentityDbContext>());

        Tables(identity).Keys.Should().NotContain(t => t.StartsWith("tramites.", StringComparison.Ordinal)
            || t.StartsWith("analytics.", StringComparison.Ordinal) || t.StartsWith("catalogs.", StringComparison.Ordinal));
    }

    private static DbContextOptions<T> Options<T>()
        where T : DbContext
    {
        var builder = new DbContextOptionsBuilder<T>();
        NpgsqlConventions.Apply(builder, AnyConnection);
        return builder.Options;
    }

    private static Dictionary<string, string[]> Tables(DbContext db)
    {
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        IEnumerable<ITableBase> all = [.. model.Tables, .. model.Views];
        return all
            .GroupBy(t => $"{t.Schema}.{t.Name}")
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(t => t.Columns.Select(c => $"{c.Name}:{c.StoreType}:{(c.IsNullable ? "null" : "not null")}"))
                    .Concat(g.OfType<ITable>().Select(t => $"pk:{string.Join(",", t.PrimaryKey?.Columns.Select(c => c.Name) ?? [])}"))
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .ToArray());
    }
}
