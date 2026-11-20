using Flit.PersonaNatural.Api.Solicitudes;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Lo que el borrador ya declaró y con lo que se decide si un documento condicional aplica.</summary>
public sealed record ContextoCondicionPunto(
    bool TienePrenda,
    IReadOnlyList<string> Transformaciones,
    IReadOnlyDictionary<string, string> Campos,
    bool HayPersonaJuridica);

/// <summary>
/// Misma regla que el portal web (<c>frontend-personanatural/lib/tramite/requisitos.ts</c>: <c>documentoVisible</c>,
/// <c>condicionConocida</c>, <c>esExigible</c>). Una condición que el portal no reconoce no se exige: el servidor de
/// FLIT 2 decide al radicar. Mantener ambos lados sincronizados.
/// </summary>
public static class CondicionesDocumentoPunto
{
    /// <summary>El cobro está fuera de esta entrega: el comprobante de pago no es requisito aunque la definición lo liste.</summary>
    public const string DocComprobantePago = "comprobante_pago";

    public static bool Visible(DocumentoRequeridoDto doc, ContextoCondicionPunto ctx)
    {
        var c = (doc.Condicion ?? string.Empty).Trim().ToLowerInvariant();
        if (c.Length == 0) return true;
        if (c.Contains("persona natural")) return false;
        if (c.Contains("persona jurídica") || c.Contains("persona juridica") || c.Contains("nit")) return !ctx.HayPersonaJuridica;
        if (c.Contains("tramitador") || c.Contains("mandato")) return false;
        if (c.Contains("causal de cancelación") || c.Contains("causal de cancelacion")) return false;
        if (c.Contains("leasing")) return Menciona(ctx, "leasing");
        if (c.Contains("prenda")) return ctx.TienePrenda;
        if (c.Contains("servicio especial")) return Menciona(ctx, "especial");
        if (c.Contains("carrocer")) return ctx.Transformaciones.Any(t => string.Equals(t, "CAMBIO_CARROCERIA", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public static bool CondicionConocida(DocumentoRequeridoDto doc)
    {
        var c = (doc.Condicion ?? string.Empty).Trim().ToLowerInvariant();
        if (c.Length == 0) return false;
        return c.Contains("persona natural") || c.Contains("persona jurídica") || c.Contains("persona juridica") || c.Contains("nit")
            || c.Contains("tramitador") || c.Contains("mandato") || c.Contains("causal de cancelación") || c.Contains("causal de cancelacion")
            || c.Contains("leasing") || c.Contains("prenda") || c.Contains("servicio especial") || c.Contains("carrocer");
    }

    /// <summary>Obligatorio y, si tiene condición que el portal entiende, solo cuando esa condición se cumple.</summary>
    public static bool Exigible(DocumentoRequeridoDto doc, ContextoCondicionPunto ctx)
    {
        if (doc.DocCode == DocComprobantePago || !doc.Obligatorio || !Visible(doc, ctx)) return false;
        // Documento condicionado de la decisión de prenda: lo reclama solo la decisión guardada (ReglasBorrador.DocumentoDePrendaDelTipo).
        if (!string.IsNullOrWhiteSpace(doc.Condicion) && ReglasBorrador.DocumentosDePrenda.Contains(doc.DocCode)) return false;
        return string.IsNullOrWhiteSpace(doc.Condicion) || CondicionConocida(doc);
    }

    private static bool Menciona(ContextoCondicionPunto ctx, string texto) =>
        ctx.Campos.Any(kv => kv.Key.Contains(texto, StringComparison.OrdinalIgnoreCase) || (kv.Value ?? string.Empty).Contains(texto, StringComparison.OrdinalIgnoreCase));
}
