using System.Globalization;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Grpc.Contracts;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Bug #13445 — traduce las transformaciones y la prenda del pre-trámite ICT a los <c>field_values</c>
/// que core-api persiste en el borrador (contrato de carriles, sin cambio de .proto).
/// <para><b>Transformaciones (D6):</b> las banderas se leen SOLO de
/// <see cref="ExternalIntegrationMaster.Transformations"/>, cuyos códigos son del catálogo ICT
/// <b>Tipo Trámite</b> (no RUNT): 5 blindaje, 6 cambio de carrocería, 7 cambio de color, 9 conversión de
/// combustible. 8 (cambio de locatario), 10/11 (duplicados) y cualquier otro código no son
/// transformaciones y no producen nada.</para>
/// <para><b>Valores (D7/D8/D9):</b> <c>blindaje_nivel</c> y <c>vehicle_fuel</c> se envían siempre que la
/// columna del master traiga un id con equivalencia exacta, venga o no la transformación (cubre el
/// trámite principal 5/9 sin <c>more_transaction</c>). Sin equivalencia exacta no se envía valor y el
/// gestor lo elige en el wizard.</para>
/// <para><b>El valor implica su bandera:</b> si se envía <c>vehicle_fuel</c> se envía también
/// <c>cambio_combustible="true"</c>, y si se envía <c>blindaje_nivel</c> también <c>blindaje="true"</c>
/// (una sola vez, aunque el código 5/9 también la produzca). Motivo: el preflight de core-api
/// (<c>PreflightCommand.UpsertTransformationAwareField</c>) pisa <c>vehicle_fuel</c> con el valor RUNT
/// salvo que <c>cambio_combustible="true"</c>, y en la primera consulta no hay snapshot que lo salve.</para>
/// <para><b>PII:</b> el nombre y el documento del acreedor solo viajan en el request; esta clase no
/// registra nada.</para>
/// </summary>
internal static class IctDraftFieldValuesMapper
{
    internal const int TransformacionBlindaje = 5;
    internal const int TransformacionCarroceria = 6;
    internal const int TransformacionColor = 7;
    internal const int TransformacionCombustible = 9;

    private const string True = "true";

    /// <summary>
    /// D7 — <c>armor_level_number_id</c> (ICT 1-4) → <c>blindaje_nivel</c> del wizard
    /// (core-api <c>BlindajeOpciones</c>: NIVEL_1/NIVEL_2/NIVEL_3/DESMONTE).
    /// </summary>
    private static readonly Dictionary<short, string> NivelBlindaje = new()
    {
        [1] = "NIVEL_1",
        [2] = "NIVEL_2",
        [3] = "NIVEL_3",
        [4] = "DESMONTE",
    };

    /// <summary>
    /// D8 — <c>new_vehicle_fuel_type</c> → <c>vehicle_fuel</c> (texto del catálogo web
    /// <c>VEHICLE_FUEL_CATALOG</c>, frontend/lib/catalogs/vehicle-transformations.ts). Solo equivalencias
    /// exactas. Tabla ICT completa (contrato v1, 1-12):
    /// <list type="table">
    /// <item><term>1 GASOLINA</term><description>GASOLINA</description></item>
    /// <item><term>2 GNV</term><description>GAS NATURAL</description></item>
    /// <item><term>3 DIESEL</term><description>DIESEL</description></item>
    /// <item><term>4 GAS GASOL</term><description>sin valor (dudoso: mezcla gas-gasolina)</description></item>
    /// <item><term>5 ELECTRICO</term><description>ELECTRICO</description></item>
    /// <item><term>6 HIDROGENO</term><description>HIDROGENO</description></item>
    /// <item><term>7 ETANOL</term><description>ETANOL</description></item>
    /// <item><term>8 BIODIESEL</term><description>BIODIESEL</description></item>
    /// <item><term>9 GLP</term><description>sin valor (dudoso: el web no distingue GLP)</description></item>
    /// <item><term>10 GASO ELEC</term><description>sin valor (dudoso: híbrido gasolina-eléctrico)</description></item>
    /// <item><term>11 DIES ELEC</term><description>sin valor (dudoso: híbrido diésel-eléctrico)</description></item>
    /// <item><term>12 AGUA CON GAS</term><description>sin valor (dudoso)</description></item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<short, string> CombustibleWeb = new()
    {
        [1] = "GASOLINA",
        [2] = "GAS NATURAL",
        [3] = "DIESEL",
        [5] = "ELECTRICO",
        [6] = "HIDROGENO",
        [7] = "ETANOL",
        [8] = "BIODIESEL",
    };

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

        // El valor declara la transformación: con valor exacto la bandera va aunque no venga el código.
        var nivelWeb = master.ArmorLevelNumberId is { } nivel && NivelBlindaje.TryGetValue(nivel, out var n)
            ? n
            : null;
        var combustibleWeb = master.NewVehicleFuelType is { } combustible
            && CombustibleWeb.TryGetValue(combustible, out var c)
            ? c
            : null;

        if (codigos.Contains(TransformacionBlindaje) || nivelWeb is not null)
        {
            Add(values, "blindaje", True);
        }

        if (nivelWeb is not null)
        {
            Add(values, "blindaje_nivel", nivelWeb);
        }

        if (codigos.Contains(TransformacionCarroceria))
        {
            Add(values, "cambio_carroceria", True);
        }

        if (codigos.Contains(TransformacionColor))
        {
            Add(values, "cambio_color", True);
        }

        if (codigos.Contains(TransformacionCombustible) || combustibleWeb is not null)
        {
            Add(values, "cambio_combustible", True);
        }

        if (combustibleWeb is not null)
        {
            Add(values, "vehicle_fuel", combustibleWeb);
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

        // Minimización (Ley 1581): con «omitir» (3) la decisión no usa el acreedor ni la fecha, y en
        // core-api los field_values son inmutables; no se envían datos que no sirven a la finalidad.
        if (master.LimitationsOperationType == 3)
        {
            return;
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
