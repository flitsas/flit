namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Tipo de trámite FLIT ↔ trámite equivalente en el RUNT (el texto que el RUNT escribe en
/// <c>solicitudes[].tramitesRealizados</c>). Vive en código y se versiona junto con la regla
/// (<see cref="RuntConfirmationRules.Version"/>): si el RUNT cambia un rótulo, cambia la versión y el
/// histórico se re-evalúa desde el crudo.
///
/// La familia (MATRICULAS / TRASPASO / OTROS) decide CÓMO se consulta (VIN, par de documentos, placa +
/// propietario); el tipo decide QUÉ se busca en el historial y qué refuerzo se exige. No hay
/// «etiquetas»: son tipos de trámite con su trámite RUNT.
/// </summary>
public sealed record RuntProcedureEquivalence(
    string ProcedureTypeCode,
    string RuntTramite,
    RuntReinforcement Reinforcement,
    bool Confirmed)
{
    /// <summary>Las equivalencias con captura real del RUNT (2026-09-10) o con presunción declarada.</summary>
    public static readonly IReadOnlyList<RuntProcedureEquivalence> All =
    [
        // MATRICULAS — consulta por VIN. Leasing se presume MATRÍCULA INICIAL (sin captura).
        new("MATRICULA_NUEVA", "MATRICULA INICIAL", RuntReinforcement.Matricula, Confirmed: true),
        new("MATRICULA_LEASING", "MATRICULA INICIAL", RuntReinforcement.Matricula, Confirmed: false),

        // TRASPASO — dos consultas por placa: documento del vendedor y del comprador.
        new("TRASPASO_STANDARD", "TRASPASO", RuntReinforcement.Traspaso, Confirmed: true),
        new("TRASPASO_UNILATERAL", "TRASPASO", RuntReinforcement.Traspaso, Confirmed: false),
        new("TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRASPASO", RuntReinforcement.Traspaso, Confirmed: false),

        // OTROS — consulta por placa + documento del propietario del expediente.
        new("CAMBIO_COLOR", "CAMBIO COLOR", RuntReinforcement.CampoColor, Confirmed: true),
        // TRANSFORMACIÓN es N:1: carrocería y combustible salen con el mismo rótulo. El refuerzo por
        // campo contra el snapshot de radicación es OBLIGATORIO para saber cuál fue.
        new("CAMBIO_CARROCERIA", "TRANSFORMACION", RuntReinforcement.CampoCarroceriaObligatorio, Confirmed: true),
        new("CONVERSION_COMBUSTIBLE", "TRANSFORMACION", RuntReinforcement.CampoCombustibleObligatorio, Confirmed: true),
        // La prenda no se llama prenda: inscribirla es INSCRIPCIÓN ALERTA (QYV381).
        new("PRENDA_INSCRIPCION", "INSCRIPCION ALERTA", RuntReinforcement.GarantiaInscrita, Confirmed: true),
        new("DUPLICADO_PLACA", "DUPLICADO PLACA", RuntReinforcement.Ninguno, Confirmed: true),
    ];

    private static readonly Dictionary<string, RuntProcedureEquivalence> ByCode =
        All.ToDictionary(e => e.ProcedureTypeCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Tipos sin equivalente conocido: producen «No verificable» hasta que haya captura (BLINDAJE, DUPLICADO_TARJETA, …).</summary>
    public static RuntProcedureEquivalence? Find(string? procedureTypeCode) =>
        procedureTypeCode is not null && ByCode.TryGetValue(procedureTypeCode.Trim(), out var eq) ? eq : null;

    /// <summary>Tipos que NUNCA entran a la corrida, aunque estén aprobados (acordado con el PO).</summary>
    public static readonly IReadOnlyList<string> ExcludedProcedureTypeCodes = ["REMATRICULA", "CANCELACION_MATRICULA"];
}

/// <summary>Refuerzo que la regla aplica sobre la señal principal (la solicitud del RUNT).</summary>
public enum RuntReinforcement
{
    Ninguno,

    /// <summary>Placa asignada + estado ACTIVO + placa igual a la preasignada si la hubo. Informativo.</summary>
    Matricula,

    /// <summary>Comprador responde Y vendedor no. Informativo: el veredicto lo decide el historial.</summary>
    Traspaso,

    /// <summary>Color actual ≠ snapshot. Informativo (CAMBIO COLOR es 1:1).</summary>
    CampoColor,

    /// <summary>tipoCarroceria actual ≠ snapshot. Obligatorio si hay snapshot (TRANSFORMACIÓN es N:1).</summary>
    CampoCarroceriaObligatorio,

    /// <summary>tipoCombustible actual ≠ snapshot. Obligatorio si hay snapshot (TRANSFORMACIÓN es N:1).</summary>
    CampoCombustibleObligatorio,

    /// <summary>garantias[] con fechaInscripcion ≥ radicación. Informativo: cita al acreedor.</summary>
    GarantiaInscrita,
}
