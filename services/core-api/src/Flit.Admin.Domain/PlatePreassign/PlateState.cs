namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// Estados del ciclo de vida de una placa del inventario de preasignación (Feature #10587).
/// Se persisten en <c>admin.plate_range_details.state</c>.
/// </summary>
public static class PlateState
{
    /// <summary>Disponible para ser tomada en una radicación o asignada por el OT.</summary>
    public const string Disponible = "disponible";

    /// <summary>Reservada a un trámite/VIN (Flujo A o B), pendiente de aprobación en RUNT.</summary>
    public const string Preasignada = "preasignada";

    /// <summary>Matrícula aprobada en RUNT: la placa quedó utilizada (terminal).</summary>
    public const string Utilizada = "utilizada";

    /// <summary>Bloqueada manualmente por el OT: no se ofrece en la radicación.</summary>
    public const string Bloqueada = "bloqueada";

    /// <summary>Preasignación revocada/rechazada: liberable de nuevo a disponible.</summary>
    public const string Revocada = "revocada";

    /// <summary>Todos los estados válidos (validación de entrada / checks).</summary>
    public static readonly IReadOnlyList<string> Todos =
        [Disponible, Preasignada, Utilizada, Bloqueada, Revocada];

    /// <summary>¿<paramref name="state"/> es un estado de placa conocido?</summary>
    public static bool EsValido(string? state) =>
        state is not null && Todos.Contains(state, StringComparer.Ordinal);
}
