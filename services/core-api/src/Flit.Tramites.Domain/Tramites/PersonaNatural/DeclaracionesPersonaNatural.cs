using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>Una opción de una declaración (nivel de blindaje, causal de cancelación…) con los documentos que exige.</summary>
public sealed record OpcionDeclaracionPortal(string Codigo, string Etiqueta, IReadOnlyList<string> DocCodes, bool ExigeSoporte);

/// <summary>
/// Una declaración que el ciudadano hace además de los campos del tipo, con nombres propios del contrato del portal.
/// <paramref name="Control"/>: <c>seleccion</c> (lista cerrada de opciones), <c>seleccion_catalogo</c> (valor de un catálogo) u
/// <c>organismo_destino</c>. <paramref name="FieldKeysExtra"/> son claves que el servidor DERIVA al crear el trámite (el portal
/// no necesita enviarlas). <paramref name="ActivadaPor"/>: transformación (<c>CAMBIO_COLOR</c>…) que activa una declaración de
/// trámite simultáneo; vacío cuando la declaración siempre aplica.
/// </summary>
public sealed record DeclaracionPortal(
    string Clave,
    string Control,
    string Etiqueta,
    bool Obligatoria,
    IReadOnlyList<OpcionDeclaracionPortal> Opciones,
    string FieldKey,
    IReadOnlyList<string> FieldKeysExtra,
    string DocCodeSoporte,
    bool SoporteObligatorio,
    string Catalogo,
    string ActivadaPor);

/// <summary>
/// HU 4 (Features #14248/#14249, Épica #14021) — declaraciones propias de cada tipo, derivadas de las MISMAS constantes y reglas
/// que usa el asistente de FLIT 2: <see cref="ProcedureTypeLayers.TransformacionDelTipo"/>, <see cref="BlindajeOpciones"/>,
/// <see cref="CancelacionCausales"/>, el flag <c>requiresDestinationTransitOffice</c> del perfil y los trámites simultáneos
/// (<see cref="ProcedureTypeGateProfile.ComplementaryTransformationsAllowed"/>). Función pura, sin IO y sin nombrar tipos que el
/// portal tenga que conocer: el portal solo ve <c>control</c>, <c>opciones</c> y <c>field_key</c>.
/// </summary>
public static class DeclaracionesPersonaNatural
{
    public const string ControlSeleccion = "seleccion";
    public const string ControlCatalogo = "seleccion_catalogo";
    public const string ControlOrganismoDestino = "organismo_destino";

    public const string CatalogoColores = "colores";
    public const string CatalogoCombustibles = "combustibles";
    public const string CatalogoCarrocerias = "carrocerias";

    // field_values que escribe el asistente de FLIT 2 (VehicleTransformationsCard).
    public const string CampoColor = "vehicle_color";
    public const string CampoCombustible = "vehicle_fuel";
    public const string CampoCarroceria = "vehicle_body_type";
    public const string BanderaColor = "cambio_color";
    public const string BanderaCombustible = "cambio_combustible";
    public const string BanderaCarroceria = "cambio_carroceria";

    // DocTipo de soporte por transformación (AttachmentRules + RF33 factura_carroceria).
    public const string DocSoporteColor = "soporte_cambio_color";
    public const string DocSoporteCombustible = "soporte_conversion_combustible";
    public const string DocSoporteCarroceria = "factura_carroceria";
    public const string DocCertificadoBlindaje = "certificado_blindaje";

    public const string ClaveBlindaje = "blindaje";
    public const string ClaveCancelacionCausal = "cancelacion_causal";
    public const string ClaveOrganismoDestino = "organismo_destino";
    public const string PrefijoSimultaneo = "simultaneo_";

    /// <summary>
    /// Catálogo de combustibles. Espejo de <c>VEHICLE_FUEL_CATALOG</c> (<c>frontend/lib/catalogs/vehicle-transformations.ts</c>),
    /// que es la fuente que usa el asistente de FLIT 2: el backend no tiene tabla de combustibles (color y carrocería sí:
    /// <c>catalogs.vehicle_colors</c> y <c>catalogs.vehicle_bodyworks</c>). Es una lista cerrada y corta; si cambia allí, cambia
    /// aquí (la prueba de dominio fija el contenido para que un cambio no pase inadvertido).
    /// </summary>
    public static readonly IReadOnlyList<string> Combustibles =
        ["GASOLINA", "DIESEL", "GAS NATURAL", "ELECTRICO", "HIBRIDO", "HIDROGENO", "ETANOL", "BIODIESEL", "MIXTO", "OTRO"];

