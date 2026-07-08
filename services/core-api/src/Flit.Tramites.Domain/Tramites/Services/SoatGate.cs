namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gate DURO de SOAT en la ruta de preasignación de placa (R06, Feature #10587): el SOAT debe estar
/// vigente para que el OT reciba y apruebe la matrícula. A diferencia del preflight estándar (blando,
/// subsanable con "asumo el riesgo"), aquí un SOAT <b>vencido</b> bloquea la aprobación y NO es
/// subsanable. El estado <c>unknown</c> (típico en 0 km) NO bloquea.
/// </summary>
public static class SoatGate
{
    /// <summary>Field value donde el trámite registra el estado del SOAT en la ruta de placa.</summary>
    public const string FieldKey = "soat_estado";

    public const string Vigente = "vigente";
    public const string Vencido = "vencido";
    public const string Unknown = "unknown";

    /// <summary>
    /// ¿El estado del SOAT bloquea la aprobación del OT? Solo <c>vencido</c> bloquea (gate duro).
    /// <c>vigente</c>, <c>unknown</c>, null o desconocido NO bloquean (0 km suele venir unknown).
    /// </summary>
    public static bool BlocksApproval(string? soatEstado) =>
        string.Equals(soatEstado, Vencido, StringComparison.OrdinalIgnoreCase);
}
