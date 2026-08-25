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

    /// <summary>
    /// El trámite interviene un ARRENDATARIO además del propietario (<c>LESSEE</c>). Es una parte
    /// declarativa: se identifica, recibe los correos del trámite y se estampa en el FUR, pero no
    /// valida identidad ni firma — eso lo hace el propietario. Que valide o no lo decide
    /// <c>biometricActors</c>, no esta llave.
    /// </summary>
    public bool RequiresLessee { get; init; }
    public bool AllowsMultipleBuyer { get; init; }
    public bool AllowsMultipleSeller { get; init; }
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

    /// <summary>
    /// El expediente admite declarar transformaciones (color / carrocería / combustible / blindaje)
    /// POR ENCIMA del tipo base — los «trámites simultáneos» del art. 5.1.8.
    ///
    /// <para><c>null</c> (la llave no está en el JSON) NO significa <c>false</c>: significa «lo que
    /// diga la familia», y por eso es anulable donde el resto del perfil son <c>bool</c>. Un perfil
    /// grabado antes de esta llave —o el snapshot congelado de un borrador en curso— seguiría
    /// comportándose como siempre en vez de perder los simultáneos en silencio. Resuélvelo SIEMPRE
    /// con <see cref="ComplementaryTransformationsAllowed"/>, nunca leyendo la propiedad directa.</para>
    /// </summary>
    public bool? AllowsComplementaryTransformations { get; init; }

    /// <summary>
    /// El expediente admite un gravamen POR ENCIMA del tipo base. No se refiere a la prenda de un
    /// tipo de prenda (ahí la prenda ES el trámite, ver <see cref="ProcedureTypeLayers.EsTipoPrendaBase"/>),
    /// sino a la que se añade a un trámite de otra naturaleza. Misma semántica de <c>null</c> que
    /// <see cref="AllowsComplementaryTransformations"/>.
    /// </summary>
    public bool? AllowsComplementaryPrenda { get; init; }

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

    /// <summary>
    /// ¿Este trámite admite transformaciones complementarias? Lo declarado en el perfil manda; sin
    /// declaración, la familia decide (OTROS no acumula: el cambio ES el trámite).
    /// </summary>
    public bool ComplementaryTransformationsAllowed(string? familyCode) =>
        AllowsComplementaryTransformations ?? ProcedureTypeLayers.FamiliaAcumulaComplementarios(familyCode);

    /// <summary>¿Admite un gravamen complementario? Misma precedencia perfil → familia.</summary>
    public bool ComplementaryPrendaAllowed(string? familyCode) =>
        AllowsComplementaryPrenda ?? ProcedureTypeLayers.FamiliaAcumulaComplementarios(familyCode);
}