    /// <summary>Etiquetas de las causales (mismo texto que <c>frontend/lib/catalogs/cancelacion.ts</c>).</summary>
    private static string EtiquetaCausal(string codigo) => codigo switch
    {
        CancelacionCausales.CodigoDecisionJudicial => "Decisión judicial",
        CancelacionCausales.CodigoPerdidaTotalFuerzaMayor => "Pérdida total por fuerza mayor",
        CancelacionCausales.CodigoPerdidaTotalAccidente => "Pérdida total por accidente",
        CancelacionCausales.CodigoDecisionVoluntaria => "Decisión voluntaria",
        _ => codigo,
    };

    private static string EtiquetaBlindaje(string codigo) => codigo switch
    {
        BlindajeOpciones.CodigoNivel1 => "Blindaje nivel 1",
        BlindajeOpciones.CodigoNivel2 => "Blindaje nivel 2",
        BlindajeOpciones.CodigoNivel3 => "Blindaje nivel 3",
        BlindajeOpciones.CodigoDesmonte => "Desmontar blindaje",
        _ => codigo,
    };

    /// <summary>Declaraciones del tipo, en el orden en que se piden. Sin ninguna, lista vacía.</summary>
    public static IReadOnlyList<DeclaracionPortal> Para(ProcedureTypeGateProfile perfil, string? familia, string? codigoTipo)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        var lista = new List<DeclaracionPortal>();

        // La capa base del tipo: el cambio ES el trámite (familia OTROS) → obligatoria, con soporte opcional.
        var propia = ProcedureTypeLayers.TransformacionDelTipo(codigoTipo);
        switch (propia)
        {
            case TransformacionBase.Color:
                lista.Add(Transformacion(TransformacionBase.Color, obligatoria: true, activadaPor: string.Empty));
                break;
            case TransformacionBase.Combustible:
                lista.Add(Transformacion(TransformacionBase.Combustible, obligatoria: true, activadaPor: string.Empty));
                break;
            case TransformacionBase.Carroceria:
                lista.Add(Transformacion(TransformacionBase.Carroceria, obligatoria: true, activadaPor: string.Empty));
                break;
            case TransformacionBase.Blindaje:
                lista.Add(Blindaje());
                break;
        }

        if (string.Equals(codigoTipo?.Trim(), CancelacionCausales.TipoCodigo, StringComparison.OrdinalIgnoreCase))
            lista.Add(CausalDeCancelacion());

        if (perfil.RequiresDestinationTransitOffice)
            lista.Add(new DeclaracionPortal(
                ClaveOrganismoDestino, ControlOrganismoDestino, "Secretaría de destino", true, [],
                TransitOfficeFieldKeys.DestinoId,
                [TransitOfficeFieldKeys.DestinoCode, TransitOfficeFieldKeys.DestinoName, TransitOfficeFieldKeys.DestinoCity],
                string.Empty, false, string.Empty, string.Empty));

        // Trámites simultáneos (matrícula y traspaso): opcionales, se activan con la transformación que el ciudadano marca.
        if (propia == TransformacionBase.Ninguna && perfil.ComplementaryTransformationsAllowed(familia))
        {
            lista.Add(Transformacion(TransformacionBase.Color, obligatoria: false, activadaPor: "CAMBIO_COLOR"));
            lista.Add(Transformacion(TransformacionBase.Combustible, obligatoria: false, activadaPor: "CONVERSION_COMBUSTIBLE"));
            lista.Add(Transformacion(TransformacionBase.Carroceria, obligatoria: false, activadaPor: "CAMBIO_CARROCERIA"));
        }

