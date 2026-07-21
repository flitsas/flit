using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Vista tipada del <c>gate_profile</c> JSON del tipo de trámite (FEATURE-08 / CFD-01..CFD-08).
/// Deserialización tolerante: JSON vacío/nulo/corrupto degrada al perfil por defecto (todo en
/// <c>false</c>/<c>null</c>) — el perfil es configuración, no un invariante del dominio, y el
/// snapshot/creación de instancia no deben romperse por un JSON mal formado.
/// <para>Fuente del esquema: comentario DDL de <c>tramites.procedure_types.gate_profile</c>.
/// Las HUs BE-02 (entryMode + validaciones), BE-04 (comercial/identidad/firma) y BE-05 (placa)
/// consumen campos de este mismo record; BE-06 lo evalúa en el wizard dinámico.</para>
/// </summary>
public sealed record ProcedureTypeGateProfile
{
    public string? EntryMode { get; init; }
    public bool RequiresSeller { get; init; }
    public bool RequiresBuyer { get; init; }
    // NOTA (BE-06, multiplicidad genérica): la multiplicidad de actores NO vive aquí. Es una propiedad
    // POR-ACTOR en conformation_rules.validation_profile.allowsMultiple (ver ConformationRuleProfile),
    // válida para OWNER/BUYER/LESSEE y cualquier actor en cualquier tipo. Se deprecaron
    // allowsMultipleBuyer/allowsMultipleSeller para no tener dos fuentes de verdad sesgadas a traspaso.
    public bool RequiresCommercialValue { get; init; }
    public string? CommercialValueSource { get; init; }
    public bool RequiresBiometrics { get; init; }
    public IReadOnlyList<string> BiometricActors { get; init; } = [];
    public bool RequiresSignature { get; init; }
    public bool RequiresPlateRequest { get; init; }
    public bool ValidateCompanyRule { get; init; }
    public bool ValidateOtOperability { get; init; }
    public bool ValidateDuplicateProcedure { get; init; }
    public bool ValidateSoat { get; init; }
    public bool ValidatePazSalvoImpuesto { get; init; }
    public bool HasPrendaGate { get; init; }
    public string? SimitMode { get; init; }

    /// <summary>Entrada por placa (vehículo ya matriculado).</summary>
    public const string EntryModePlate = "PLATE";

    /// <summary>Entrada por VIN (vehículo nuevo, aún sin placa).</summary>
    public const string EntryModeVin = "VIN";

    /// <summary>Entrada por placa o VIN (el operador elige).</summary>
    public const string EntryModeBoth = "BOTH";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Deserializa el JSON del <c>gate_profile</c>; perfil por defecto si es nulo/vacío/corrupto.</summary>
    public static ProcedureTypeGateProfile FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ProcedureTypeGateProfile();

        try
        {
            return JsonSerializer.Deserialize<ProcedureTypeGateProfile>(json, Options)
                   ?? new ProcedureTypeGateProfile();
        }
        catch (JsonException)
        {
            return new ProcedureTypeGateProfile();
        }
    }

    /// <summary><c>true</c> si alguna validación inicial de negocio está activada (CFD-03).</summary>
    public bool RequiresInitialValidation =>
        ValidateCompanyRule || ValidateOtOperability || ValidateDuplicateProcedure;

    /// <summary><c>true</c> si <paramref name="value"/> es un modo de entrada válido (PLATE/VIN/BOTH).</summary>
    public static bool IsValidEntryMode(string? value) =>
        value is EntryModePlate or EntryModeVin or EntryModeBoth;
}
