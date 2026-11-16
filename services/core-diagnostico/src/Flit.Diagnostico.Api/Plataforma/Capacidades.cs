namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Lo que el usuario puede hacer en Diagnóstico, para armar el menú (HU #13724). Sale solo del token: permisos del
/// manifiesto y, para el backoffice, las dos políticas de <see cref="PoliticaBackoffice"/> (ajustes 2026-11-11, punto 1):
/// <see cref="Backoffice"/>, <see cref="EditarContrato"/> y <see cref="VerAuditoria"/> siguen la de configuración (SuperAdmin
/// de cualquier compañía o FLIT con <c>diagnostico.backoffice.ver</c>); <see cref="BackofficeOperacion"/>,
/// <see cref="LlenarConsultas"/> y <see cref="CorregirDictamen"/>, la de operación FLIT; <see cref="VerVolumetria"/> sigue
/// exigiendo <c>tenant_type=FLIT</c> (§6.2, D-16). Es una ayuda para la interfaz; cada endpoint vuelve a exigir su permiso.
/// </summary>
public sealed record Capacidades(
    bool CrearDiagnosticos,
    bool VerDiagnosticos,
    bool DescargarReportes,
    bool Derivar,
    bool VerConfiguracion,
    bool EditarConfiguracion,
    bool ReprocesarConsultas,
    bool Backoffice,
    bool EditarContrato,
    bool LlenarConsultas,
    bool CorregirDictamen,
    bool VerAuditoria,
    bool VerVolumetria,
    bool BackofficeOperacion)
{
    public static Capacidades De(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        bool DeFlit(string slug) => usuario.EsEmpresaFlit && usuario.Tiene(slug);
        return new Capacidades(
            CrearDiagnosticos: usuario.Tiene(Permisos.DiagnosticosCrear),
            VerDiagnosticos: usuario.Tiene(Permisos.DiagnosticosVer),
            DescargarReportes: usuario.Tiene(Permisos.ReportesDescargar),
            Derivar: usuario.Tiene(Permisos.DerivacionCrear),
            VerConfiguracion: usuario.Tiene(Permisos.ConfiguracionVer),
            EditarConfiguracion: usuario.Tiene(Permisos.ConfiguracionEditar),
            ReprocesarConsultas: usuario.Tiene(Permisos.ConsultasReprocesar),
            Backoffice: usuario.BackofficeConfiguracion,
            EditarContrato: usuario.BackofficeConfiguracion && usuario.Tiene(Permisos.ConfiguracionContratoEditar),
            LlenarConsultas: usuario.BackofficeOperacion && usuario.Tiene(Permisos.ConsultasLlenar),
            CorregirDictamen: usuario.BackofficeOperacion && usuario.Tiene(Permisos.DictamenOverride),
            VerAuditoria: usuario.BackofficeConfiguracion && usuario.Tiene(Permisos.AuditoriaVer),
            VerVolumetria: DeFlit(Permisos.VolumetriaVer),
            BackofficeOperacion: usuario.BackofficeOperacion);
    }
}
