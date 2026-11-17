namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Permisos del producto (plan §6.2, D-01 = c, HU #13724), formato <c>diagnostico.&lt;modulo&gt;.&lt;accion&gt;</c>. Viajan
/// en el manifiesto que <see cref="RegistroManifiesto"/> registra al arrancar; los roles por defecto llevan exactamente
/// las columnas «Sí» de la tabla §6.2.
/// </summary>
public static class Permisos
{
    public const string DiagnosticosCrear = "diagnostico.diagnosticos.crear";
    public const string DiagnosticosVer = "diagnostico.diagnosticos.ver";
    public const string BackofficeVer = "diagnostico.backoffice.ver";
    public const string ReportesDescargar = "diagnostico.reportes.descargar";
    public const string DerivacionCrear = "diagnostico.derivacion.crear";
    public const string ConfiguracionVer = "diagnostico.configuracion.ver";
    public const string ConfiguracionEditar = "diagnostico.configuracion.editar";
    public const string ConfiguracionContratoEditar = "diagnostico.configuracion.contrato.editar";
    public const string ConsultasReprocesar = "diagnostico.consultas.reprocesar";
    public const string ConsultasLlenar = "diagnostico.consultas.llenar";
    public const string DictamenOverride = "diagnostico.dictamen.override";
    public const string AuditoriaVer = "diagnostico.auditoria.ver";
    public const string VolumetriaVer = "diagnostico.volumetria.ver";

    /// <summary>
    /// Módulos del manifiesto con sus permisos y el nombre que ve el administrador de roles. El código de módulo es global
    /// en la plataforma (<c>security.modules</c>), por eso lleva el prefijo <c>diagnostico_</c>.
    /// </summary>
    public static IReadOnlyList<(string Codigo, string Nombre, (string Slug, string Nombre)[] Permisos)> Modulos { get; } =
    [
        ("diagnostico_diagnosticos", "Diagnóstico · Diagnósticos",
        [
            (DiagnosticosCrear, "Crear diagnósticos y cargar vehículos"), (DiagnosticosVer, "Ver diagnósticos y el detalle de cada celda"),
            (ReportesDescargar, "Descargar reportes y evidencias"), (DerivacionCrear, "Derivar un vehículo a un trámite"),
        ]),
        ("diagnostico_consultas", "Diagnóstico · Consultas",
        [
            (ConsultasReprocesar, "Reprocesar consultas en error"), (ConsultasLlenar, "Llenar consultas a mano (backoffice FLIT)"),
            (DictamenOverride, "Corregir el dictamen (backoffice FLIT)"),
        ]),
        ("diagnostico_configuracion", "Diagnóstico · Configuración",
        [
            (ConfiguracionVer, "Ver la configuración de la compañía"), (ConfiguracionEditar, "Editar la configuración operativa"),
            (ConfiguracionContratoEditar, "Editar el nivel contractual (backoffice FLIT)"),
        ]),
        ("diagnostico_backoffice", "Diagnóstico · Backoffice FLIT",
        [
            (BackofficeVer, "Ver diagnósticos de todas las compañías"), (AuditoriaVer, "Ver la auditoría de consultas y dictámenes"),
            (VolumetriaVer, "Ver la volumetría y la conversión"),
        ]),
    ];

    private static readonly string[] Usuario = [DiagnosticosCrear, DiagnosticosVer, ReportesDescargar, DerivacionCrear, ConfiguracionVer];

    /// <summary>
    /// Roles por defecto (§6.2). <c>admin_diagnostico</c> es el rol de sistema que la plataforma ya asigna a todo
    /// AdminCompany (DDL 126, R-12): el manifiesto solo le pone permisos. <c>backoffice_diagnostico</c> no crea
    /// diagnósticos ni deriva (R-6) y su <c>PoliticaBackoffice</c> exige además <c>tenant_type=FLIT</c> (D-16).
    /// </summary>
    public static IReadOnlyList<(string Codigo, string Nombre, string[] Permisos)> RolesPorDefecto { get; } =
    [
        ("usuario_diagnostico", "Usuario de Diagnóstico", Usuario),
        ("admin_diagnostico", "Administrador de Diagnóstico", [.. Usuario, ConfiguracionEditar, ConsultasReprocesar]),
        ("backoffice_diagnostico", "Backoffice FLIT de Diagnóstico",
        [
            BackofficeVer, ReportesDescargar, ConfiguracionVer, ConfiguracionContratoEditar, ConsultasReprocesar, ConsultasLlenar,
            DictamenOverride, AuditoriaVer, VolumetriaVer,
        ]),
    ];

    /// <summary>Todos los slugs del manifiesto.</summary>
    public static IEnumerable<string> Todos => Modulos.SelectMany(m => m.Permisos).Select(p => p.Slug);
}