        return lista;
    }

    /// <summary>Todos los docTipo que las declaraciones pueden recibir (soportes y documentos por opción): lo que el servidor admite como adjunto.</summary>
    public static IReadOnlySet<string> DocumentosDeDeclaraciones(IEnumerable<DeclaracionPortal> declaraciones)
    {
        ArgumentNullException.ThrowIfNull(declaraciones);
        var docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in declaraciones)
        {
            if (!string.IsNullOrWhiteSpace(d.DocCodeSoporte))
                docs.Add(d.DocCodeSoporte);
            foreach (var o in d.Opciones)
                foreach (var doc in o.DocCodes)
                    docs.Add(doc);
        }

        return docs;
    }

    /// <summary>
    /// Claves derivadas que FLIT 2 escribe a partir de lo que el ciudadano declaró (el portal no las envía): la bandera
    /// <c>cambio_*</c> de cada transformación con valor nuevo, y la bandera <c>blindaje</c> derivada de la opción (el desmonte
    /// la deja en <c>false</c>). Función pura sobre los <c>field_values</c> ya guardados.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Derivados(
        ProcedureTypeGateProfile perfil, string? familia, string? codigoTipo, IReadOnlyDictionary<string, string> campos)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        ArgumentNullException.ThrowIfNull(campos);
        var salida = new List<KeyValuePair<string, string>>();
        string Valor(string clave) => campos.TryGetValue(clave, out var v) ? v?.Trim() ?? string.Empty : string.Empty;

        foreach (var d in Para(perfil, familia, codigoTipo))
        {
            // Un trámite simultáneo lo activa el ciudadano (la bandera cambio_* llega con su valor desde el portal).
            if (d.ActivadaPor.Length > 0)
                continue;

            var valor = Valor(d.FieldKey);
            if (valor.Length == 0)
                continue;

            switch (d.Clave)
            {
                case ClaveBlindaje:
                    var deja = BlindajeOpciones.DejaElVehiculoBlindado(BlindajeOpciones.Parse(valor));
                    salida.Add(new(BlindajeOpciones.BanderaFieldKey, deja ? "true" : "false"));
                    break;
                case ClaveCancelacionCausal:
                case ClaveOrganismoDestino:
                    break;
                default:
                    var bandera = d.FieldKeysExtra.FirstOrDefault(k => k.StartsWith("cambio_", StringComparison.Ordinal));
                    if (bandera is not null)
                        salida.Add(new(bandera, "true"));
                    break;
            }
        }

        return salida;
    }

    // ── Construcción ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static DeclaracionPortal Transformacion(TransformacionBase cual, bool obligatoria, string activadaPor)
    {
        var (sufijo, etiqueta, campo, bandera, doc, catalogo) = cual switch
        {
            TransformacionBase.Color => ("color", "Nuevo color", CampoColor, BanderaColor, DocSoporteColor, CatalogoColores),
            TransformacionBase.Combustible => ("combustible", "Nuevo combustible", CampoCombustible, BanderaCombustible, DocSoporteCombustible, CatalogoCombustibles),
            _ => ("carroceria", "Nueva carrocería", CampoCarroceria, BanderaCarroceria, DocSoporteCarroceria, CatalogoCarrocerias),
        };
        var clave = activadaPor.Length == 0 ? $"transformacion_{sufijo}" : $"{PrefijoSimultaneo}{sufijo}";
        IReadOnlyList<OpcionDeclaracionPortal> opciones = cual == TransformacionBase.Combustible
            ? [.. Combustibles.Select(c => new OpcionDeclaracionPortal(c, c, [], false))]
            : [];
        // El soporte es opcional en FLIT 2 (el valor nuevo es lo obligatorio).
        return new DeclaracionPortal(clave, ControlCatalogo, etiqueta, obligatoria, opciones, campo, [bandera], doc, false, catalogo, activadaPor);
    }

    private static DeclaracionPortal Blindaje() =>
        new(
            ClaveBlindaje, ControlSeleccion, "Blindaje", true,
            [.. BlindajeOpciones.Codigos.Select(c => new OpcionDeclaracionPortal(c, EtiquetaBlindaje(c), [DocCertificadoBlindaje], true))],
            BlindajeOpciones.FieldKey, [BlindajeOpciones.BanderaFieldKey], DocCertificadoBlindaje, true, string.Empty, string.Empty);

    private static DeclaracionPortal CausalDeCancelacion() =>
        new(
            ClaveCancelacionCausal, ControlSeleccion, "Causal de cancelación", true,
            [.. CancelacionCausales.Codigos.Select(c => new OpcionDeclaracionPortal(c, EtiquetaCausal(c), [.. CancelacionCausales.DocumentosExigidos(c)], true))],
            CancelacionCausales.FieldKey, [], string.Empty, false, string.Empty, string.Empty);
}
