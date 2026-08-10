using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Los campos por los que puede preguntar una empresa gestora sobre sus propios trámites.
///
/// <para><b>Es otro catálogo, no el del organismo.</b> Comparten el motor —operadores, rangos,
/// normalización, cobertura— pero no la lista de preguntas, y no debería forzarse: el organismo
/// pregunta por «empresa cliente» y por el estado de su bandeja; la gestora pregunta por «organismo
/// de tránsito» y por el estado del trámite en su propio ciclo. Un catálogo único acabaría lleno de
/// campos que en la mitad de los casos no significan nada.</para>
///
/// <para>Los campos cuyas opciones dependen de los datos del tenant —organismos, tipos de trámite,
/// quién radica, métodos de pago— salen con <see cref="QueryFieldDto.Options"/> vacío y los rellena
/// el repositorio con lo que esa empresa realmente tiene. Ofrecer un organismo con el que nunca ha
/// tramitado es ofrecer un filtro que solo puede devolver cero.</para>
/// </summary>
public sealed class CompanyQueryFieldCatalog : IQueryFieldCatalog
{
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Radicado = "radicado";
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Organismo = "organismo";
    public const string TipoTramite = "tipo_tramite";
    public const string Estado = "estado";
    public const string RadicadoPor = "radicado_por";
    public const string Prioritario = "prioritario";
    public const string EnSubsanacion = "en_subsanacion";
    public const string Prenda = "prenda";
    public const string LicenciaTransito = "licencia_transito";
    public const string Transformaciones = "transformaciones";
    public const string Leasing = "leasing";
    public const string MetodoPago = "metodo_pago";
    public const string TipoTraspaso = "tipo_traspaso";

    public const string GrupoVehiculo = "Vehículo";
    public const string GrupoPersonas = "Personas";
    public const string GrupoTramite = "Trámite";
    public const string GrupoCaracteristicas = "Características";
    public const string GrupoComercial = "Comercial";

    private CompanyQueryFieldCatalog()
    {
    }

    /// <summary>El catálogo de la empresa. Es inmutable, así que una sola instancia basta.</summary>
    public static CompanyQueryFieldCatalog Instance { get; } = new();

    /// <summary>
    /// Claves de las transformaciones, tal y como se guardan en los valores del formulario
    /// (HU #11206: un valor de texto <c>"true"</c> por transformación declarada). Son las mismas del
    /// organismo — es el mismo dato mirado desde el otro lado.
    /// </summary>
    public static IReadOnlyList<QueryFieldOptionDto> TransformacionOptions { get; } =
    [
        new("cambio_color", "Cambio de color"),
        new("cambio_carroceria", "Cambio de carrocería"),
        new("cambio_combustible", "Cambio de combustible"),
    ];

    /// <summary>
    /// Cómo se transfiere el dominio. Se deriva igual que en
    /// <c>analytics.v_procedure_detail_report</c> (ver <c>Sql/Ddl/35-HU10814-…</c>): si hay adjunto
    /// de transferencia de dominio manda ése, si no manda la marca de unilateral, y en otro caso es
    /// bilateral. Las dos derivaciones tienen que decir lo mismo o «Reportes Detallados» y esta
    /// consulta clasificarán el mismo trámite de forma distinta.
    /// </summary>
    public static IReadOnlyList<QueryFieldOptionDto> TipoTraspasoOptions { get; } =
    [
        new(TraspasoTransferenciaDominio, "Transferencia de dominio"),
        new(TraspasoUnilateral, "Unilateral"),
        new(TraspasoBilateral, "Bilateral"),
    ];

    public const string TraspasoTransferenciaDominio = "transferencia_dominio";
    public const string TraspasoUnilateral = "unilateral";
    public const string TraspasoBilateral = "bilateral";

    /// <summary>
    /// El ciclo de vida del trámite en la empresa, en orden. NO es el estado del organismo: aquí
    /// «entregado» quiere decir que la gestora ya lo radicó, no que alguien lo haya mirado.
    /// </summary>
    private static readonly QueryFieldOptionDto[] EstadoOptions =
    [
        new("borrador", "Borrador"),
        new("preparado", "Preparado"),
        new("entregado", "Entregado al organismo"),
        new("aprobado", "Aprobado"),
        new("rechazado", "Rechazado"),
        new("anulado", "Anulado"),
    ];

    private static readonly QueryFieldOptionDto[] SiNoOptions =
    [
        new("true", "Sí"),
        new("false", "No"),
    ];

    private static readonly string[] TextoOperators =
        [QueryOperator.EsAlguno, QueryOperator.Contiene, QueryOperator.EstaVacio, QueryOperator.NoEstaVacio];

