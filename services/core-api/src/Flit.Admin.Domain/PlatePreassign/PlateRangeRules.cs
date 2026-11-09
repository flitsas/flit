using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// Reglas de dominio de un rango de placas de preasignación (Feature #10587): validación y
/// explosión a placas individuales. Los rangos enumerables siguen siendo carro/público
/// (3 letras + 3 dígitos, ej. ABC123). La asignación fuera de rango acepta además moto,
/// remolque/semirremolque y maquinaria (mismo criterio que el frontend <c>PLATE_PATTERN</c>).
/// </summary>
public static partial class PlateRangeRules
{
    public const int MinNumber = 0;
    public const int MaxNumber = 999;

    /// <summary>Máximo de placas por rango (evita explosiones accidentales gigantes).</summary>
    public const int MaxPlatesPerRange = 1000;

    /// <summary>
    /// Prefijo reservado del rango ad-hoc contenedor para placas con formato no enumerable
    /// (moto, remolque, maquinaria). La placa real vive en <c>plate_range_details.plate</c>.
    /// </summary>
    public const string NonStandardFormatPrefix = "NSF";

    /// <summary>Bounds del contenedor ad-hoc para formatos no enumerables.</summary>
    public const int NonStandardFormatNumber = 0;

    /// <summary>Mensaje 422 cuando la placa no cumple el patrón unificado.</summary>
    public const string InvalidPlateFormatMessage =
        "La placa no cumple el formato esperado. Ej: ABC123 (carro), ABC12D (moto), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria).";

    /// <summary>
    /// Mensaje 422 cuando el trámite es un carro (<see cref="IsCarClass"/>) y la placa que asigna el
    /// OT no es <c>ABC123</c> (Caso C 2026-11-09).
    /// </summary>
    public const string InvalidCarPlateFormatMessage =
        "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.";

