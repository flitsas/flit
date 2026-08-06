namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Los campos por los que se puede preguntar, y nada más.
///
/// <para>Este catálogo es el contrato entre la UI y el motor de consultas: el constructor de
/// filtros se pinta a partir de él, así que un campo nuevo aparece en pantalla sin tocar el
/// frontend. La contrapartida es la regla que lo hace seguro — el cliente solo manda ids de esta
/// lista; el <c>cómo</c> se traduce cada uno vive en el repositorio y nunca viaja por la red.</para>
///
/// <para>Los campos cuyo catálogo depende del organismo (empresas) salen con
/// <see cref="OtQueryFieldDto.Options"/> vacío: los rellena el endpoint. Se declaran igual aquí para
/// que la lista de campos consultables sea UNA, y no «esta lista más los que agrega el endpoint».</para>
/// </summary>
public static class OtQueryFieldCatalog
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

    /// <summary>
    /// Claves de las transformaciones, tal y como se guardan en los valores del formulario
    /// (HU #11206: un valor de texto <c>"true"</c> por transformación declarada).
    ///
    /// <para>El campo pregunta CUÁL y no solo SI: preguntar cuáles cuesta lo mismo y responde
    /// bastante más. «Ninguna» sale del mismo campo con el operador negado sobre las tres.</para>
    /// </summary>
    public static IReadOnlyList<OtQueryFieldOptionDto> TransformacionOptions { get; } =
    [
        new("cambio_color", "Cambio de color"),
        new("cambio_carroceria", "Cambio de carrocería"),
        new("cambio_combustible", "Cambio de combustible"),
    ];

    private static readonly OtQueryFieldOptionDto[] TipoTramiteOptions =
    [
        new("matricula_inicial", "Matrícula inicial"),
        new("traspaso", "Traspaso"),
    ];

    private static readonly OtQueryFieldOptionDto[] EstadoOptions =
    [
        new("en_revision", "En revisión"),
        new("esperando_placa", "Esperando placa"),
        new("esperando_cliente", "En espera del cliente"),
        new("en_subsanacion", "En subsanación"),
        new("aprobado", "Aprobado"),
        new("rechazado", "Rechazado"),
        new("anulado", "Anulado"),
    ];

    private static readonly OtQueryFieldOptionDto[] SiNoOptions =
    [
        new("true", "Sí"),
        new("false", "No"),
    ];

    private static readonly string[] TextoOperators =
        [OtQueryOperator.EsAlguno, OtQueryOperator.Contiene, OtQueryOperator.EstaVacio, OtQueryOperator.NoEstaVacio];

    private static readonly string[] OpcionOperators =
        [OtQueryOperator.EsAlguno, OtQueryOperator.NoEsNinguno];

    private static readonly string[] BooleanoOperators = [OtQueryOperator.EsAlguno];

    private static readonly OtQueryFieldDto[] All =
    [
        new(Placa, "Placa", OtQueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Vin, "VIN", OtQueryFieldKind.Texto, GrupoVehiculo, TextoOperators, [],
            "Se puede pegar una lista completa desde Excel.", AdmiteLista: true),
        new(Radicado, "Radicado", OtQueryFieldKind.Texto, GrupoTramite, TextoOperators, [],
            "Número de referencia del trámite.", AdmiteLista: true),

        new(Comprador, "Comprador", OtQueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Busca por nombre y también por número de documento.", AdmiteLista: true),
        new(Vendedor, "Vendedor", OtQueryFieldKind.Texto, GrupoPersonas, TextoOperators, [],
            "Solo los traspasos tienen vendedor; en matrícula inicial no hay.", AdmiteLista: true),

        // Las opciones las pone el endpoint con las empresas del organismo.
        new(Empresa, "Empresa cliente", OtQueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            null, AdmiteLista: true),
        new(TipoTramite, "Tipo de trámite", OtQueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, TipoTramiteOptions, null, AdmiteLista: true),
        new(Estado, "Estado en el organismo", OtQueryFieldKind.Opcion, GrupoTramite,
            OpcionOperators, EstadoOptions, null, AdmiteLista: true),
        // También lo rellena el endpoint: los revisores del organismo.
        new(Revisor, "Decidido por", OtQueryFieldKind.Opcion, GrupoTramite, OpcionOperators, [],
            "Quién aprobó o rechazó. Los que siguen sin decidir no coinciden con ningún revisor.",
            AdmiteLista: true),

        new(Prioritario, "Prioritario", OtQueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions, null, AdmiteLista: false),
        new(Prenda, "Tiene prenda", OtQueryFieldKind.Booleano, GrupoCaracteristicas,
            BooleanoOperators, SiNoOptions,
            "Cuenta la prenda vigente del trámite; las versiones reemplazadas no.", AdmiteLista: false),
        new(LicenciaTransito, "Licencia de tránsito cargada", OtQueryFieldKind.Booleano,
            GrupoCaracteristicas, BooleanoOperators, SiNoOptions,
            "Si el trámite tiene adjunta la LT.", AdmiteLista: false),
        new(Transformaciones, "Transformaciones", OtQueryFieldKind.Opcion, GrupoCaracteristicas,
            OpcionOperators, TransformacionOptions,
            "«No es ninguna» sobre las tres devuelve los trámites sin transformaciones.",
            AdmiteLista: true),
    ];

    public static IReadOnlyList<OtQueryFieldDto> Fields => All;

    public static OtQueryFieldDto? Find(string? fieldId) =>
        fieldId is null ? null : All.FirstOrDefault(f => f.Id == fieldId);

    public static bool IsKnown(string? fieldId) => Find(fieldId) is not null;

    /// <summary>Etiqueta legible de un campo, para el aviso de cobertura y los mensajes de error.</summary>
    public static string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;

    /// <summary>
    /// Deja la condición en forma canónica o la descarta.
    ///
    /// <para>Descartar es lo correcto y no un atajo: una condición sin valores no restringe nada, y
    /// tratarla como error rompería una consulta guardada mientras el usuario la está editando —
    /// justo cuando acaba de borrar el último valor para escribir otro.</para>
    /// </summary>
    public static OtQueryCondition? Normalize(OtQueryCondition? condition)
    {
        if (condition is null || !IsKnown(condition.FieldId) || !OtQueryOperator.IsKnown(condition.Operator))
        {
            return null;
        }

        var field = Find(condition.FieldId)!;
        if (!field.Operators.Contains(condition.Operator, StringComparer.Ordinal))
        {
            return null;
        }

        if (OtQueryOperator.IsUnary(condition.Operator))
        {
            return new OtQueryCondition(field.Id, condition.Operator, []);
        }

        var values = (condition.Values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(OtQueryLimits.MaxValoresPorCondicion)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        // «Contiene» es un solo texto por definición; si llegan varios se queda con el primero en
        // vez de rechazar la consulta entera.
        if (condition.Operator == OtQueryOperator.Contiene && values.Count > 1)
        {
            values = [values[0]];
        }

        return new OtQueryCondition(field.Id, condition.Operator, values);
    }

    /// <summary>
    /// Deja la definición completa en forma canónica: condiciones válidas, fechas conocidas y orden
    /// de la lista cerrada. Lo que no se reconoce se cae en silencio, por la misma razón que en
    /// <see cref="Normalize(OtQueryCondition)"/>.
    /// </summary>
    public static OtQueryDefinition Normalize(OtQueryDefinition? definition)
    {
        var fechas = definition?.Fechas;
        var campo = OtQueryDateField.IsKnown(fechas?.Campo) ? fechas!.Campo : OtQueryDateField.Radicacion;
        var preset = OtQueryRangePreset.IsKnown(fechas?.Preset) ? fechas!.Preset : OtQueryRangePreset.Ultimos30;

        var condiciones = (definition?.Condiciones ?? [])
            .Select(Normalize)
            .Where(c => c is not null)
            .Select(c => c!)
            // Una condición por campo y operador: dos «placa es alguno» seguidas serían un «y» de
            // dos listas, que nunca es lo que el usuario quiso decir al agregar la segunda.
            .GroupBy(c => (c.FieldId, c.Operator))
            .Select(g => g.Last())
            .Take(OtQueryLimits.MaxCondiciones)
            .ToList();

        return new OtQueryDefinition(
            new OtQueryDateFilter(campo, preset, fechas?.From, fechas?.To),
            condiciones,
            definition?.Columnas ?? [],
            OtQuerySort.IsKnown(definition?.SortBy) ? definition!.SortBy : OtQuerySort.Radicado,
            definition?.Descending ?? true);
    }
}