    private static readonly string[] OpcionOperators =
        [QueryOperator.EsAlguno, QueryOperator.NoEsNinguno];

    private static readonly string[] BooleanoOperators = [QueryOperator.EsAlguno];

    private static readonly QueryFieldDto[] All =
    [
        new(Placa, "Placa", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Vin, "VIN", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Radicado, "Radicado", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Número de referencia del trámite.", AdmiteLista: true),

        new(Comprador, "Comprador", QueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Busca por nombre y también por número de documento.", AdmiteLista: true),
        new(Vendedor, "Vendedor", QueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Solo los traspasos tienen vendedor; en matrícula inicial no hay.", AdmiteLista: true),

        // Las opciones las pone el repositorio con los organismos con los que esta empresa tramita.
        new(Organismo, "Organismo de tránsito", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        // También del repositorio: los tipos de trámite publicados que esta empresa usa.
        new(TipoTramite, "Tipo de trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        new(Estado, "Estado del trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, EstadoOptions,
            "El estado en su empresa, no el de la bandeja del organismo.", AdmiteLista: true),
        // Y del repositorio: quién de la empresa ha radicado.
        new(RadicadoPor, "Radicado por", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            "Quién creó el trámite en FLIT.", AdmiteLista: true),

        new(Prioritario, "Prioritario", QueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions, null, AdmiteLista: false),
        new(EnSubsanacion, "En subsanación", QueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions,
            "Si el organismo lo devolvió y sigue pendiente de corregir.", AdmiteLista: false),
        new(Prenda, "Tiene prenda", QueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions,
            "Cuenta la prenda vigente del trámite; las versiones reemplazadas no.", AdmiteLista: false),
        new(LicenciaTransito, "Licencia de tránsito cargada", QueryFieldKind.Booleano,
            GrupoCaracteristicas, BooleanoOperators, SiNoOptions,
            "Si el trámite tiene adjunta la LT.", AdmiteLista: false),
        new(Transformaciones, "Transformaciones", QueryFieldKind.Opcion, GrupoCaracteristicas,
            OpcionOperators, TransformacionOptions,
            "«No es ninguna» sobre las tres devuelve los trámites sin transformaciones.",
            AdmiteLista: true),

        new(Leasing, "Leasing", QueryFieldKind.Booleano, GrupoComercial,
            BooleanoOperators, SiNoOptions, null, AdmiteLista: false),
        // Del repositorio: los métodos de pago que esta empresa ha usado de verdad. Es texto libre
        // en la base, así que una lista fija se quedaría corta o sobraría según el cliente.
        new(MetodoPago, "Método de pago", QueryFieldKind.Opcion, GrupoComercial,
            OpcionOperators, [], null, AdmiteLista: true),
        new(TipoTraspaso, "Tipo de traspaso", QueryFieldKind.Opcion, GrupoComercial,
            OpcionOperators, TipoTraspasoOptions,
            "En matrícula inicial no aplica; esos trámites no coinciden con ninguno.",
            AdmiteLista: true),
    ];

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    IReadOnlyList<QueryFieldDto> IQueryFieldCatalog.Fields => All;

    IReadOnlyList<QueryFieldOptionDto> IQueryFieldCatalog.DateFields => CompanyQueryDateField.Options;

    string IQueryFieldCatalog.DefaultDateField => CompanyQueryDateField.Creacion;

    IReadOnlyList<string> IQueryFieldCatalog.SortFields => CompanyQuerySort.All;

    string IQueryFieldCatalog.DefaultSort => CompanyQuerySort.Creado;

    string IQueryFieldCatalog.Universo => "su empresa";

    bool IQueryFieldCatalog.IsIdentifier(string fieldId) => IsIdentifier(fieldId);

    // ── Superficie estática ───────────────────────────────────────────────────────────────────

    public static IReadOnlyList<QueryFieldDto> Fields => All;

    /// <summary>
    /// Campos que rinden cuentas una a una en el aviso de cobertura: los que el usuario escribe o
    /// pega esperando ver cada valor de vuelta.
    /// </summary>
    public static bool IsIdentifier(string fieldId) => fieldId is Placa or Vin or Radicado;

    public static QueryFieldDto? Find(string? fieldId) =>
        fieldId is null ? null : All.FirstOrDefault(f => f.Id == fieldId);

    public static string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;

    /// <inheritdoc cref="QueryNormalizer.Normalize(IQueryFieldCatalog, QueryDefinition)"/>
    public static QueryDefinition Normalize(QueryDefinition? definition) =>
        QueryNormalizer.Normalize(Instance, definition);
}
