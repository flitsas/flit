namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// HU #13351 (ADR-0065 §6) — un aviso de proveedor tal como llegó, con el resultado de verificarlo. Traza de todo lo
/// que entra al receptor, válido o no.
/// </summary>
public sealed class AvisoProveedor
{
    public Guid Id { get; set; }

    public string Proveedor { get; set; } = string.Empty;

    /// <summary>Id de la URL del aviso.</summary>
    public Guid ReferenciaId { get; set; }

    /// <summary>Empresa de la validación; null si no se reconoció la referencia.</summary>
    public Guid? TenantId { get; set; }

    /// <summary><see cref="AvisoResultados"/>.</summary>
    public string Resultado { get; set; } = string.Empty;

    /// <summary>Cuerpo crudo en UTF-8, sin tocar.</summary>
    public string Cuerpo { get; set; } = string.Empty;

    public DateTimeOffset RecibidoEn { get; set; }
}

public static class AvisoResultados
{
    public const string Publicado = "publicado";
    public const string FirmaInvalida = "firma_invalida";
    public const string ReferenciaDesconocida = "referencia_desconocida";
}
