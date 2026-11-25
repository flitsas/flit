using System.Text.RegularExpressions;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M05, F5b-CF1) — permisos, módulos y roles por defecto del manifiesto contra la tabla §5.2 del plan, copiada aquí
/// literal: si el manifiesto cambia sin cambiar el plan, la prueba falla. Dos roles de producto (<c>gestor_impuestos</c> y
/// <c>admin_impuestos</c>); los permisos «solo SA» no van en ningún rol por defecto.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~PermisosTests
/// </code>
/// </example>
public sealed partial class PermisosTests
{
    /// <summary>Tabla §5.2: módulo → slugs.</summary>
    private static readonly Dictionary<string, string[]> ModulosDelPlan = new(StringComparer.Ordinal)
    {
        ["impuestos_liquidaciones"] =
        [
            "impuestos.dashboard.ver", "impuestos.liquidaciones.ver", "impuestos.liquidaciones.crear", "impuestos.recibos.descargar",
            "impuestos.liquidaciones.cerrar",
        ],
        ["impuestos_reportes"] = ["impuestos.reportes.ver", "impuestos.reportes.exportar"],
        ["impuestos_usuarios"] = ["impuestos.usuarios.gestionar"],
        ["impuestos_configuracion"] = ["impuestos.configuracion.ver", "impuestos.configuracion.editar", "impuestos.configuracion.contrato.editar"],
        ["impuestos_backoffice"] = ["impuestos.backoffice.ver", "impuestos.credenciales.cargar"],
    };

    private static readonly string[] Gestor =
    [
        "impuestos.dashboard.ver", "impuestos.liquidaciones.ver", "impuestos.liquidaciones.crear", "impuestos.recibos.descargar",
        "impuestos.reportes.ver", "impuestos.reportes.exportar",
    ];

    private static readonly string[] Admin =
    [
        .. Gestor, "impuestos.liquidaciones.cerrar", "impuestos.usuarios.gestionar", "impuestos.configuracion.ver", "impuestos.configuracion.editar",
    ];

    private static readonly string[] SoloSuperAdmin =
    [
        "impuestos.configuracion.contrato.editar", "impuestos.backoffice.ver", "impuestos.credenciales.cargar",
    ];

    [Fact]
    public void Modulos_y_slugs_coinciden_con_la_tabla_5_2()
    {
        Permisos.Modulos.Select(m => m.Codigo).Should().BeEquivalentTo(ModulosDelPlan.Keys);
        Permisos.Modulos.Should().AllSatisfy(m =>
        {
            m.Permisos.Select(p => p.Slug).Should().BeEquivalentTo(ModulosDelPlan[m.Codigo], m.Codigo);
            m.Nombre.Should().StartWith("Impuestos · ");
            m.Permisos.Should().AllSatisfy(p => p.Nombre.Should().NotBeNullOrWhiteSpace());
        });
        Permisos.Todos.Should().HaveCount(13).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Todo_slug_sigue_impuestos_modulo_accion_y_todo_modulo_lleva_el_prefijo()
    {
        Permisos.Todos.Should().AllSatisfy(s => FormatoSlug().IsMatch(s).Should().BeTrue($"«{s}» sigue impuestos.<modulo>.<accion>"));
        Permisos.Modulos.Should().AllSatisfy(m => m.Codigo.Should().StartWith("impuestos_", "el código de módulo es global en security.modules"));
    }

    [Fact]
    public void Roles_por_defecto_son_gestor_y_admin_con_las_columnas_si()
    {
        var roles = Permisos.RolesPorDefecto.ToDictionary(r => r.Codigo, r => r.Permisos, StringComparer.Ordinal);

        roles.Keys.Should().BeEquivalentTo(["gestor_impuestos", "admin_impuestos"]);
        roles["gestor_impuestos"].Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Gestor);
        roles["admin_impuestos"].Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Admin);
        Permisos.RolesPorDefecto.Should().AllSatisfy(r =>
        {
            r.Nombre.Should().NotBeNullOrWhiteSpace();
            r.Permisos.Should().AllSatisfy(p => Permisos.Todos.Should().Contain(p, "la plataforma rechaza un rol con slugs ajenos al manifiesto"));
        });
    }

    [Fact]
    public void Los_permisos_solo_SA_no_van_en_ningun_rol_por_defecto()
    {
        Permisos.SoloSuperAdmin.Should().BeEquivalentTo(SoloSuperAdmin);
        Permisos.RolesPorDefecto.Should().AllSatisfy(r => r.Permisos.Should().NotContain(SoloSuperAdmin, r.Codigo));
    }

    [Fact]
    public void Las_constantes_son_los_slugs_del_plan()
    {
        Permisos.DashboardVer.Should().Be("impuestos.dashboard.ver");
        Permisos.LiquidacionesVer.Should().Be("impuestos.liquidaciones.ver");
        Permisos.LiquidacionesCrear.Should().Be("impuestos.liquidaciones.crear");
        Permisos.LiquidacionesCerrar.Should().Be("impuestos.liquidaciones.cerrar");
        Permisos.RecibosDescargar.Should().Be("impuestos.recibos.descargar");
        Permisos.ReportesVer.Should().Be("impuestos.reportes.ver");
        Permisos.ReportesExportar.Should().Be("impuestos.reportes.exportar");
        Permisos.UsuariosGestionar.Should().Be("impuestos.usuarios.gestionar");
        Permisos.ConfiguracionVer.Should().Be("impuestos.configuracion.ver");
        Permisos.ConfiguracionEditar.Should().Be("impuestos.configuracion.editar");
        Permisos.ConfiguracionContratoEditar.Should().Be("impuestos.configuracion.contrato.editar");
        Permisos.BackofficeVer.Should().Be("impuestos.backoffice.ver");
        Permisos.CredencialesCargar.Should().Be("impuestos.credenciales.cargar");
    }

    [GeneratedRegex(@"^impuestos(\.[a-z]+){2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoSlug();
}
