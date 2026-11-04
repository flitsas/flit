using System.Text;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>Color, combustible y carrocería que el RUNT tiene hoy del vehículo del borrador (dato del vehículo, no de personas).</summary>
public sealed record ValoresActualesVehiculo(string? Color, string? Combustible, string? Carroceria);

/// <summary>Valores RUNT vigentes del vehículo de un borrador. <c>null</c> si no se pueden obtener (RUNT caído, sin vehículo): entonces no se compara ni se bloquea.</summary>
public interface IValoresRuntBorrador
{
    Task<ValoresActualesVehiculo?> ObtenerAsync(Guid solicitudId, CancellationToken ct);
}

public static partial class ReglasBorrador
{
    public const string TransformacionColor = "color";
    public const string TransformacionCombustible = "combustible";
    public const string TransformacionCarroceria = "carroceria";

    /// <summary>
    /// Qué atributo del vehículo transforma una declaración (color, combustible o carrocería) o <c>null</c> si no es una
    /// transformación. Se reconoce por el <c>field_key</c> (el mismo del RUNT) o por el catálogo del control.
    /// </summary>
    public static string? TipoTransformacion(DeclaracionTipoDto d) => d.FieldKey switch
    {
        "vehicle_color" => TransformacionColor,
        "vehicle_fuel" => TransformacionCombustible,
        "vehicle_body_type" => TransformacionCarroceria,
        _ => d.Catalogo switch
        {
            "colores" => TransformacionColor,
            "combustibles" => TransformacionCombustible,
            "carrocerias" => TransformacionCarroceria,
            _ => null,
        },
    };

    /// <summary>Mayúsculas, sin tildes y con espacios colapsados: el criterio de FLIT 2 para decir que dos valores son el mismo.</summary>
    public static string NormalizarComparable(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;
        // Sin depender de ICU: con InvariantGlobalization (contenedores) Normalize(FormD) no descompone, así que se mapean a mano las tildes del español.
        var sb = new StringBuilder(valor.Length);
        foreach (var c in valor)
            sb.Append(SinTilde(c));
        return string.Join(' ', sb.ToString().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static char SinTilde(char c) => c switch
    {
        'á' or 'à' or 'ä' or 'â' or 'Á' or 'À' or 'Ä' or 'Â' => 'A',
        'é' or 'è' or 'ë' or 'ê' or 'É' or 'È' or 'Ë' or 'Ê' => 'E',
        'í' or 'ì' or 'ï' or 'î' or 'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
        'ó' or 'ò' or 'ö' or 'ô' or 'Ó' or 'Ò' or 'Ö' or 'Ô' => 'O',
        'ú' or 'ù' or 'ü' or 'û' or 'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
        'ñ' or 'Ñ' => 'N',
        _ => c,
    };

    /// <summary>Mensaje cuando el valor nuevo coincide con el del RUNT; <c>null</c> si es distinto o no hay con qué comparar.</summary>
    public static string? ErrorIgualAlRunt(DeclaracionTipoDto d, string? valor, ValoresActualesVehiculo? runt)
    {
        if (runt is null || string.IsNullOrWhiteSpace(valor))
            return null;
        var tipo = TipoTransformacion(d);
        var actual = tipo switch
        {
            TransformacionColor => runt.Color,
            TransformacionCombustible => runt.Combustible,
            TransformacionCarroceria => runt.Carroceria,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(actual) || NormalizarComparable(actual) != NormalizarComparable(valor))
            return null;
        return tipo switch
        {
            TransformacionColor => "El nuevo color debe ser distinto al registrado en el RUNT.",
            TransformacionCombustible => "El nuevo combustible debe ser distinto al registrado en el RUNT.",
            _ => "La nueva carrocería debe ser distinta a la registrada en el RUNT.",
        };
    }

    /// <summary>¿Alguna de las claves es una declaración de transformación? Solo entonces vale la pena consultar el RUNT.</summary>
    public static bool HayTransformaciones(DefinicionTipoDto def, IEnumerable<string> claves)
    {
        var declaradas = ClavesDeDeclaraciones(def);
        return claves.Any(k => declaradas.TryGetValue(k, out var x) && !x.EsExtra && TipoTransformacion(x.Declaracion) is not null);
    }
}
