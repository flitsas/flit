using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Admin.Application.GeneracionDocumental.Prefill;

/// <summary>Entrada del prellenado de vehículo: la placa manda, el documento del propietario ayuda.</summary>
public sealed record PrefillVehiculoCommand(
    Guid TenantId,
    string? Placa,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber);

/// <summary>
/// Prellenado «placa primero» del bloque de vehículo (CF-25, HU #12206).
///
/// <para>Traduce los campos hidratados del RUNT —vocabulario interno del repo— a las <b>variables
/// del anexo normativo</b> (<c>docs/plantilla-transferencia-dominio.md</c> §5.1). De las 13
/// variables de vehículo, el RUNT cubre 12: la que falta es
/// <c>no_licencia_transito</c>, que ninguna consulta devuelve y queda para captura manual.</para>
///
/// <para><b>Un trámite activo sobre la placa no bloquea nada.</b> Este handler depende de un solo
/// puerto de consulta: no conoce el repositorio de instancias, no evalúa duplicidad y no evalúa el
/// organismo de tránsito. La imposibilidad de bloquear por trámite activo es estructural, no una
/// promesa del código.</para>
///
/// <para><b>No persiste.</b> No recibe repositorio ni storage de documentos: los valores viajan
/// solo hacia el formulario.</para>
/// </summary>
public sealed class PrefillVehiculoHandler
{
    /// <summary>
    /// Variable del anexo ← clave hidratada del RUNT. El orden es el de §5.1 del anexo, para que
    /// contar filas de este mapa sea contar variables cubiertas. Cada entrada admite claves
    /// alternativas: la primera con valor gana.
    /// </summary>
    private static readonly (string Variable, string[] RuntKeys)[] AnexoMap =
    [
        ("placa", ["plate"]),
        ("marca", ["vehicle_brand"]),
        ("linea", ["vehicle_line"]),
        ("modelo_anio", ["vehicle_year"]),
        ("clase_vehiculo", ["vehicle_class"]),
        ("tipo_carroceria", ["vehicle_body_type"]),
        ("color", ["vehicle_color"]),
        ("no_motor", ["vehicle_engine_number"]),
        // El anexo pide «número de chasis o VIN»: el RUNT los reporta por separado y no siempre trae
        // los dos. Se prefiere el chasis y se cae al VIN, que es el mismo dato para el OT.
        ("no_chasis", ["vehicle_chassis", "vin"]),
        ("no_serie", ["vehicle_series"]),
        ("servicio", ["vehicle_service"]),
        ("organismo_transito", ["transit_office_name"]),
    ];

    /// <summary>
    /// La decimotercera variable de §5.1. No la devuelve ninguna consulta —el número de la licencia
    /// de tránsito está en el cartón físico, no en el RUNT—, así que se captura a mano (HU-10). Se
    /// nombra aquí para que la cobertura del anexo sea verificable por una prueba.
    /// </summary>
    public const string VariableManual = "no_licencia_transito";

    /// <summary>Las 13 variables de vehículo del anexo normativo, §5.1.</summary>
    public static IReadOnlyList<string> VariablesDelAnexo =>
        [.. AnexoMap.Select(m => m.Variable), VariableManual];

    private readonly IStandaloneVehiclePrefill _lookup;

    public PrefillVehiculoHandler(IStandaloneVehiclePrefill lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public async Task<PrefillResult> HandleAsync(
        PrefillVehiculoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var placa = command.Placa?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(placa))
        {
            return PrefillResult.Invalid();
        }

        var result = await _lookup
            .LookupAsync(
                command.TenantId,
                placa,
                command.OwnerDocumentType?.Trim(),
                command.OwnerDocumentNumber?.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Error is not null)
        {
            return PrefillResult.Unavailable(
                [new PrefillSourceAttempt(PrefillSources.Runt, PrefillOutcomes.Error, result.Error)]);
        }

        var fields = new List<PrefilledField>(AnexoMap.Length);
        foreach (var (variable, runtKeys) in AnexoMap)
        {
            var value = FirstValue(result.Fields, runtKeys);
            if (value is not null)
            {
                fields.Add(new PrefilledField(variable, value, PrefillSources.Runt));
            }
        }

        // Sin ningún campo del anexo, la placa no tiene antecedente útil: 200 con found=false, sin
        // campos. Nunca 404 y nunca 502 — la fuente respondió, el vehículo es el que no está.
        if (fields.Count == 0)
        {
            return PrefillResult.Empty(
                [new PrefillSourceAttempt(PrefillSources.Runt, PrefillOutcomes.NotFound)]);
        }

        return new PrefillResult(
            true,
            PrefillSources.Runt,
            fields,
            [new PrefillSourceAttempt(PrefillSources.Runt, PrefillOutcomes.Found)],
            null);
    }

    private static string? FirstValue(IReadOnlyDictionary<string, string?> fields, string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
