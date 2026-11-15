namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Lo que el usuario puede hacer en Diagnóstico, para armar el menú (HU #13724). Sale solo del token: permisos del
/// manifiesto y, para el backoffice, las dos políticas de <see cref="PoliticaBackoffice"/> (ajustes 2026-11-11, punto 1):
/// <see cref="Backoffice"/>, <see cref="EditarContrato"/> y <see cref="VerAuditoria"/> siguen la de configuración (SuperAdmin
/// de cualquier compañía o FLIT con <c>diagnostico.backoffice.ver</c>); <see cref="BackofficeOperacion"/>,
/// <see cref="LlenarConsultas"/> y <see cref="CorregirDictamen"/>, la de operación FLIT; <see cref="VerVolumetria"/> exige
/// <c>tenant_type=FLIT</c> (§6.2, D-16) salvo al SuperAdmin, que tiene todas las capacidades sin mirar la compañía (decisión
/// D23 de la HU #14563). <see cref="Sanear"/> sigue <c>diagnostico.saneamiento.solicitar</c> y <see cref="GestionarSaneamiento"/> la
/// operación FLIT con <c>diagnostico.saneamiento.gestionar</c> (HU #14576). <see cref="VerReportes"/> y <see cref="VerEstadisticasGlobales"/>
/// salen de <see cref="ReglasEstadisticasYReportes"/>, la misma regla que sus rutas (HU #14579, BE-0, SP-2). Es una ayuda para la interfaz; cada endpoint vuelve a
/// exigir su permiso.
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
    bool BackofficeOperacion,
    bool Sanear,
    bool GestionarSaneamiento,
    bool VerReportes,
    bool VerEstadisticasGlobales)
{
    public static Capacidades De(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        bool DeFlit(string slug) => (usuario.EsSuperAdmin || usuario.EsEmpresaFlit) && usuario.Tiene(slug);
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
            BackofficeOperacion: usuario.BackofficeOperacion,
            // HU #14576 (plan saneamiento §5.3; SP-8 con la decisión E3: el SuperAdmin pasa la regla general del permiso).
            Sanear: usuario.Tiene(Permisos.SaneamientoSolicitar),
            GestionarSaneamiento: usuario.BackofficeOperacion && usuario.Tiene(Permisos.SaneamientoGestionar),
            // HU #14579, BE-0 (SP-2): la misma regla que las rutas R1 y E2.
            VerReportes: ReglasEstadisticasYReportes.VerReportes(usuario),
            VerEstadisticasGlobales: ReglasEstadisticasYReportes.VerEstadisticasGlobales(usuario));
    }
}

/// <summary>
/// Reglas de autorización de estadísticas y reportes (HU #14579, BE-0, SP-1 y SP-2, ADR-0088). La capacidad de la sesión y la ruta usan la
/// misma regla: <see cref="VerReportes"/> es <c>diagnostico.reportes.ver</c> (la exige R1–R3; R2/R3 además exigen
/// <c>diagnostico.reportes.descargar</c>) y <see cref="VerEstadisticasGlobales"/> es la política de configuración del backoffice con
/// <c>diagnostico.volumetria.ver</c> (la exigen E2 y E3). El SuperAdmin pasa las dos (regla general de <c>RequirePermiso</c>); una
/// compañía cliente con los claims de backoffice armados a mano no pasa la segunda (<see cref="PoliticaBackoffice"/>).
/// <c>CapacidadesTests</c> (Plataforma) comprueba que cada capacidad es verdadera si y solo si la ruta responde distinto de 403.
/// </summary>
public static class ReglasEstadisticasYReportes
{
    /// <summary>Capacidad <c>verReportes</c>: la misma regla que <see cref="RequiereVerReportes{TBuilder}"/>.</summary>
    public static bool VerReportes(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return usuario.Tiene(Permisos.ReportesVer);
    }

    /// <summary>Capacidad <c>verEstadisticasGlobales</c>: la misma regla que <see cref="RequiereEstadisticasGlobales{TBuilder}"/>.</summary>
    public static bool VerEstadisticasGlobales(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return usuario.BackofficeConfiguracion && usuario.Tiene(Permisos.VolumetriaVer);
    }

    /// <summary>R0–R3: <c>diagnostico.reportes.ver</c> (o SuperAdmin).</summary>
    public static TBuilder RequiereVerReportes<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequirePermiso(Permisos.ReportesVer);

    /// <summary>E2 y E3: política de configuración del backoffice y <c>diagnostico.volumetria.ver</c>.</summary>
    public static TBuilder RequiereEstadisticasGlobales<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PoliticaBackoffice.Nombre).RequirePermiso(Permisos.VolumetriaVer);
}
