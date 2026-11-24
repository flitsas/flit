using Flit.Queries.Domain;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// Lo que una gestora puede preguntarle a sus comparendos en Reportes → Consultas (Reportes 2.0, CONTRATO §5). Los ids
/// están congelados: los usa la UI, las consultas guardadas y las de fábrica.
///
/// <para>Los campos cuyas opciones dependen de lo que la empresa tiene (NIT, código de infracción, organismo…) salen
/// con <see cref="QueryFieldDto.Options"/> vacío y los rellena el repositorio. Las etiquetas de los estados van en
/// español de negocio: el cliente nunca ve el nombre de un enum ni de una fuente (R3).</para>
/// </summary>
public sealed class ComparendoQueryFieldCatalog : IQueryFieldCatalog
{
    public const string Numero = "numero";
    public const string Placa = "placa";
    public const string Nit = "nit";
    public const string Empresa = "empresa";
    public const string CodigoInfraccion = "codigo_infraccion";
    public const string Fotomulta = "fotomulta";
    public const string TipoVehiculo = "tipo_vehiculo";
    public const string ServicioVehiculo = "servicio_vehiculo";
    public const string Organismo = "organismo";
    public const string Departamento = "departamento";
    public const string Municipio = "municipio";
    public const string Activo = "activo";
    public const string EstadoLegal = "estado_legal";
    public const string VentanaDescuento = "ventana_descuento";
    public const string CobroCoactivo = "cobro_coactivo";
    public const string Gestionado = "gestionado";
    public const string Pagado = "pagado";
    public const string EstadoGestion = "estado_gestion";
    public const string TieneDp = "tiene_dp";
    public const string DpEstado = "dp_estado";
    public const string DpSentido = "dp_sentido";
    public const string DpTipologia = "dp_tipologia";
    public const string DpAutomatico = "dp_automatico";
    public const string DpRadicado = "dp_radicado";
    public const string NotificacionConductor = "notificacion_conductor";
    public const string CambioContraventor = "cambio_contraventor";
    public const string ConsultaContraventor = "consulta_contraventor";
    public const string NotificadoFisicamente = "notificado_fisicamente";
    public const string PagoTipo = "pago_tipo";

    public const string GrupoIdentificacion = "Identificación";
    public const string GrupoInfraccion = "Infracción";
    public const string GrupoOrganismo = "Organismo";
    public const string GrupoEstado = "Estado";
    public const string GrupoDp = "Derecho de petición";
    public const string GrupoConductor = "Conductor";
    public const string GrupoPago = "Pago";

    private ComparendoQueryFieldCatalog(bool todasLasEmpresas) => TodasLasEmpresas = todasLasEmpresas;

    /// <summary>El catálogo de una empresa (o del SuperAdmin mirando una): sin el campo «empresa».</summary>
    public static ComparendoQueryFieldCatalog Instance { get; } = new(todasLasEmpresas: false);

    /// <summary>El del SuperAdmin con «todas las empresas»: suma «empresa» y cambia cómo se nombra el universo.</summary>
    public static ComparendoQueryFieldCatalog TodasInstance { get; } = new(todasLasEmpresas: true);

    public static ComparendoQueryFieldCatalog Para(bool todasLasEmpresas) => todasLasEmpresas ? TodasInstance : Instance;

    public bool TodasLasEmpresas { get; }

    // ── Opciones fijas (español de negocio) ───────────────────────────────────────────────────

    private static readonly QueryFieldOptionDto[] SiNo = [new("true", "Sí"), new("false", "No")];

