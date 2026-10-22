namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// Permisos del producto (especificación GDC §2.2), formato <c>comparendos.&lt;modulo&gt;.&lt;accion&gt;</c>. Son los de la
/// empresa; lo global (catálogos, trabajos, plantillas, calendario) es solo del SuperAdmin y no lleva permiso propio.
/// </summary>
public static class Permisos
{
    public const string BandejaVer = "comparendos.bandeja.ver";
    public const string BandejaExportar = "comparendos.bandeja.exportar";
    public const string ComparendoInactivar = "comparendos.comparendo.inactivar";
    public const string ComparendoObservar = "comparendos.comparendo.observar";
    public const string ContraventorAsignar = "comparendos.contraventor.asignar";
    public const string ContraventorConsultar = "comparendos.contraventor.consultar";
    public const string ContraventorDocumentos = "comparendos.contraventor.documentos";
    public const string CambioContraventorSolicitar = "comparendos.cambio_contraventor.solicitar";
    public const string CambioContraventorResponder = "comparendos.cambio_contraventor.responder";
    public const string AvisosEnviar = "comparendos.avisos.enviar";
    public const string DpGenerar = "comparendos.dp.generar";
    public const string DpEnviar = "comparendos.dp.enviar";
    public const string DpEnviarMasivo = "comparendos.dp.enviar_masivo";
    public const string DpResponder = "comparendos.dp.responder";
    public const string PagosRegistrar = "comparendos.pagos.registrar";
    public const string PagosCargarMasivo = "comparendos.pagos.cargar_masivo";
    public const string OcrVer = "comparendos.ocr.ver";
    public const string OcrGestionar = "comparendos.ocr.gestionar";
    public const string MonitoreoVer = "comparendos.monitoreo.ver";
    public const string MonitoreoGestionar = "comparendos.monitoreo.gestionar";
    public const string RepresentantesVer = "comparendos.representantes.ver";
    public const string RepresentantesGestionar = "comparendos.representantes.gestionar";
    public const string OtsGestionar = "comparendos.ots.gestionar";
    public const string ConfiguracionVer = "comparendos.configuracion.ver";
    public const string ConfiguracionEditar = "comparendos.configuracion.editar";
    public const string PlantillasVer = "comparendos.plantillas.ver";
    public const string MensajesVer = "comparendos.mensajes.ver";
    public const string ReportesVer = "comparendos.reportes.ver";
    public const string ProductividadVer = "comparendos.productividad.ver";

    /// <summary>Módulos del manifiesto con sus permisos y el nombre que ve el administrador de roles.</summary>
    public static readonly IReadOnlyList<(string Codigo, string Nombre, (string Slug, string Nombre)[] Permisos)> Modulos =
    [
        ("comparendos_bandeja", "Comparendos · Bandeja", [(BandejaVer, "Ver la bandeja y el detalle"), (BandejaExportar, "Exportar la bandeja a Excel")]),
        ("comparendos_gestion", "Comparendos · Gestión", [(ComparendoInactivar, "Inactivar comparendos"), (ComparendoObservar, "Agregar observaciones")]),
        ("comparendos_contraventor", "Comparendos · Contraventor",
        [
            (ContraventorAsignar, "Asignar o reemplazar el conductor"), (ContraventorConsultar, "Consultar el conductor automáticamente"),
            (ContraventorDocumentos, "Cargar documentos del conductor"), (CambioContraventorSolicitar, "Solicitar el cambio de contraventor"),
            (CambioContraventorResponder, "Registrar la respuesta del OT al cambio de contraventor"), (AvisosEnviar, "Enviar avisos al conductor"),
        ]),
        ("comparendos_dp", "Comparendos · Derechos de petición",
        [
            (DpGenerar, "Previsualizar derechos de petición"), (DpEnviar, "Enviar derechos de petición"),
            (DpEnviarMasivo, "Enviar derechos de petición en lote"), (DpResponder, "Registrar la respuesta del OT"),
        ]),
        ("comparendos_pagos", "Comparendos · Pagos", [(PagosRegistrar, "Registrar pagos"), (PagosCargarMasivo, "Cargar pagos por Excel")]),
        ("comparendos_ocr", "Comparendos · OCR", [(OcrVer, "Ver paquetes OCR"), (OcrGestionar, "Cargar, revisar y registrar documentos OCR")]),
        ("comparendos_configuracion", "Comparendos · Configuración",
        [
            (MonitoreoVer, "Ver NITs y placas monitoreadas"), (MonitoreoGestionar, "Gestionar NITs y placas monitoreadas"),
            (RepresentantesVer, "Ver representantes legales"), (RepresentantesGestionar, "Gestionar representantes legales"),
            (OtsGestionar, "Gestionar permisos por organismo de tránsito"), (ConfiguracionVer, "Ver la configuración de la empresa"),
            (ConfiguracionEditar, "Editar remitente, copias y destinatarios"), (PlantillasVer, "Ver las plantillas"),
        ]),
        ("comparendos_reportes", "Comparendos · Reportes",
            [(MensajesVer, "Ver el centro de mensajes"), (ReportesVer, "Ver el dashboard"), (ProductividadVer, "Ver la productividad")]),
    ];

    private static readonly string[] Lectura =
        [BandejaVer, BandejaExportar, MensajesVer, PlantillasVer, MonitoreoVer, RepresentantesVer, ConfiguracionVer, OcrVer, ReportesVer, ProductividadVer];

    private static readonly string[] Operacion =
    [
        ComparendoInactivar, ComparendoObservar, ContraventorAsignar, ContraventorConsultar, ContraventorDocumentos,
        CambioContraventorSolicitar, CambioContraventorResponder, AvisosEnviar, DpGenerar, DpEnviar, DpEnviarMasivo, DpResponder,
        PagosRegistrar, PagosCargarMasivo, OcrGestionar,
    ];

    private static readonly string[] Administracion = [MonitoreoGestionar, RepresentantesGestionar, OtsGestionar, ConfiguracionEditar];

    /// <summary>
    /// Roles sugeridos (§2.3). <c>admin_comparendos</c> es el rol de sistema que la plataforma ya asigna a todo
    /// AdminCompany: el manifiesto solo le pone permisos.
    /// </summary>
    public static readonly IReadOnlyList<(string Codigo, string Nombre, string[] Permisos)> RolesPorDefecto =
    [
        ("admin_comparendos", "Administrador de Comparendos", [.. Lectura, .. Operacion, .. Administracion]),
        ("operador_comparendos", "Operador de Comparendos", [.. Lectura, .. Operacion]),
        ("consulta_comparendos", "Consulta de Comparendos", Lectura),
    ];
}
