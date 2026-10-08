using System.Globalization;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Grpc.Contracts;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Bug #13445 — traduce las transformaciones y la prenda del pre-trámite ICT a los <c>field_values</c>
/// que core-api persiste en el borrador (contrato de carriles, sin cambio de .proto).
/// <para><b>Transformaciones:</b> se leen SOLO de <see cref="ExternalIntegrationMaster.Transformations"/>
/// (códigos RUNT 5/9/17). El <c>transaction_type</c> principal no interviene: allí el 5 significa blindaje,
/// aquí el código de transformación 5 es cambio de color.</para>
/// <para><b>Catálogos:</b> <c>new_vehicle_fuel_type</c> (1-12) y <c>armor_level_number_id</c> (1-4) no
/// tienen en core-ict un catálogo que diga qué combustible o nivel representa cada id, así que no hay
/// equivalencia inequívoca con los códigos del wizard (<c>vehicle_fuel</c> GASOLINA/DIESEL/…,
/// <c>blindaje_nivel</c> NIVEL_1..3/DESMONTE). Se envía solo la bandera y el gestor completa el valor.</para>
/// <para><b>PII:</b> el nombre y el documento del acreedor solo viajan en el request; esta clase no
/// registra nada.</para>
/// </summary>
internal static class IctDraftFieldValuesMapper
{
    internal const int TransformacionColor = 5;
    internal const int TransformacionCombustibleOBlindaje = 9;
    internal const int TransformacionCarroceria = 17;

    private const string True = "true";

    /// <summary>Field values de transformaciones y prenda, en orden estable y sin claves repetidas.</summary>
    internal static IReadOnlyList<FieldValue> Map(ExternalIntegrationMaster master)
    {
        ArgumentNullException.ThrowIfNull(master);

        var values = new List<FieldValue>();
        AddTransformations(values, master);
        AddPrenda(values, master);
        return values;
    }

    private static void AddTransformations(List<FieldValue> values, ExternalIntegrationMaster master)
    {
        var codigos = master.Transformations.Select(t => t.IdTransformationType).ToHashSet();

        if (codigos.Contains(TransformacionColor))
        {
            Add(values, "cambio_color", True);
        }

        if (codigos.Contains(TransformacionCarroceria))
        {
            Add(values, "cambio_carroceria", True);
        }

        if (codigos.Contains(TransformacionCombustibleOBlindaje))
        {
            var conCombustible = master.NewVehicleFuelType is not null;
            var conBlindaje = master.ArmorLevelNumberId is not null;

            if (conCombustible)
            {
                Add(values, "cambio_combustible", True);
            }

            if (conBlindaje)
            {
                Add(values, "blindaje", True);
            }

            if (!conCombustible && !conBlindaje)
            {
                // Sin subtipo no se adivina: core-api emite el aviso y el gestor la declara en el wizard.
                Add(values, "ict_transformacion_sin_subtipo", True);
            }
        }
    }

    private static void AddPrenda(List<FieldValue> values, ExternalIntegrationMaster master)
    {
        // 1 levantar · 2 registrar/inscribir · 3 omitir (catálogo external_integration_guarantee_operation_type).
        if (master.LimitationsOperationType is >= 1 and <= 3)
        {
            Add(values, "ict_prenda_operacion",
                master.LimitationsOperationType.Value.ToString(CultureInfo.InvariantCulture));
        }

        AddIfPresent(values, "ict_prenda_acreedor_nombre", master.LimitationsCreditor);
        AddIfPresent(values, "ict_prenda_acreedor_documento_tipo", master.LimitationsCreditorDocumentType);
        AddIfPresent(values, "ict_prenda_acreedor_documento", master.LimitationsCreditorDocumentNumber);
        AddIfPresent(values, "ict_prenda_fecha_inscripcion", master.LimitationsInscriptionDate);
    }

    private static void AddIfPresent(List<FieldValue> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Add(values, key, value.Trim());
        }
    }

    private static void Add(List<FieldValue> values, string key, string value) =>
        values.Add(new FieldValue { FieldKey = key, ValueText = value });
}
