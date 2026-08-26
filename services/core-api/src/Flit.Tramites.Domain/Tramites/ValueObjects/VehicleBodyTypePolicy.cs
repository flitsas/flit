using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Detalle del bloqueo «sin carrocería que cambiar»: viaja del preflight al endpoint, que lo traduce
/// a las extensions RFC7807 del 422 <see cref="VehicleBodyTypePolicy.ErrorCode"/>.
/// </summary>
public sealed record VehicleBodyTypeBlock(string ProcedureType);

/// <summary>
/// Precondición registral del cambio de carrocería: no se puede cambiar la carrocería de un vehículo
/// que el RUNT no reporta con ninguna.
///
/// <para><b>Por qué es un bloqueo y no una advertencia.</b> El trámite consiste en sustituir un
/// atributo por otro, y el FUR lo declara como tal: la casilla 17 se marca y las observaciones
/// imprimen «Carroceria nueva(NUEVA CARROCERIA: …)» sobre un dato original que en este caso no
/// existe. Sin carrocería de partida el organismo no tiene qué sustituir —lo que corresponde es otro
/// trámite— y el expediente se devuelve. Dejar radicar aquí solo traslada el rechazo al final.</para>
///
/// <para><b>Lo que NO bloquea.</b> Que el proveedor no haya respondido. Ahí no se sabe si el vehículo
/// tiene carrocería o no, y convertir una caída del RUNT en un trámite imposible de radicar sería
/// castigar al gestor por una falla de infraestructura. Es la diferencia deliberada con CF-03
/// (<see cref="VehicleStatePolicy"/>), que sí endurece el caso «dato no verificable» porque allí lo
/// que está en juego es matricular dos veces el mismo vehículo.</para>
/// </summary>
public static class VehicleBodyTypePolicy
{
    /// <summary>Código de error 422: el vehículo no tiene carrocería que cambiar.</summary>
    public const string ErrorCode = "VEHICLE_BODY_TYPE_MISSING";

    /// <summary><c>procedureType</c> del detalle RFC7807.</summary>
    public const string ProcedureTypeCambioCarroceria = "cambio_carroceria";

    /// <summary>Clave del <c>field_value</c> con la carrocería que reportó el RUNT.</summary>
    public const string BodyTypeFieldKey = "vehicle_body_type";

    /// <summary>
    /// Clave del snapshot RUNT persistido. Es la que hay que mirar fuera de la consulta: el valor
    /// EFECTIVO (<see cref="BodyTypeFieldKey"/>) lo pisa la carrocería NUEVA que escoge el gestor, así
    /// que en un cambio de carrocería siempre acaba lleno y no dice nada sobre la de partida.
    /// </summary>
    public const string BodyTypeRuntFieldKey = "vehicle_body_type_runt";

    /// <summary>
    /// ¿Este tipo de trámite exige que el vehículo YA tenga carrocería? Solo el cambio de carrocería:
    /// para el resto la carrocería es un dato descriptivo más, y un vehículo que la traiga vacía en el
    /// RUNT puede traspasarse o duplicar su tarjeta sin problema.
    /// </summary>
    public static bool ExigeCarroceriaPrevia(string? procedureTypeCode) =>
        ProcedureTypeLayers.TransformacionDelTipo(procedureTypeCode) == TransformacionBase.Carroceria;

    /// <summary>
    /// Resuelve el bloqueo, o <c>null</c> si el trámite puede seguir.
    /// </summary>
    /// <param name="procedureTypeCode">Código del tipo de trámite de la instancia.</param>
    /// <param name="consultaRespondio">
    /// <c>true</c> si el proveedor de vehículo devolvió datos en esta corrida. Con <c>false</c> nunca
    /// se bloquea: ausencia de respuesta no es ausencia de carrocería.
    /// </param>
    /// <param name="carroceriaReportada">Carrocería que trajo el RUNT (o la ya persistida).</param>
    public static VehicleBodyTypeBlock? Evaluar(
        string? procedureTypeCode,
        bool consultaRespondio,
        string? carroceriaReportada)
    {
        if (!ExigeCarroceriaPrevia(procedureTypeCode))
            return null;
        if (!consultaRespondio)
            return null;
        if (!SinCarroceria(carroceriaReportada))
            return null;

        return new VehicleBodyTypeBlock(ProcedureTypeCambioCarroceria);
    }

    /// <summary>
    /// «No tiene carrocería» tiene DOS formas en el RUNT y hay que reconocer las dos: el campo puede
    /// no venir, o venir con el valor <c>SIN CARROCERIA</c>.
    ///
    /// <para><b>Por qué no basta con mirar si está vacío.</b> <c>SIN CARROCERIA</c> no es relleno: es
    /// una entrada real del catálogo oficial (código <c>0</c> en <c>carroceria.xlsx</c>) y es la
    /// ÚNICA opción de seis clases —motocicleta, mototriciclo, cuatrimoto, ciclomotor, tricimoto y
    /// cuadriciclo—, así que para esos vehículos el RUNT siempre responde con un valor no vacío que
    /// significa exactamente lo contrario de tener carrocería. Comprobando solo el vacío, una
    /// motocicleta pasaba el pre-vuelo y llegaba a un paso donde el selector de carrocería nueva
    /// no tenía ni una opción que ofrecer (el catálogo excluye el valor actual del RUNT).</para>
    /// </summary>
    public static bool SinCarroceria(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return true;

        var norm = Norm(valor);
        // El guion es el hueco que el propio formulario pinta cuando no hay dato.
        return norm is "SIN CARROCERIA" or "SIN CARROCERÍA" or "-";
    }

    /// <summary>Trim + mayúsculas + espacios internos colapsados, para no fallar por un doble espacio.</summary>
    private static string Norm(string valor) =>
        string.Join(' ', valor.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
