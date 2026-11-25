namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Permisos del producto (plan §5.2), formato <c>impuestos.&lt;modulo&gt;.&lt;accion&gt;</c>. Viajan en el manifiesto que
/// <see cref="RegistroManifiesto"/> registra al arrancar. Dos roles de producto (F5b-CF1): <c>gestor_impuestos</c> y
/// <c>admin_impuestos</c>, con exactamente las columnas «Sí» de la tabla §5.2. Los permisos de <see cref="SoloSuperAdmin"/> van en
/// el manifiesto pero en ningún rol por defecto: el Super Admin no recibe un rol de producto y pasa <see cref="PoliticaBackoffice"/>.
/// </summary>
public static class Permisos
{
    public const string DashboardVer = "impuestos.dashboard.ver";
    public const string LiquidacionesVer = "impuestos.liquidaciones.ver";
    public const string LiquidacionesCrear = "impuestos.liquidaciones.crear";
    public const string LiquidacionesCerrar = "impuestos.liquidaciones.cerrar";
    public const string RecibosDescargar = "impuestos.recibos.descargar";
    public const string ReportesVer = "impuestos.reportes.ver";
    public const string ReportesExportar = "impuestos.reportes.exportar";
    public const string UsuariosGestionar = "impuestos.usuarios.gestionar";
    public const string ConfiguracionVer = "impuestos.configuracion.ver";
    public const string ConfiguracionEditar = "impuestos.configuracion.editar";
    public const string ConfiguracionContratoEditar = "impuestos.configuracion.contrato.editar";
    public const string BackofficeVer = "impuestos.backoffice.ver";
    public const string CredencialesCargar = "impuestos.credenciales.cargar";

    /// <summary>
    /// Módulos del manifiesto (<c>security.modules</c>) con sus permisos y el nombre que ve el administrador de roles. El código de
    /// módulo es global en la plataforma, por eso lleva el prefijo <c>impuestos_</c>.
    /// </summary>
    public static IReadOnlyList<(string Codigo, string Nombre, (string Slug, string Nombre)[] Permisos)> Modulos { get; } =
    [
        ("impuestos_liquidaciones", "Impuestos · Liquidaciones",
        [
            (DashboardVer, "Ver el dashboard de impuestos"), (LiquidacionesVer, "Ver liquidaciones y el detalle de cada placa"),
            (LiquidacionesCrear, "Crear liquidaciones individuales y masivas"), (RecibosDescargar, "Descargar los recibos de pago"),
            (LiquidacionesCerrar, "Cerrar una liquidación pendiente"),
        ]),
        ("impuestos_reportes", "Impuestos · Reportes",
        [
            (ReportesVer, "Ver los reportes de impuestos"), (ReportesExportar, "Exportar los reportes a Excel y PDF"),
        ]),
        ("impuestos_usuarios", "Impuestos · Usuarios",
        [
            (UsuariosGestionar, "Gestionar los usuarios de Impuestos de la compañía"),
        ]),
        ("impuestos_configuracion", "Impuestos · Configuración",
        [
            (ConfiguracionVer, "Ver la configuración de la compañía"), (ConfiguracionEditar, "Editar la configuración operativa"),
            (ConfiguracionContratoEditar, "Editar el nivel contractual (backoffice FLIT)"),
        ]),
        ("impuestos_backoffice", "Impuestos · Backoffice FLIT",
        [
            (BackofficeVer, "Ver las compañías y su aprovisionamiento"), (CredencialesCargar, "Cargar las credenciales del API de una compañía"),
        ]),
    ];

    private static readonly string[] Gestor = [DashboardVer, LiquidacionesVer, LiquidacionesCrear, RecibosDescargar, ReportesVer, ReportesExportar];

    /// <summary>Roles por defecto (tabla §5.2). <c>admin_impuestos</c> lo siembra la plataforma (DDL 205); el manifiesto le pone permisos.</summary>
    public static IReadOnlyList<(string Codigo, string Nombre, string[] Permisos)> RolesPorDefecto { get; } =
    [
        ("gestor_impuestos", "Gestor de Impuestos", Gestor),
        ("admin_impuestos", "Administrador de Impuestos", [.. Gestor, LiquidacionesCerrar, UsuariosGestionar, ConfiguracionVer, ConfiguracionEditar]),
    ];

    /// <summary>Permisos que solo tiene el Super Admin (columna «Solo SA»); fuera de él exigen además <c>tenant_type=FLIT</c>.</summary>
    public static IReadOnlyList<string> SoloSuperAdmin { get; } = [ConfiguracionContratoEditar, BackofficeVer, CredencialesCargar];

    /// <summary>Todos los slugs del manifiesto.</summary>
    public static IEnumerable<string> Todos => Modulos.SelectMany(m => m.Permisos).Select(p => p.Slug);
}