    public static IReadOnlyList<QueryFieldOptionDto> EstadoLegalOptions { get; } =
    [
        new("SinNotificar", "Sin notificación"),
        new("EnTiempo", "En tiempo para gestionar"),
        new("Vencido", "Vencido"),
        new("Caducado", "Caducado"),
        new("Prescrito", "Prescrito"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> VentanaDescuentoOptions { get; } =
    [
        new("Descuento50", "Descuento del 50 % vigente"),
        new("Descuento25", "Descuento del 25 % vigente"),
        new("SinDescuento", "Sin descuento"),
        new("SinFechaBase", "Sin fecha para calcularlo"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> EstadoGestionOptions { get; } =
    [
        new("SinGestion", "Sin gestión"),
        new("DpEnviado", "Derecho de petición enviado"),
        new("RespuestaOt", "Con respuesta del organismo"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> DpEstadoOptions { get; } =
    [
        new("Enviando", "Enviándose"),
        new("Enviado", "Enviado, sin respuesta"),
        new("EnvioFallido", "No se pudo enviar"),
        new("Respondido", "Respondido por el organismo"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> DpSentidoOptions { get; } =
    [
        new("Favorable", "Aceptado por el organismo"),
        new("Desfavorable", "Negado por el organismo"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> NotificacionConductorOptions { get; } =
    [
        new("SinNotificar", "Sin avisos"),
        new("Aviso1", "Aviso 1 enviado"),
        new("Aviso2", "Aviso 2 enviado"),
        new("Aviso3", "Aviso 3 enviado"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> CambioContraventorOptions { get; } =
    [
        new("NoAplica", "No aplica"),
        new("SinSolicitar", "Sin solicitar"),
        new("Solicitado", "Solicitado al organismo"),
        new("RechazadoOt", "Rechazado por el organismo"),
        new("AceptadoOt", "Aceptado por el organismo"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> ConsultaContraventorOptions { get; } =
    [
        new("NoAplica", "Pendiente de asignación manual"),
        new("Pendiente", "Buscando al conductor"),
        new("Encontrado", "Conductor asignado automáticamente"),
        new("NoEncontrado", "Conductor no encontrado"),
        new("Error", "No se pudo buscar; se reintenta"),
        new("SinHora", "Sin hora: revisar el conductor"),
        new("Agotada", "Búsqueda agotada: asignar a mano"),
    ];

    public static IReadOnlyList<QueryFieldOptionDto> PagoTipoOptions { get; } =
    [
        new("CC", "Cambio de contraventor"),
        new("PC", "Pagó el contraventor"),
        new("BC", "Pagó la empresa"),
    ];

    // ── Fechas y orden ────────────────────────────────────────────────────────────────────────

    public const string FechaImposicion = "imposicion";
    public const string FechaNotificacion = "notificacion";
    public const string FechaIngreso = "ingreso";
    public const string FechaDpRadicacion = "dp_radicacion";
    public const string FechaDpRespuesta = "dp_respuesta";
    public const string FechaPago = "pago";

    // Plazos legales (decisión de S0): lo que caduca o prescribe pronto es lo VIEJO, que un rango por imposición deja fuera.
    public const string FechaLimiteGestion = "limite_gestion";
    public const string FechaCaducidad = "caducidad";
    public const string FechaPrescripcion = "prescripcion";

    public static IReadOnlyList<QueryFieldOptionDto> DateFieldOptions { get; } =
    [
        new(FechaImposicion, "Fecha de imposición"),
        new(FechaNotificacion, "Fecha de notificación"),
        new(FechaIngreso, "Fecha de ingreso a FLIT"),
        new(FechaDpRadicacion, "Fecha de radicación del DP"),
        new(FechaDpRespuesta, "Fecha de respuesta del organismo"),
        new(FechaPago, "Fecha de pago"),
        new(FechaLimiteGestion, "Límite de gestión"),
        new(FechaCaducidad, "Fecha de caducidad"),
        new(FechaPrescripcion, "Fecha de prescripción"),
    ];

    public const string OrdenFecha = "fecha";
    public const string OrdenValorTotal = "valor_total";
    public const string OrdenFechaLimiteGestion = "fecha_limite_gestion";
    public const string OrdenNumero = "numero";

    public static IReadOnlyList<string> SortFieldIds { get; } = [OrdenFecha, OrdenValorTotal, OrdenFechaLimiteGestion, OrdenNumero];

    // ── Campos ────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] TextoOps = [QueryOperator.EsAlguno, QueryOperator.Contiene, QueryOperator.EstaVacio, QueryOperator.NoEstaVacio];
    private static readonly string[] OpcionOps = [QueryOperator.EsAlguno, QueryOperator.NoEsNinguno];
    private static readonly string[] BoolOps = [QueryOperator.EsAlguno];

    private static QueryFieldDto Texto(string id, string label, string grupo, string? hint) =>
        new(id, label, QueryFieldKind.Texto, grupo, TextoOps, [], hint, AdmiteLista: true);

    private static QueryFieldDto Opcion(string id, string label, string grupo, IReadOnlyList<QueryFieldOptionDto> opciones, string? hint = null) =>
        new(id, label, QueryFieldKind.Opcion, grupo, OpcionOps, opciones, hint, AdmiteLista: true);

    private static QueryFieldDto Booleano(string id, string label, string grupo, string? hint = null) =>
        new(id, label, QueryFieldKind.Booleano, grupo, BoolOps, SiNo, hint, AdmiteLista: false);

    private static readonly QueryFieldDto[] Todos =
    [
        Texto(Numero, "No. comparendo", GrupoIdentificacion, "Se puede pegar una lista completa desde Excel."),
        Texto(Placa, "Placa", GrupoIdentificacion, "Se puede pegar una lista completa desde Excel."),
        Opcion(Nit, "NIT monitoreado", GrupoIdentificacion, []),
        Opcion(Empresa, "Empresa", GrupoIdentificacion, [], "Sin elegir ninguna, la consulta corre sobre todas las empresas."),

        Opcion(CodigoInfraccion, "Código de infracción", GrupoInfraccion, []),
        Booleano(Fotomulta, "Fotomulta", GrupoInfraccion),
        Opcion(TipoVehiculo, "Tipo del vehículo", GrupoInfraccion, []),
        Opcion(ServicioVehiculo, "Servicio del vehículo", GrupoInfraccion, []),

        Opcion(Organismo, "Organismo de tránsito", GrupoOrganismo, []),
        Opcion(Departamento, "Departamento", GrupoOrganismo, []),
        Opcion(Municipio, "Municipio", GrupoOrganismo, []),

        Booleano(Activo, "Activo", GrupoEstado, "Inactivo es el que ya no reporta la fuente o se inactivó a mano."),
        Opcion(EstadoLegal, "Estado legal", GrupoEstado, EstadoLegalOptions, "Calculado con el día de hoy."),
        Opcion(VentanaDescuento, "Descuento vigente", GrupoEstado, VentanaDescuentoOptions, "Calculado con el día de hoy."),
        Booleano(CobroCoactivo, "Cobro coactivo", GrupoEstado),
        Booleano(Gestionado, "Gestionado", GrupoEstado),
        Booleano(Pagado, "Pagado", GrupoEstado),
        Opcion(EstadoGestion, "Estado de gestión", GrupoEstado, EstadoGestionOptions),

        Booleano(TieneDp, "Tiene DP", GrupoDp),
        Opcion(DpEstado, "Estado DP", GrupoDp, DpEstadoOptions),
        Opcion(DpSentido, "Respuesta del organismo", GrupoDp, DpSentidoOptions),
        Opcion(DpTipologia, "Tesis", GrupoDp, []),
        Booleano(DpAutomatico, "Enviado automáticamente", GrupoDp),
        Texto(DpRadicado, "Radicado", GrupoDp, "Número de radicado del derecho de petición."),

        Opcion(NotificacionConductor, "Avisos al conductor", GrupoConductor, NotificacionConductorOptions),
        Opcion(CambioContraventor, "Cambio de contraventor", GrupoConductor, CambioContraventorOptions),
        Opcion(ConsultaContraventor, "Consulta del contraventor", GrupoConductor, ConsultaContraventorOptions),
        Booleano(NotificadoFisicamente, "Notificado físicamente", GrupoConductor),

        Opcion(PagoTipo, "Tipo de pago", GrupoPago, PagoTipoOptions),
    ];

    private static readonly QueryFieldDto[] SinEmpresa = Todos.Where(f => f.Id != Empresa).ToArray();

    /// <summary>Todos los campos, también «empresa» (para armar la lista de opciones del SuperAdmin).</summary>
    public static IReadOnlyList<QueryFieldDto> TodosLosCampos => Todos;

    public static bool EsIdentificador(string fieldId) => fieldId is Numero or Placa or DpRadicado;

    /// <summary>Campos cuyas opciones las pone el repositorio con los datos de la empresa (no son una lista fija).</summary>
    public static bool EsDinamico(string fieldId) =>
        fieldId is Nit or Empresa or CodigoInfraccion or TipoVehiculo or ServicioVehiculo or Organismo or Departamento or Municipio or DpTipologia;

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    public IReadOnlyList<QueryFieldDto> Fields => TodasLasEmpresas ? Todos : SinEmpresa;

    public IReadOnlyList<QueryFieldOptionDto> DateFields => DateFieldOptions;

    public string DefaultDateField => FechaImposicion;

    public IReadOnlyList<string> SortFields => SortFieldIds;

    public string DefaultSort => OrdenFecha;

    public string Universo => TodasLasEmpresas ? "las empresas" : "su empresa";

    public string RowNoun => "comparendo";

    public string RowNounPlural => "comparendos";

    public bool IsIdentifier(string fieldId) => EsIdentificador(fieldId);

    /// <summary>Etiqueta de negocio de un valor de opción fija (para el Excel); el valor tal cual si no hay.</summary>
    public static string EtiquetaDe(string fieldId, string? valor)
    {
        if (string.IsNullOrEmpty(valor))
            return string.Empty;
        var campo = Todos.FirstOrDefault(f => f.Id == fieldId);
        return campo?.Options.FirstOrDefault(o => o.Value == valor)?.Label ?? valor;
    }
}
