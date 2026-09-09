using Flit.Queries.Domain;

namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Por qué puede filtrar el organismo de tránsito en su BANDEJA de trámites (HU #12217).
///
/// <para><b>Es el mismo vocabulario que el organismo ya usa en Consultas</b>, no uno nuevo: los
/// identificadores de campo se toman de <see cref="OtQueryFieldCatalog"/>, así que «placa» o
/// «empresa» se llaman igual y se preguntan igual en las dos pantallas. Lo que cambia es el
/// recorte, y por dos motivos concretos:</para>
///
/// <list type="bullet">
/// <item><description><b>El estado es el de la bandeja, no el del informe.</b> Consultas ofrece la
/// lectura analítica del trámite (en revisión, esperando placa, en espera del cliente), que se
/// deriva de cuatro columnas. La bandeja ofrece el estado crudo que el organismo recibe
/// —entregado, aprobado, rechazado, revocado, en subsanación— porque es el que mandan sus tarjetas
/// de cabecera y el que decide qué trabajo tiene delante. Ofrecer las dos lecturas en la misma
/// pantalla serían dos formas de decir lo mismo, que es justo lo que este trabajo viene a
/// quitar.</description></item>
/// <item><description><b>El sub-estado de placa solo existe aquí.</b> Es el otro eje por el que ya
/// cuentan las tarjetas de la bandeja, y sin él una tarjeta llevaría a una lista que no se puede
/// reproducir con los filtros.</description></item>
/// </list>
///
/// <para><b>Fuera de alcance a propósito:</b> «Decidido por» y «Transformaciones» se quedan en
/// Consultas. El primero es una pregunta sobre decisiones ya tomadas —cosa de informe, no de cola de
/// trabajo—; el segundo pregunta CUÁL transformación, una forma distinta que aquí solo añadiría un
/// control con otra gramática.</para>
///
/// <para>Como todo catálogo, es el contrato entre la UI y el servidor: el panel de filtros se pinta
/// a partir de él, así que un campo nuevo aparece en pantalla sin desplegar frontend. La regla que
/// lo hace seguro es que el cliente solo manda ids de esta lista; el <c>cómo</c> se traduce cada uno
/// vive en el repositorio y nunca viaja por la red.</para>
/// </summary>
public sealed class OtBandejaQueryFieldCatalog : IQueryFieldCatalog
{
    // Los ids se citan desde el catálogo de Consultas del organismo, no se reescriben: son el mismo
    // campo visto desde otra pantalla, y dos cadenas iguales escritas en dos sitios es exactamente
    // la forma en que dos pantallas acaban preguntando cosas distintas con el mismo nombre.
    public const string Placa = OtQueryFieldCatalog.Placa;
    public const string Vin = OtQueryFieldCatalog.Vin;
    public const string Radicado = OtQueryFieldCatalog.Radicado;
    public const string Comprador = OtQueryFieldCatalog.Comprador;
    public const string Vendedor = OtQueryFieldCatalog.Vendedor;
    public const string Empresa = OtQueryFieldCatalog.Empresa;
    public const string TipoTramite = OtQueryFieldCatalog.TipoTramite;
    public const string Estado = OtQueryFieldCatalog.Estado;
    public const string Prioritario = OtQueryFieldCatalog.Prioritario;
    public const string Prenda = OtQueryFieldCatalog.Prenda;

    /// <summary>Quién radicó el trámite en la empresa cliente. La bandeja ya lo muestra en su tabla.</summary>
    public const string Gestor = "gestor";

    /// <summary>
    /// Sub-estado de la ruta de placa. Propio de la bandeja: es el eje por el que cuentan tres de
    /// sus tarjetas de cabecera.
    /// </summary>
    public const string SubEstadoPlaca = "sub_estado_placa";

    public const string GrupoVehiculo = OtQueryFieldCatalog.GrupoVehiculo;
    public const string GrupoPersonas = OtQueryFieldCatalog.GrupoPersonas;
    public const string GrupoTramite = OtQueryFieldCatalog.GrupoTramite;
    public const string GrupoCaracteristicas = OtQueryFieldCatalog.GrupoCaracteristicas;

    private OtBandejaQueryFieldCatalog()
    {
    }

    /// <summary>El catálogo es inmutable: una sola instancia basta.</summary>
    public static OtBandejaQueryFieldCatalog Instance { get; } = new();

    /// <summary>
    /// Los estados que el organismo REALMENTE recibe, en el orden del trabajo: lo pendiente primero.
    ///
    /// <para>Tiene que coincidir con <c>TramiteEstado.RecibidosPorOrganismo</c>, que es lo que el
    /// repositorio deja entrar en la bandeja. Ofrecer aquí un estado que la bandeja nunca devuelve
    /// sería ofrecer un filtro que solo puede dar cero. Esa correspondencia la sostiene una prueba,
    /// porque este proyecto no ve el dominio de trámites.</para>
    /// </summary>
    private static readonly QueryFieldOptionDto[] EstadoOptions =
    [
        new("entregado", "Pendiente de decisión"),
        new("aprobado", "Aprobado"),
        new("rechazado", "Rechazado"),
        new("subsanacion", "En subsanación"),
        new("revocado", "Revocado"),
    ];

    /// <summary>
    /// Sub-estado de la ruta de placa. <c>sin_ruta</c> no es un valor de la columna sino su ausencia:
    /// se ofrece como opción porque «los que no están en ruta de placa» es una pregunta que el
    /// organismo hace, y sin ella habría que expresarla negando las otras tres.
    /// </summary>
    private static readonly QueryFieldOptionDto[] SubEstadoPlacaOptions =
    [
        new("sin_ruta", "Sin ruta de placa"),
        new("preasignado", "Placa preasignada"),
        new("asignado", "Placa asignada"),
        new("terminado", "Terminado"),
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
        // Identificadores. `es` compara ignorando guiones, puntos y espacios —una lista pegada desde
        // Excel trae «ABC-123» tan a menudo como «ABC123»— y `contiene` cubre la búsqueda parcial:
        // por eso «exacta o parcial» no obliga a elegir, lo decide el operador.
        new(Placa, "Placa", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Vin, "VIN", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Radicado, "Radicado", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Número de radicado del trámite. Se puede pegar una lista.", AdmiteLista: true),

        new(Comprador, "Comprador", QueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Busca por nombre y también por número de documento.", AdmiteLista: true),
        new(Vendedor, "Propietario / vendedor", QueryFieldKind.Texto, GrupoPersonas, TextoOperators,
            [], "Solo los traspasos tienen vendedor; en matrícula inicial no hay.", AdmiteLista: true),

        // Opciones resueltas por el repositorio: las empresas que de verdad le entregan a este
        // organismo y los tipos que de verdad ha recibido. Ofrecer una empresa con la que nunca ha
        // tramitado es ofrecer un filtro que solo puede devolver cero.
        new(Empresa, "Empresa cliente", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            null, AdmiteLista: true),
        new(TipoTramite, "Tipo de trámite", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            null, AdmiteLista: true),
        new(Estado, "Estado en la bandeja", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators,
            EstadoOptions,
            "El estado con el que el trámite llega al organismo, no el que tiene en su empresa.",
            AdmiteLista: true),
        new(SubEstadoPlaca, "Ruta de placa", QueryFieldKind.Opcion, GrupoTramite, OpcionOperators,
            SubEstadoPlacaOptions,
            "En qué punto va la preasignación de placa. Es lo mismo que cuentan las tarjetas de la "
            + "cabecera.", AdmiteLista: true),
        new(Gestor, "Gestor", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Quién radicó el trámite en la empresa cliente.", AdmiteLista: false),

        new(Prioritario, "Prioritario", QueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions,
            "Los que la empresa cliente marcó para que se revisen con primacía.", AdmiteLista: false),
        new(Prenda, "Tiene prenda", QueryFieldKind.Booleano, GrupoCaracteristicas, BooleanoOperators,
            SiNoOptions,
            "Los que llevan un gravamen vigente y también los trámites que SON de prenda "
            + "(inscribir, levantar, cambio de acreedor).", AdmiteLista: false),
    ];

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    IReadOnlyList<QueryFieldDto> IQueryFieldCatalog.Fields => All;

    IReadOnlyList<QueryFieldOptionDto> IQueryFieldCatalog.DateFields => DateFields;

    string IQueryFieldCatalog.DefaultDateField => FechaRadicacion;

    IReadOnlyList<string> IQueryFieldCatalog.SortFields => OtBandejaSort.All;

    string IQueryFieldCatalog.DefaultSort => OtBandejaSort.FechaRadicacion;

    string IQueryFieldCatalog.Universo => "el organismo";

    bool IQueryFieldCatalog.IsIdentifier(string fieldId) => IsIdentifier(fieldId);

    // ── Superficie estática ───────────────────────────────────────────────────────────────────

    /// <summary>Cuándo entró el trámite al organismo. Es la fecha que la bandeja ya muestra.</summary>
    public const string FechaRadicacion = "radicacion";

    /// <summary>Último movimiento de cualquier tipo.</summary>
    public const string FechaActualizacion = "actualizacion";

    /// <summary>Las dos fechas sobre las que el selector «Periodo» puede aplicar el rango.</summary>
    public static IReadOnlyList<QueryFieldOptionDto> DateFields { get; } =
    [
        new(FechaRadicacion, "Fecha de radicación"),
        new(FechaActualizacion, "Fecha de última actualización"),
    ];

    public static IReadOnlyList<QueryFieldDto> Fields => All;

    /// <summary>
    /// Los que el usuario escribe o pega esperando ver cada valor de vuelta. Determina además cómo se
    /// normaliza al comparar: sin separadores, igual que <c>QueryEngine.SinSeparadores</c>.
    /// </summary>
    public static bool IsIdentifier(string fieldId) => fieldId is Placa or Vin or Radicado;

    public static QueryFieldDto? Find(string? fieldId) =>
        fieldId is null ? null : All.FirstOrDefault(f => f.Id == fieldId);

    public static bool IsKnown(string? fieldId) => Find(fieldId) is not null;

    public static string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;
}

/// <summary>
/// Órdenes admitidos en la bandeja. Lista cerrada: un campo libre sería inyección de orden.
///
/// <para>Incluye <see cref="Empresa"/> además de <see cref="Gestor"/> porque la columna «Empresa /
/// Gestor» de la bandeja apila los dos datos en una celda, y hasta ahora solo se podía ordenar por
/// el segundo: la cabecera prometía un orden por empresa que no existía (HU #12219).</para>
/// </summary>
public static class OtBandejaSort
{
    public const string Radicado = "radicado";
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Empresa = "empresa";
    public const string Gestor = "gestor";
    public const string Estado = "estado";
    public const string TipoTramite = "tipo_tramite";
    public const string FechaRadicacion = "createdAt";

    public static IReadOnlyList<string> All { get; } =
        [Radicado, Placa, Vin, Comprador, Vendedor, Empresa, Gestor, Estado, TipoTramite,
         FechaRadicacion];

    public static bool IsKnown(string? sort) =>
        sort is not null && All.Contains(sort, StringComparer.Ordinal);
}
