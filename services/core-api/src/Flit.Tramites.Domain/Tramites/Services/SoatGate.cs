namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gate DURO de SOAT en la ruta de preasignación de placa (R06, Feature #10587): el SOAT debe estar
/// registrado y vigente para que el OT reciba y apruebe la matrícula. La compañía lo registra en
/// estado <c>asignado</c> por una de dos vías (HU #10611): validando en línea la consulta RUNT (que
/// marca <c>soat_estado=vigente</c> si el RUNT lo reporta vigente) o cargando el PDF del SOAT (que
/// también marca <c>vigente</c>). A diferencia del preflight estándar (blando, subsanable con "asumo
/// el riesgo"), este gate es NO subsanable: sin evidencia de SOAT (estado distinto de <c>vigente</c>:
/// <c>vencido</c>, <c>unknown</c>, null o desconocido) la aprobación del OT queda bloqueada.
/// </summary>
public static class SoatGate
{
    /// <summary>Field value donde el trámite registra el estado del SOAT en la ruta de placa.</summary>
    public const string FieldKey = "soat_estado";

    public const string Vigente = "vigente";
    public const string Vencido = "vencido";
    public const string Unknown = "unknown";

    /// <summary>¿El SOAT está registrado como vigente (evidencia válida: RUNT vigente o PDF cargado)?</summary>
    public static bool IsSatisfied(string? soatEstado) =>
        string.Equals(soatEstado, Vigente, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿El estado del SOAT bloquea la aprobación del OT? Bloquea SALVO que esté <c>vigente</c>: sin
    /// evidencia de SOAT (null/<c>unknown</c>/<c>vencido</c>/desconocido) el OT no puede aprobar.
    /// </summary>
    public static bool BlocksApproval(string? soatEstado) => !IsSatisfied(soatEstado);
}
