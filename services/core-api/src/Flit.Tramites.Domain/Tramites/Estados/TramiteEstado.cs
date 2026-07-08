namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Estados de NEGOCIO del ciclo de vida del trámite (N 03, RF01). Son los valores que se
/// persisten en <c>tramites.procedure_instances.status</c> (en español, snake_case-safe) y
/// los que expone la API. Reemplazan al vocabulario draft/submitted/... (ADR-0022).
/// </summary>
public static class TramiteEstado
{
    public const string Borrador = "borrador";
    public const string Anulado = "anulado";
    public const string Preparado = "preparado";
    public const string Entregado = "entregado";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";

    // Ruta de preasignación de placa (Feature #10587, matrícula inicial):
    /// <summary>El trámite se envió al OT y espera que este le asigne una placa (Flujo B, sin rango).</summary>
    public const string Preasignado = "preasignado";
    /// <summary>El trámite ya tiene placa asignada al VIN (Flujo A directo, o Flujo B tras el OT). Pendiente de SOAT + recepción del OT.</summary>
    public const string Asignado = "asignado";

    /// <summary>Todos los estados válidos (para validación de entrada y checks DDL).</summary>
    public static readonly IReadOnlyList<string> Todos =
        [Borrador, Anulado, Preparado, Entregado, Aprobado, Rechazado, Preasignado, Asignado];

    /// <summary>Estados FINALES (RF04): sin transiciones posteriores ni edición de datos.</summary>
    public static readonly IReadOnlyList<string> Finales = [Aprobado, Anulado];

    /// <summary>¿<paramref name="estado"/> es un estado de negocio conocido?</summary>
    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);

    /// <summary>¿<paramref name="estado"/> es final (RF04)? Aprobado y Anulado son inmutables.</summary>
    public static bool EsFinal(string? estado) =>
        estado is Aprobado or Anulado;
}
