using Flit.Queries.Domain;

namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Los campos por los que se puede preguntar en el organismo, y nada más.
///
/// <para>Este catálogo es el contrato entre la UI y el motor de consultas: el constructor de
/// filtros se pinta a partir de él, así que un campo nuevo aparece en pantalla sin tocar el
/// frontend. La contrapartida es la regla que lo hace seguro — el cliente solo manda ids de esta
/// lista; el <c>cómo</c> se traduce cada uno vive en el repositorio y nunca viaja por la red.</para>
///
/// <para>Los campos cuyo catálogo depende del organismo (empresas, revisores) salen con
/// <see cref="QueryFieldDto.Options"/> vacío: los rellena el endpoint. Se declaran igual aquí para
/// que la lista de campos consultables sea UNA, y no «esta lista más los que agrega el endpoint».</para>
///
/// <para>Es la única pieza que este módulo escribe entera; operadores, rangos, normalización y
/// cobertura salen de <c>Flit.Queries.Domain</c> y se comparten con las consultas de la empresa
/// gestora. Mantiene además su superficie estática porque los ids de campo se citan por todas
/// partes —fábrica, repositorio, pruebas— y escribirlos a mano sería el único sitio por donde este
/// diseño podría equivocarse en silencio.</para>
/// </summary>
public sealed class OtQueryFieldCatalog : IQueryFieldCatalog
{
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Radicado = "radicado";
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Empresa = "empresa";
    public const string TipoTramite = "tipo_tramite";
    public const string Estado = "estado";
    public const string Prioritario = "prioritario";
    public const string Prenda = "prenda";
    public const string Transformaciones = "transformaciones";
    public const string LicenciaTransito = "licencia_transito";
    public const string Revisor = "revisor";

    public const string GrupoVehiculo = "Vehículo";
    public const string GrupoPersonas = "Personas";
    public const string GrupoTramite = "Trámite";
    public const string GrupoCaracteristicas = "Características";

    private OtQueryFieldCatalog()
    {
    }

    /// <summary>El catálogo del organismo. Es inmutable, así que una sola instancia basta.</summary>
    public static OtQueryFieldCatalog Instance { get; } = new();

    /// <summary>
    /// Claves de las transformaciones, tal y como se guardan en los valores del formulario
    /// (HU #11206: un valor de texto <c>"true"</c> por transformación declarada).
    ///
    /// <para>El campo pregunta CUÁL y no solo SI: preguntar cuáles cuesta lo mismo y responde
    /// bastante más. «Ninguna» sale del mismo campo con el operador negado sobre las tres.</para>
    /// </summary>
    public static IReadOnlyList<QueryFieldOptionDto> TransformacionOptions { get; } =
    [
        new("cambio_color", "Cambio de color"),
        new("cambio_carroceria", "Cambio de carrocería"),
        new("cambio_combustible", "Cambio de combustible"),
    ];

    private static readonly QueryFieldOptionDto[] TipoTramiteOptions =
    [
        new("matricula_inicial", "Matrícula inicial"),
        new("traspaso", "Traspaso"),
    ];

    private static readonly QueryFieldOptionDto[] EstadoOptions =
    [
        new("en_revision", "En revisión"),
        new("esperando_placa", "Esperando placa"),
        new("esperando_cliente", "En espera del cliente"),
        new("en_subsanacion", "En subsanación"),
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

        // Las opciones las pone el endpoint con las empresas del organismo.
        new(Empresa, "Empresa cliente", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            null, AdmiteLista: true),
        new(TipoTramite, "Tipo de trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, TipoTramiteOptions, null, AdmiteLista: true),
        new(Estado, "Estado en el organismo", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, EstadoOptions, null, AdmiteLista: true),
        // También lo rellena el endpoint: los revisores del organismo.
        new(Revisor, "Decidido por", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            "Quién aprobó o rechazó. Los que siguen sin decidir no coinciden con ningún revisor.",
            AdmiteLista: true),

        new(Prioritario, "Prioritario", QueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions, null, AdmiteLista: false),
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
    ];

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    IReadOnlyList<QueryFieldDto> IQueryFieldCatalog.Fields => All;

    IReadOnlyList<QueryFieldOptionDto> IQueryFieldCatalog.DateFields => OtQueryDateField.Options;

    string IQueryFieldCatalog.DefaultDateField => OtQueryDateField.Radicacion;

    IReadOnlyList<string> IQueryFieldCatalog.SortFields => OtQuerySort.All;

    string IQueryFieldCatalog.DefaultSort => OtQuerySort.Radicado;

    string IQueryFieldCatalog.Universo => "el organismo";

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

    public static bool IsKnown(string? fieldId) => Find(fieldId) is not null;

    /// <summary>Etiqueta legible de un campo, para el aviso de cobertura y los mensajes de error.</summary>
    public static string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;

    /// <inheritdoc cref="QueryNormalizer.Normalize(IQueryFieldCatalog, QueryCondition)"/>
    public static QueryCondition? Normalize(QueryCondition? condition) =>
        QueryNormalizer.Normalize(Instance, condition);

    /// <inheritdoc cref="QueryNormalizer.Normalize(IQueryFieldCatalog, QueryDefinition)"/>
    public static QueryDefinition Normalize(QueryDefinition? definition) =>
        QueryNormalizer.Normalize(Instance, definition);
}
