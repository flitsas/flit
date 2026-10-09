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

    /// <summary>
    /// HU #10973 — normaliza el estado CRUDO que reporta el RUNT (p. ej. <c>"VIGENTE"</c>) al
    /// vocabulario del gate (<see cref="Vigente"/>/<see cref="Vencido"/>/<see cref="Unknown"/>).
    /// <para><b>Es obligatorio normalizar antes de persistir en <see cref="FieldKey"/>.</b> Este
    /// tipo compara con <c>OrdinalIgnoreCase</c>, pero el FRONTEND no: <c>lib/tramites/estados.ts</c>
    /// compara de forma ESTRICTA contra <c>"vigente"</c> en minúscula. Persistir un <c>"VIGENTE"</c>
    /// crudo dejaría <c>puedeAprobar = false</c> con el SOAT vigente, bloqueando la aprobación del OT
    /// en trámites correctos.</para>
    /// Devuelve <c>null</c> si no hay estado que normalizar (el llamador NO debe escribir la llave:
    /// valor ausente ⇒ celda en blanco, regla HU #10856).
    /// </summary>
    public static string? Normalize(string? rawEstado)
    {
        var v = rawEstado?.Trim();
        if (string.IsNullOrEmpty(v))
            return null;

        if (string.Equals(v, Vigente, StringComparison.OrdinalIgnoreCase))
            return Vigente;

        // "VENCIDO", "NO VIGENTE" y "NO_VIGENTE" son las formas que reportan los proveedores para un
        // SOAT sin cobertura. Cualquier otro valor (ANULADA, CANCELADA, …) NO se fuerza a 'vencido':
        // se declara 'unknown' para no afirmar un estado que el RUNT no dijo. El gate bloquea igual.
        if (string.Equals(v, Vencido, StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.Replace('_', ' '), "NO VIGENTE", StringComparison.OrdinalIgnoreCase))
        {
            return Vencido;
        }

        return Unknown;
    }
}
