using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13727 (F03-H2), ADR a + ADR-0072 — prueba de arquitectura (grep auditable, como
/// <see cref="IgnoreQueryFiltersSoloEnBackofficeTests"/>): la toma de la cola es la única lectura y escritura del servicio que
/// cruza empresas sin el filtro de EF, y lo hace con SQL crudo (<c>FOR UPDATE SKIP LOCKED</c>). El SQL crudo
/// (<c>GetDbConnection</c>, <c>SqlQuery</c>, <c>ExecuteSql</c>, <c>FromSql</c>, <c>NpgsqlCommand</c>) solo puede estar en
/// <c>Modulos/Trabajos/ColaConsultas.cs</c>; todo lo demás pasa por EF con <c>TenantFilter</c>. Las migraciones quedan fuera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TomaEntreEmpresasSoloEnColaTests
/// </code>
/// </example>
public sealed partial class TomaEntreEmpresasSoloEnColaTests
{
    private const string ArchivoPermitido = "Modulos/Trabajos/ColaConsultas.cs";

    [Fact]
    public void Solo_la_cola_usa_SQL_crudo_entre_empresas()
    {
        var src = Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src");

        var usos = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(src, SqlCrudo()).Where(u => !u.Archivo.Contains("/Migrations/", StringComparison.Ordinal)).ToList();

        usos.Should().Contain(u => u.Archivo.EndsWith(ArchivoPermitido, StringComparison.Ordinal),
            "la toma de la cola usa SQL crudo: si no hay usos ahí, el escáner no mira donde debe");
        usos.Where(u => !u.Archivo.EndsWith(ArchivoPermitido, StringComparison.Ordinal)).Select(u => $"{u.Archivo}:{u.Linea}")
            .Should().BeEmpty("el SQL crudo salta el filtro por empresa: solo la toma de la cola lo usa (ADR a, ADR-0072)");
    }

    [Fact]
    public void El_escaner_detecta_SQL_crudo_fuera_de_la_cola()
    {
        var raiz = Directory.CreateTempSubdirectory("dgn-arq-sql-").FullName;
        try
        {
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Modulos/Diagnosticos/Lista.cs", "await db.Database\n    .ExecuteSqlRawAsync(sql);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Modulos/Trabajos/ColaConsultas.cs", "var conexion = db.Database.GetDbConnection();");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Modulos/Comun/Doc.cs", "// db.Database.SqlQuery<long>(x);");

            var usos = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(raiz, SqlCrudo());

            usos.Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["Modulos/Diagnosticos/Lista.cs:2", "Modulos/Trabajos/ColaConsultas.cs:1"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    [GeneratedRegex(@"\b(GetDbConnection|SqlQuery(Raw)?|ExecuteSql(Raw|Interpolated)?(Async)?|FromSql(Raw|Interpolated)?|NpgsqlCommand)\b")]
    private static partial Regex SqlCrudo();
}
