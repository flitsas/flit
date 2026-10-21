using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13724 (F00.1-H3), escenarios 1 y 3 — permisos y roles por defecto del manifiesto contra la tabla §6.2 del plan
/// (D-01 = c, R-6). La tabla se copia aquí literal: si el manifiesto cambia sin cambiar el plan, la prueba falla.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PermisosTests
/// </code>
/// </example>
public sealed partial class PermisosTests
{
    /// <summary>Tabla §6.2: rol → slugs que tiene (columna «Sí»).</summary>
    private static readonly Dictionary<string, string[]> Tabla62 = new(StringComparer.Ordinal)
    {
        ["usuario_diagnostico"] =
        [
            "diagnostico.diagnosticos.crear", "diagnostico.diagnosticos.ver", "diagnostico.reportes.descargar",
            "diagnostico.derivacion.crear", "diagnostico.configuracion.ver",
        ],
        ["admin_diagnostico"] =
        [
            "diagnostico.diagnosticos.crear", "diagnostico.diagnosticos.ver", "diagnostico.reportes.descargar",
            "diagnostico.derivacion.crear", "diagnostico.configuracion.ver", "diagnostico.configuracion.editar",
            "diagnostico.consultas.reprocesar",
        ],
        ["backoffice_diagnostico"] =
        [
            "diagnostico.backoffice.ver", "diagnostico.reportes.descargar", "diagnostico.configuracion.ver",
            "diagnostico.configuracion.contrato.editar", "diagnostico.consultas.reprocesar", "diagnostico.consultas.llenar",
            "diagnostico.dictamen.override", "diagnostico.auditoria.ver", "diagnostico.volumetria.ver",
        ],
    };

    private static readonly string[] SlugsDelPlan =
    [
        "diagnostico.diagnosticos.crear", "diagnostico.diagnosticos.ver", "diagnostico.backoffice.ver",
        "diagnostico.reportes.descargar", "diagnostico.derivacion.crear", "diagnostico.configuracion.ver",
        "diagnostico.configuracion.editar", "diagnostico.configuracion.contrato.editar", "diagnostico.consultas.reprocesar",
        "diagnostico.consultas.llenar", "diagnostico.dictamen.override", "diagnostico.auditoria.ver", "diagnostico.volumetria.ver",
    ];

    private static List<string> Slugs => Permisos.Modulos.SelectMany(m => m.Permisos).Select(p => p.Slug).ToList();

    // Escenario 1 — todos los slugs empiezan por diagnostico. y siguen diagnostico.<modulo>.<accion>.
    [Fact]
    public void Todo_slug_sigue_el_formato_diagnostico_modulo_accion()
    {
        var slugs = Slugs;

        slugs.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(SlugsDelPlan, "son los 13 slugs de la tabla §6.2");
        slugs.Should().AllSatisfy(s => FormatoSlug().IsMatch(s).Should().BeTrue($"«{s}» sigue diagnostico.<modulo>.<accion>"));
        Permisos.Modulos.Should().AllSatisfy(m =>
        {
            m.Codigo.Should().StartWith("diagnostico_", "el código de módulo es global en security.modules");
            m.Nombre.Should().NotBeNullOrWhiteSpace();
            m.Permisos.Should().NotBeEmpty().And.AllSatisfy(p => p.Nombre.Should().NotBeNullOrWhiteSpace());
        });
    }

    // Escenario 3 — cada rol por defecto tiene exactamente los slugs de la tabla §6.2, todos del manifiesto.
    [Fact]
    public void Roles_por_defecto_coinciden_con_la_tabla_6_2()
    {
        var roles = Permisos.RolesPorDefecto;
        var slugs = Slugs.ToHashSet(StringComparer.Ordinal);

        roles.Select(r => r.Codigo).Should().BeEquivalentTo(Tabla62.Keys);
        roles.Should().AllSatisfy(r =>
        {
            r.Nombre.Should().NotBeNullOrWhiteSpace();
            r.Permisos.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Tabla62[r.Codigo], $"tabla §6.2, columna {r.Codigo}");
            r.Permisos.Should().AllSatisfy(p => slugs.Should().Contain(p, "ApplyProductManifestHandler rechaza un rol con slugs ajenos al manifiesto"));
        });
    }

    // Escenario 3 — R-6: el backoffice FLIT no crea diagnósticos.
    [Fact]
    public void Backoffice_no_tiene_diagnosticos_crear()
    {
        var backoffice = Permisos.RolesPorDefecto.Single(r => r.Codigo == "backoffice_diagnostico");

        backoffice.Permisos.Should().NotContain("diagnostico.diagnosticos.crear");
        backoffice.Permisos.Should().NotContain("diagnostico.derivacion.crear");
    }

    [GeneratedRegex(@"^diagnostico(\.[a-z]+){2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoSlug();
}
