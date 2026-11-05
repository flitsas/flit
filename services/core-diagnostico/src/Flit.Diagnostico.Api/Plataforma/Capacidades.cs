namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Lo que el usuario puede hacer en Diagnóstico, para armar el menú (HU #13724). Sale solo del token: permisos del
/// manifiesto y, para lo que es del backoffice FLIT, además <c>tenant_type=FLIT</c> (§6.2, D-16). Es una ayuda para la
/// interfaz; cada endpoint vuelve a exigir su permiso.
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
    bool VerVolumetria)
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
            Backoffice: DeFlit(Permisos.BackofficeVer),
            EditarContrato: DeFlit(Permisos.ConfiguracionContratoEditar),
            LlenarConsultas: DeFlit(Permisos.ConsultasLlenar),
            CorregirDictamen: DeFlit(Permisos.DictamenOverride),
            VerAuditoria: DeFlit(Permisos.AuditoriaVer),
            VerVolumetria: DeFlit(Permisos.VolumetriaVer));
    }
}
