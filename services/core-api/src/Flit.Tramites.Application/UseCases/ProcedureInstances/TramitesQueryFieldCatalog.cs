using Flit.Queries.Domain;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Por qué puede filtrar un gestor en el LISTADO de trámites (HU #12106).
///
/// <para><b>Comparte la gramática de Consultas, no su motor.</b> El contrato es el mismo
/// <see cref="IQueryFieldCatalog"/> que ya consume la barra de filtros —mismos operadores, mismos
/// grupos, mismo DTO— para que el frontend la reutilice sin adaptador y para que el usuario no tenga
/// que aprenderse dos formas de preguntar lo mismo. Lo que NO se comparte es la ejecución: aquí cada
/// condición se traduce a <c>WHERE</c> en el repositorio, mientras que el motor de Consultas carga el
/// universo en memoria (hasta 20.000 filas) y exige rango de fechas. El listado se recarga en cada
/// pestaña, cada tarjeta de estado y cada página, y arranca sin periodo a propósito: el gestor espera
/// ver todo lo suyo.</para>
///
/// <para><b>Solo entra lo que SQL puede resolver.</b> Esa es la línea que decide qué campo cabe aquí.
/// Consultas ofrece además prenda, licencia de tránsito, transformaciones, leasing o método de pago,
/// que viven en tablas hijas y se resuelven en memoria; ofrecerlos aquí obligaría a cargar el universo
/// y sería exactamente el motor que se descartó. Quien necesite esas preguntas tiene la pestaña
/// Consultas, que para eso está.</para>
///
/// <para><b>Firma de compraventa, no «Firmado».</b> El nombre viejo prometía el estado compuesto que
/// el gestor ve junto a cada actor —que además considera identidad acreditada y firma del baúl, y no
/// es resoluble en SQL sin replicar <c>DeriveFirmaParte</c>—. Lo que este campo filtra es la firma
/// electrónica de la compraventa, y ahora lo dice.</para>
/// </summary>
public sealed class TramitesQueryFieldCatalog : IQueryFieldCatalog
{
    public const string Radicado = "radicado";
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Organismo = "organismo";
    public const string TipoTramite = "tipo_tramite";
    public const string Estado = "estado";
    public const string Gestor = "gestor";
    public const string Fuente = "fuente";
    public const string FirmaCompraventa = "firma_compraventa";

    public const string GrupoVehiculo = "Vehículo";
    public const string GrupoPersonas = "Personas";
    public const string GrupoTramite = "Trámite";
    public const string GrupoOrigen = "Origen";

    private TramitesQueryFieldCatalog()
    {
    }

    /// <summary>El catálogo es inmutable: una sola instancia basta.</summary>
    public static TramitesQueryFieldCatalog Instance { get; } = new();

    /// <summary>
    /// El ciclo de vida del trámite en la empresa. Mismo vocabulario y mismo orden que el de
    /// Consultas: son el mismo dato, y dos listas distintas harían que el mismo trámite se
    /// clasificara diferente según por dónde se pregunte.
    /// </summary>
    private static readonly QueryFieldOptionDto[] EstadoOptions =
    [
        new("borrador", "Borrador"),
        new("preparado", "Preparado"),
        new("entregado", "Entregado al organismo"),
        new("aprobado", "Aprobado"),
        new("rechazado", "Rechazado"),
        new("anulado", "Anulado"),
        new("subsanacion", "En subsanación"),
    ];

    /// <summary>
    /// Por dónde entró el trámite a FLIT. Lista cerrada y corta, así que viaja en el catálogo en vez
    /// de resolverla el repositorio. No hay opción «QX»: Quipux es canal de salida, no de entrada.
    /// </summary>
    private static readonly QueryFieldOptionDto[] FuenteOptions =
    [
        new(TramiteFuente.Dashboard, "Dashboard"),
        new(TramiteFuente.Integracion, "Integración"),
        new(TramiteFuente.Migrado, "Migrado"),
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
        // por eso el criterio «exacta o parcial» no obliga a elegir, lo decide el operador.
        new(Radicado, "ID Trámite", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Número de radicado que emite FLIT (TD-AAAA-NNNNN). Se puede pegar una lista.",
            AdmiteLista: true),
        new(Placa, "Placa", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Vin, "VIN", QueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),

        new(Comprador, "Comprador", QueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Busca por nombre y también por número de documento.", AdmiteLista: true),
        new(Vendedor, "Vendedor", QueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Busca por nombre y también por documento. Solo los traspasos tienen vendedor.",
            AdmiteLista: true),

        // Opciones resueltas por el repositorio: los organismos con los que esta empresa tramita de
        // verdad y los tipos que usa. Ofrecer un organismo con el que nunca ha tramitado es ofrecer un
        // filtro que solo puede devolver cero.
        new(Organismo, "Organismo de tránsito", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        new(TipoTramite, "Tipo de trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, [], null, AdmiteLista: true),
        new(Estado, "Estado del trámite", QueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, EstadoOptions,
            "El estado en su empresa, no el de la bandeja del organismo.", AdmiteLista: true),
        new(Gestor, "Gestor", QueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Quién creó el trámite en FLIT.", AdmiteLista: false),

        new(Fuente, "Fuente", QueryFieldKind.Opcion, GrupoOrigen, OpcionOperators, FuenteOptions,
            "Por dónde entró el trámite a FLIT.", AdmiteLista: false),
        new(FirmaCompraventa, "Firma de compraventa", QueryFieldKind.Booleano, GrupoOrigen,
            BooleanoOperators, SiNoOptions,
            "Firma electrónica de la compraventa completa. No es la acreditación que aparece junto a "
            + "cada actor, que además considera identidad validada y firma del baúl.",
            AdmiteLista: false),
    ];

    // ── IQueryFieldCatalog ────────────────────────────────────────────────────────────────────

    IReadOnlyList<QueryFieldDto> IQueryFieldCatalog.Fields => All;

    IReadOnlyList<QueryFieldOptionDto> IQueryFieldCatalog.DateFields => DateFields;

    string IQueryFieldCatalog.DefaultDateField => FechaCreacion;

    IReadOnlyList<string> IQueryFieldCatalog.SortFields => TramitesQuerySort.All;

    string IQueryFieldCatalog.DefaultSort => TramitesQuerySort.Creado;

    string IQueryFieldCatalog.Universo => "su empresa";

    bool IQueryFieldCatalog.IsIdentifier(string fieldId) => IsIdentifier(fieldId);

    // ── Superficie estática ───────────────────────────────────────────────────────────────────

    public const string FechaCreacion = "creacion";
    public const string FechaActualizacion = "actualizacion";

    /// <summary>Las dos fechas del listado, que son las que ya ofrece el selector «Periodo».</summary>
    public static IReadOnlyList<QueryFieldOptionDto> DateFields { get; } =
    [
        new(FechaCreacion, "Fecha de radicación"),
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

    public static string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;
}

/// <summary>
/// Órdenes admitidos en el listado. Lista cerrada: un campo libre sería inyección de orden.
///
/// <para>Cubre los SUBCAMPOS de las celdas compuestas de la tabla (HU #12108): «Radicado» apila las
/// dos fechas, «Vehículo» la placa y el VIN, y «Trámite» el estado y el tipo, así que ordenar por
/// cualquiera de ellos tiene que ser expresable.</para>
///
/// <para>El organismo NO está: su nombre vive en <c>field_values</c> y su <c>ORDER BY</c> exige un
/// join cuyo coste hay que medir antes de prometerlo.</para>
/// </summary>
public static class TramitesQuerySort
{
    public const string Radicado = "radicado";
    public const string Placa = "placa";
    public const string Vin = "vin";
    public const string Comprador = "comprador";
    public const string Gestor = "gestor";
    public const string Estado = "estado";
    public const string TipoTramite = "tipo_tramite";
    public const string Fuente = "fuente";
    public const string Creado = "createdAt";
    public const string Actualizado = "updatedAt";

    public static IReadOnlyList<string> All { get; } =
        [Radicado, Placa, Vin, Comprador, Gestor, Estado, TipoTramite, Fuente, Creado, Actualizado];
}

/// <summary>
/// Sirve el catálogo de campos filtrables del listado con las opciones que dependen del tenant ya
/// resueltas (HU #12106).
///
/// <para>El constructor de filtros del frontend se pinta a partir de esta respuesta, así que un campo
/// nuevo aparece en pantalla sin desplegar frontend. Esa es la razón de que el catálogo se sirva y no
/// se escriba en el cliente.</para>
/// </summary>
public sealed class GetTramitesQueryFieldsHandler(IProcedureInstanceRepository repo)
{
    public async Task<IReadOnlyList<QueryFieldDto>> HandleAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var opciones = await repo.GetFilterOptionsAsync(tenantId, ct);

        var organismos = opciones.Organismos
            .Select(nombre => new QueryFieldOptionDto(nombre, nombre))
            .ToList();

        // Agrupados por familia: son hasta veintiún tipos y una lista plana obliga a quien filtra a
        // saberse de memoria qué tipo pertenece a cuál. Mismo criterio que el catálogo de Consultas.
        var tipos = opciones.Tipos
            .Select(t => new QueryFieldOptionDto(t.Code, t.Name, FamiliaLabel(t.Family)))
            .ToList();

        return TramitesQueryFieldCatalog.Fields
            .Select(campo => campo.Id switch
            {
                TramitesQueryFieldCatalog.Organismo => campo with { Options = organismos },
                TramitesQueryFieldCatalog.TipoTramite => campo with { Options = tipos },
                _ => campo,
            })
            .ToList();
    }

    private static string FamiliaLabel(string family) => family?.ToUpperInvariant() switch
    {
        "MATRICULAS" => "Matrícula",
        "TRASPASO" => "Traspaso",
        _ => "Otros",
    };
}
