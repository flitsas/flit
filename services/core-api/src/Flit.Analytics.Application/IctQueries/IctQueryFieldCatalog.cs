using Flit.Queries.Domain;

namespace Flit.Analytics.Application.IctQueries;

/// <summary>
/// Los campos por los que puede preguntar una empresa sobre sus propios pre-trámites de
/// Integración con Terceros (ICT).
///
/// <para><b>Es otro catálogo, no el de trámites.</b> Comparte el motor —operadores, rangos,
/// normalización, cobertura— con <c>CompanyQueryFieldCatalog</c>, pero no la lista de preguntas: ICT
/// vive ANTES del trámite, en el pipeline de validación de un sistema externo (gestor, aseguradora),
/// y sus preguntas son sobre ese pipeline —en qué paso de validación va, si tuvo novedades, si ya
/// generó el borrador— no sobre el ciclo de vida del trámite ya radicado.</para>
///
/// <para>Los campos cuyas opciones dependen de los datos del tenant —tipo de trámite, secretaría,
/// cliente de integración— salen con <see cref="QueryFieldDto.Options"/> vacío y los rellena el
/// repositorio con lo que esa empresa realmente tiene.</para>
/// </summary>
public sealed class IctQueryFieldCatalog : IQueryFieldCatalog
{
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Radicado = "radicado";
    public const string NumeroTransaccion = "numero_transaccion";
    public const string Comentarios = "comentarios";
    public const string TipoTramite = "tipo_tramite";
    public const string Estado = "estado";
    public const string Secretaria = "secretaria";
    public const string ClienteIntegracion = "cliente_integracion";
    public const string TieneNovedades = "tiene_novedades";
    public const string Prioritario = "prioritario";
    public const string TieneBorrador = "tiene_borrador";

    /// <summary>
    /// Solo para SuperAdmin. Se excluye del catálogo que ve una empresa normal
    /// (<see cref="IctQueryRepository"/>-equivalente, ver <c>GetFieldsAsync</c>) porque para ella el
    /// campo no dice nada: todas sus filas son de su propia compañía.
    /// </summary>
    public const string Compania = "compania";

    public const string GrupoVehiculo = "Vehículo";
    public const string GrupoTramite = "Trámite";
    public const string GrupoValidacion = "Validación";
    public const string GrupoAlcance = "Alcance";

    private IctQueryFieldCatalog()
    {
    }

    /// <summary>El catálogo de ICT. Es inmutable, así que una sola instancia basta.</summary>
    public static IctQueryFieldCatalog Instance { get; } = new();

    /// <summary>
    /// El estado del pre-trámite en el pipeline de ICT, derivado de <c>process_status_id</c> y de si
    /// ya generó un borrador — ver la regla de precedencia en
    /// <c>IctQueryRepository.ResolveEstado</c>. No es el estado del trámite ya radicado: ese vive en
    /// <see cref="Flit.Analytics.Application.CompanyQueries.CompanyQueryFieldCatalog.Estado"/>.
    /// </summary>
    private static readonly QueryFieldOptionDto[] EstadoOptions =
    [
        new("recibido", "Recibido"),
        new("en_validacion_negocio", "En validación de negocio"),
        new("en_validacion_externa", "En validación externa"),
        new("con_novedades", "Con novedades"),
        new("borrador_creado", "Borrador creado"),
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
        new(Radicado, "Radicado del gestor", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "El identificador con el que el sistema externo lo nombra.", AdmiteLista: true),
        new(NumeroTransaccion, "Número de transacción", QueryFieldKind.Texto, GrupoTramite,
            TextoOperators, [], "El consecutivo que asigna ICT al recibirlo.", AdmiteLista: true),
        // Sin taxonomía de códigos de rechazo: es texto libre concatenado por el SP externo de
        // core-ict, así que el único filtro con sentido es "contiene" — nunca "es alguno de estos".
        new(Comentarios, "Comentarios de validación", QueryFieldKind.Texto, GrupoValidacion,
            TextoOperators, [], "Busca en las observaciones de negocio y de la fuente externa.",
            AdmiteLista: false),

        // Las opciones las pone el repositorio con los tipos que esta empresa realmente ha usado.
        new(TipoTramite, "Tipo de trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        new(Estado, "Estado en ICT", QueryFieldKind.Opcion, GrupoValidacion,
            OpcionOperators, EstadoOptions,
            "En qué paso del pipeline de validación va, no el estado del trámite ya radicado.",
            AdmiteLista: true),
        // Del repositorio: las secretarías con las que esta empresa ha tramitado.
        new(Secretaria, "Secretaría de tránsito", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        // Del repositorio: los clientes de integración (credenciales) de esta empresa.
        new(ClienteIntegracion, "Cliente de integración", QueryFieldKind.Opcion, GrupoValidacion,
            OpcionOperators, [], "Con qué credencial se registró el pre-trámite.", AdmiteLista: true),

        new(TieneNovedades, "Tiene novedades", QueryFieldKind.Booleano, GrupoValidacion,
            BooleanoOperators, SiNoOptions,
            "Si la validación de negocio o externa encontró algo que revisar.", AdmiteLista: false),
        new(TieneBorrador, "Generó borrador", QueryFieldKind.Booleano, GrupoValidacion,
            BooleanoOperators, SiNoOptions,
            "Si el pre-trámite ya pasó a ser un trámite en FLIT.", AdmiteLista: false),
        new(Prioritario, "Prioritario", QueryFieldKind.Booleano, GrupoVehiculo,
            BooleanoOperators, SiNoOptions, null, AdmiteLista: false),

        // Reservado para cuando exista un modo "todas las compañías a la vez" para SuperAdmin
        // (fuera del alcance de esta HU — ver GetFieldsAsync, que hoy lo excluye siempre). El campo
        // queda declarado para no reordenar ids más adelante, pero no es alcanzable todavía.
        new(Compania, "Compañía", QueryFieldKind.Opcion, GrupoAlcance, OpcionOperators, [],
            null, AdmiteLista: true),
    ];

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    IReadOnlyList<QueryFieldDto> IQueryFieldCatalog.Fields => All;

    IReadOnlyList<QueryFieldOptionDto> IQueryFieldCatalog.DateFields => IctQueryDateField.Options;

    string IQueryFieldCatalog.DefaultDateField => IctQueryDateField.Registro;

    IReadOnlyList<string> IQueryFieldCatalog.SortFields => IctQuerySort.All;

    string IQueryFieldCatalog.DefaultSort => IctQuerySort.Registrado;

    string IQueryFieldCatalog.Universo => "sus pre-trámites de ICT";

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
