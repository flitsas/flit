namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Claves canónicas de <c>procedure_instance_field_values</c> que describen el vehículo.
///
/// <para>Los tres atributos transformables (color, combustible y carrocería) viven por partida
/// doble: la clave base guarda el valor EFECTIVO —el que queda tras el trámite— y la clave
/// <c>*_runt</c> guarda el SNAPSHOT de lo que el RUNT tenía al consultarlo. Quien lea solo la clave
/// base no puede distinguir un vehículo sin transformar de uno cuyo valor nuevo ya se declaró; ese
/// es justamente el error que corrige el detalle del OT. Ver <see cref="ColorRunt"/>.</para>
/// </summary>
public static class VehicleFieldKeys
{
    public const string Plate = "plate";
    public const string Vin = "vin";
    public const string Brand = "vehicle_brand";
    public const string Line = "vehicle_line";

    /// <summary>
    /// Año/modelo. <c>vehicle_year</c> es la que escribe el runtime; <c>vehicle_model</c> solo existe
    /// como alias legado en los datos migrados (ver Bug #11584).
    /// </summary>
    public const string Year = "vehicle_year";

    /// <summary>Alias legado de <see cref="Year"/>, presente únicamente en trámites migrados.</summary>
    public const string LegacyModel = "vehicle_model";

    public const string Class = "vehicle_class";
    public const string Service = "vehicle_service";
    public const string EngineDisplacement = "vehicle_engine_displacement";
    public const string Passengers = "vehicle_passengers";
    public const string Axles = "vehicle_axles";
    public const string State = "vehicle_state";
    public const string EngineNumber = "vehicle_engine_number";
    public const string Chassis = "vehicle_chassis";
    public const string Series = "vehicle_series";

    /// <summary>Color efectivo: el nuevo si el trámite declara un cambio, el del RUNT si no.</summary>
    public const string Color = "vehicle_color";

    /// <summary>Color con el que el vehículo figura en el RUNT (snapshot de la consulta).</summary>
    public const string ColorRunt = "vehicle_color_runt";

    /// <summary>Combustible efectivo. Ver <see cref="Color"/>.</summary>
    public const string Fuel = "vehicle_fuel";

    /// <summary>Combustible con el que el vehículo figura en el RUNT.</summary>
    public const string FuelRunt = "vehicle_fuel_runt";

    /// <summary>Carrocería efectiva. Ver <see cref="Color"/>.</summary>
    public const string BodyType = "vehicle_body_type";

    /// <summary>Carrocería con la que el vehículo figura en el RUNT.</summary>
    public const string BodyTypeRunt = "vehicle_body_type_runt";
}