    /// <summary>
    /// Clases de vehículo que son «carro» (Caso C 2026-11-09, decisión del usuario): solo admiten placa
    /// <c>ABC123</c> al asignarla el OT. Valores ya normalizados (mayúsculas, sin tildes, espacios
    /// colapsados). Igualdad EXACTA: hay clases CAMION* de MAQUINARIA (p. ej. «CAMION GRUA»).
    /// </summary>
    public static readonly IReadOnlySet<string> CarVehicleClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "AUTOMOVIL", "CAMIONETA", "CAMPERO", "BUS", "BUSETA", "MICROBUS", "CAMION", "TRACTOCAMION", "VOLQUETA",
    };

    /// <summary>HU8 (Caso C 2026-11-09, D1) — mensaje 422 de moto (<see cref="PlateClassGroup.Moto"/>).</summary>
    public const string InvalidMotoPlateFormatMessage =
        "La placa de esta moto debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.";

    /// <summary>HU8 (Caso C 2026-11-09, D1) — mensaje 422 de remolque (<see cref="PlateClassGroup.Remolque"/>).</summary>
    public const string InvalidRemolquePlateFormatMessage =
        "La placa de este remolque debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.";

    /// <summary>HU8 (Caso C 2026-11-09, D1) — mensaje 422 de maquinaria (<see cref="PlateClassGroup.Maquinaria"/>).</summary>
    public const string InvalidMaquinariaPlateFormatMessage =
        "La placa de esta maquinaria debe tener 2 letras y 6 números, por ejemplo MC029554.";

    /// <summary>
    /// HU8 (Caso C 2026-11-09, D1) — clases de moto y afines (catálogo 97). Ya normalizadas; igualdad EXACTA.
    /// </summary>
    public static readonly IReadOnlySet<string> MotoVehicleClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "MOTOCICLETA", "MOTOCARRO", "CUATRIMOTO", "CUADRICICLO", "MOTOTRICICLO", "TRICIMOTO", "CICLOMOTOR",
    };

    /// <summary>
    /// HU8 (Caso C 2026-11-09, D1) — clases de remolque conocidas aun sin catálogo (las tres filas REMOLQUES
    /// del seed más la grafía «SEMIRREMOLQUE»). El resto de filas REMOLQUES del catálogo llega por
    /// <c>catalogGroup</c> en <see cref="ResolvePlateClassGroup"/>. Ya normalizadas; igualdad EXACTA.
    /// </summary>
    public static readonly IReadOnlySet<string> RemolqueVehicleClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "REMOLQUE", "SEMIREMOLQUE", "SEMIRREMOLQUE", "REMOLQUE Y SEMIRREMOLQUE",
    };

    /// <summary>Ventana de edición del rango tras su creación (Feature #10587, consola OT).</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Unión de formatos colombianos alineada a <c>frontend/lib/validation/fieldRules.ts</c>
    /// (<c>PLATE_PATTERN</c>): carro <c>AAA123</c>, moto <c>AAA12A</c>/<c>AAA12</c>,
    /// remolque/semirremolque <c>R|S</c> + 5–6 dígitos, maquinaria <c>AA123456</c>.
    /// </summary>
    [GeneratedRegex(
        @"^([A-Z]{3}[0-9]{3}|[A-Z]{3}[0-9]{2}[A-Z]?|[RS][0-9]{5,6}|[A-Z]{2}[0-9]{6})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlatePattern();

    /// <summary>Placa de carro: exactamente 3 letras A–Z + 3 dígitos ASCII (Caso C 2026-11-09).</summary>
    [GeneratedRegex(@"^[A-Z]{3}[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CarPlatePattern();

    /// <summary>
    /// ¿La clase de vehículo es un carro? Recibe la clase YA normalizada con
    /// <c>Flit.Tramites.Application.Documents.FurClassificationNormalizer.Normalize</c> (Admin.Domain no
    /// puede depender de una capa de aplicación; la normaliza quien llama, en Infrastructure). Compara
    /// por igualdad exacta contra <see cref="CarVehicleClasses"/>: nunca prefijo ni contains.
    /// Vacía, «SIN CLASE» o cualquier otra clase ⇒ <c>false</c> (formatos actuales).
    /// </summary>
    public static bool IsCarClass(string? normalizedVehicleClass) =>
        !string.IsNullOrEmpty(normalizedVehicleClass) && CarVehicleClasses.Contains(normalizedVehicleClass);

    /// <summary>
    /// ¿La placa (ya en mayúsculas y sin espacios) cumple el formato de carro <c>^[A-Z]{3}[0-9]{3}$</c>?
    /// </summary>
    public static bool IsValidCarPlate(string? plate) =>
        !string.IsNullOrEmpty(plate) && CarPlatePattern().IsMatch(plate);

    /// <summary>Placa de moto: 3 letras + 2 dígitos + letra opcional (HU8).</summary>
    [GeneratedRegex(@"^[A-Z]{3}[0-9]{2}[A-Z]?$", RegexOptions.CultureInvariant)]
    private static partial Regex MotoPlatePattern();

    /// <summary>Placa de remolque: R o S + 5 o 6 dígitos (HU8).</summary>
    [GeneratedRegex(@"^[RS][0-9]{5,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex RemolquePlatePattern();

    /// <summary>Placa de maquinaria: 2 letras + 6 dígitos (HU8).</summary>
    [GeneratedRegex(@"^[A-Z]{2}[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex MaquinariaPlatePattern();

    /// <summary>
    /// HU8 (Caso C 2026-11-09, D1) — grupo de placa de una clase YA normalizada con
    /// <c>FurClassificationNormalizer.Normalize</c> (lo hace Infrastructure: Admin.Domain no ve esa capa).
    /// Orden: listas exactas (carro, moto, remolque) y, si ninguna aplica, el grupo que aporte el catálogo
    /// <c>tramites.vehicle_classification_fur</c> (<paramref name="catalogGroup"/>), pero SOLO
    /// <see cref="PlateClassGroup.Remolque"/> o <see cref="PlateClassGroup.Maquinaria"/>: el AUTOMOTOR por
    /// defecto del catálogo nunca convierte una clase desconocida en carro o moto. Vacía ⇒ <c>null</c>.
    /// </summary>
    public static PlateClassGroup? ResolvePlateClassGroup(string? normalizedVehicleClass, PlateClassGroup? catalogGroup)
    {
        if (string.IsNullOrEmpty(normalizedVehicleClass))
        {
            return null;
        }

        if (CarVehicleClasses.Contains(normalizedVehicleClass))
        {
            return PlateClassGroup.Carro;
        }

        if (MotoVehicleClasses.Contains(normalizedVehicleClass))
        {
            return PlateClassGroup.Moto;
        }

        if (RemolqueVehicleClasses.Contains(normalizedVehicleClass))
        {
            return PlateClassGroup.Remolque;
        }

        return catalogGroup is PlateClassGroup.Remolque or PlateClassGroup.Maquinaria ? catalogGroup : null;
    }

    /// <summary>
    /// HU8 — ¿la placa (ya en mayúsculas y sin espacios) cumple el formato del grupo? Sin grupo ⇒ unión
    /// de formatos (<see cref="IsValidPlate"/>).
    /// </summary>
    public static bool IsValidPlateForGroup(PlateClassGroup? group, string? plate)
    {
        if (string.IsNullOrEmpty(plate))
        {
            return false;
        }

        return group switch
        {
            PlateClassGroup.Carro => CarPlatePattern().IsMatch(plate),
            PlateClassGroup.Moto => MotoPlatePattern().IsMatch(plate),
            PlateClassGroup.Remolque => RemolquePlatePattern().IsMatch(plate),
            PlateClassGroup.Maquinaria => MaquinariaPlatePattern().IsMatch(plate),
            _ => IsValidPlate(plate),
        };
    }

    /// <summary>HU8 — literal 422 del grupo cuando la placa no cumple su formato.</summary>
    public static string InvalidPlateFormatMessageFor(PlateClassGroup group) => group switch
    {
        PlateClassGroup.Carro => InvalidCarPlateFormatMessage,
        PlateClassGroup.Moto => InvalidMotoPlateFormatMessage,
        PlateClassGroup.Remolque => InvalidRemolquePlateFormatMessage,
        PlateClassGroup.Maquinaria => InvalidMaquinariaPlateFormatMessage,
        _ => InvalidPlateFormatMessage,
    };

    /// <summary>
    /// HU8 — valor del contrato <c>grupoPlaca</c> (bandeja y detalle OT): <c>CARRO</c> | <c>MOTO</c> |
    /// <c>REMOLQUE</c> | <c>MAQUINARIA</c> | <c>null</c>.
    /// </summary>
    public static string? ToContractValue(PlateClassGroup? group) => group switch
    {
        PlateClassGroup.Carro => "CARRO",
        PlateClassGroup.Moto => "MOTO",
        PlateClassGroup.Remolque => "REMOLQUE",
        PlateClassGroup.Maquinaria => "MAQUINARIA",
        _ => null,
    };

    /// <summary>¿El prefijo son exactamente 3 letras A–Z?</summary>
    public static bool IsValidPrefix(string? prefix) =>
        prefix is { Length: 3 } && prefix.All(c => c is >= 'A' and <= 'Z');

    /// <summary>Valida un rango; devuelve el motivo del rechazo o null si es válido.</summary>
    public static string? Validate(string? prefix, int from, int to)
    {
        if (!IsValidPrefix(prefix))
        {
            return "El prefijo debe ser 3 letras mayúsculas (A–Z).";
        }

        if (from < MinNumber || to > MaxNumber || from > to)
        {
            return $"El rango numérico debe estar entre {MinNumber:D3} y {MaxNumber:D3} y 'desde' ≤ 'hasta'.";
        }

        if (to - from + 1 > MaxPlatesPerRange)
        {
            return $"El rango no puede exceder {MaxPlatesPerRange} placas.";
        }

        return null;
    }

    /// <summary>Formatea una placa a partir del prefijo y el número (ej. "ABC", 7 → "ABC007").</summary>
    public static string Format(string prefix, int number) =>
        prefix + number.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>¿La placa es carro/público enumerable (3 letras A–Z + 3 dígitos, ej. ABC123)?</summary>
    public static bool IsCarPlate(string? plate) =>
        plate is { Length: 6 }
        && plate[..3].All(c => c is >= 'A' and <= 'Z')
        && plate[3..].All(char.IsDigit);

    /// <summary>
    /// ¿La placa tiene un formato colombiano aceptado en asignación OT (carro, moto,
    /// remolque/semirremolque o maquinaria)?
    /// </summary>
    public static bool IsValidPlate(string? plate) =>
        !string.IsNullOrEmpty(plate) && PlatePattern().IsMatch(plate);

    /// <summary>
    /// Descompone la placa en prefijo + número para el rango contenedor del inventario
    /// (HU #10800). Carro <c>ABC123</c> → (<c>ABC</c>, 123). Otros formatos válidos →
    /// contenedor reservado (<c>NSF</c>, 0). Devuelve null si el formato es inválido.
    /// </summary>
    public static (string Prefix, int Number)? ParsePlate(string? plate)
    {
        if (!IsValidPlate(plate))
        {
            return null;
        }

        if (IsCarPlate(plate))
        {
            return (plate![..3], int.Parse(plate[3..], CultureInfo.InvariantCulture));
        }

        return (NonStandardFormatPrefix, NonStandardFormatNumber);
    }

    /// <summary>Explota el rango en las placas individuales (inclusive en ambos extremos).</summary>
    public static IEnumerable<string> Enumerate(string prefix, int from, int to)
    {
        for (var n = from; n <= to; n++)
        {
            yield return Format(prefix, n);
        }
    }
}
