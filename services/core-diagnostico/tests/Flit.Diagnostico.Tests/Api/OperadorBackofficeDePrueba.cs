using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// Permisos de un operador del backoffice FLIT que no es SuperAdmin (los que tenía el rol retirado <c>backoffice_diagnostico</c>,
/// decisión D23 de la HU #14563). Sirve para las pruebas de R-6: con solo estos permisos no se crean diagnósticos ni se deriva.
/// </summary>
/// <example>
/// <code>
/// api.Token(permisos: OperadorBackofficeDePrueba.Slugs, roles: ["admin_diagnostico"], tipoEmpresa: "FLIT");
/// </code>
/// </example>
internal static class OperadorBackofficeDePrueba
{
    public static readonly string[] Slugs =
    [
        Permisos.BackofficeVer, Permisos.ReportesDescargar, Permisos.ConfiguracionVer,
        Permisos.ConfiguracionContratoEditar, Permisos.ConsultasReprocesar,
        Permisos.ConsultasLlenar, Permisos.DictamenOverride, Permisos.AuditoriaVer,
        Permisos.VolumetriaVer,
    ];
}
