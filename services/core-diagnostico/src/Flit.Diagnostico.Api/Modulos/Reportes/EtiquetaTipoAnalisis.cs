using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Rótulo del tipo de análisis en el encabezado de los archivos (HU #13847, F07-C2, decisión Q4): Simple → «Diagnóstico Simple»;
/// Con Análisis IA → «Diagnóstico Con Análisis IA — » más el nombre del tipo de trámite en mayúsculas, del catálogo publicado
/// (<see cref="ICatalogoTiposTramite"/>); si el código ya no está publicado o el catálogo no responde, el código.
/// </summary>
/// <example>
/// <code>
/// EtiquetaTipoAnalisis.Texto(ModoDiagnostico.ConAnalisis, "Traspaso"); // "Diagnóstico Con Análisis IA — TRASPASO"
/// </code>
/// </example>
public static class EtiquetaTipoAnalisis
{
    public const string Simple = "Diagnóstico Simple";

    public const string PrefijoConAnalisis = "Diagnóstico Con Análisis IA — ";

    /// <summary>El rótulo del modo con el nombre (o código) del tipo de trámite ya resuelto.</summary>
    public static string Texto(ModoDiagnostico modo, string? tipoTramite) => modo switch
    {
        ModoDiagnostico.Simple => Simple,
        _ => PrefijoConAnalisis + (tipoTramite ?? string.Empty).Trim().ToUpperInvariant(),
    };

    /// <summary>El rótulo del lote, con el nombre del tipo leído del catálogo (el código si no está o el catálogo falla).</summary>
    public static async Task<string> ResolverAsync(CabeceraDiagnostico cabecera, ICatalogoTiposTramite catalogo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(catalogo);
        if (cabecera.Modo == ModoDiagnostico.Simple || cabecera.TipoTramiteCodigo is not { } codigo)
            return Texto(cabecera.Modo, cabecera.TipoTramiteCodigo);
        try
        {
            var publicados = await catalogo.ListarAsync(ct).ConfigureAwait(false);
            return Texto(cabecera.Modo, publicados.FirstOrDefault(t => string.Equals(t.Codigo, codigo, StringComparison.Ordinal))?.Nombre ?? codigo);
        }
        catch (CatalogoTiposTramiteNoDisponibleException)
        {
            return Texto(cabecera.Modo, codigo);
        }
    }
}
