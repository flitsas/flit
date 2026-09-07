namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Procedencia del proveedor externo de un adjunto (p. ej. impronta Kyverum RUNT).
/// Distinto de <c>Source</c> (hecho del trámite / company / system): no altera DT-4.
/// <c>null</c> en el adjunto = carga manual u origen no proveedor.
/// </summary>
public static class AttachmentProviders
{
    public const string Kyverum = "kyverum";

    public static bool IsKyverum(string? provider) =>
        string.Equals(provider, Kyverum, StringComparison.OrdinalIgnoreCase);
}
