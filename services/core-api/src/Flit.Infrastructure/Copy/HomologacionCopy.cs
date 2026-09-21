namespace Flit.Infrastructure.Copy;

/// <summary>
/// Labels canónicos OT ↔ Gestor (épica #12551). Solo filas con Ganador publicado.
/// No sustituye el enum de estado; solo el texto que ve el destinatario.
/// </summary>
public static class HomologacionCopy
{
    /// <summary>D01 / catálogo B07.</summary>
    public const string Aprobado = "Aprobado";

    /// <summary>D02 / catálogo B08.</summary>
    public const string Rechazado = "Rechazado";

    /// <summary>A05 / D06 / E04.</summary>
    public const string OrganismoDeTransito = "Organismo de tránsito";

    /// <summary>A24.</summary>
    public const string Soat = "SOAT";

    /// <summary>A12 / D07.</summary>
    public const string VerConsolidado = "Ver consolidado";
}
