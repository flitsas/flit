using Flit.Platform.Sdk.IaConsumo;
using Flit.Tramites.Application.Ocr;

namespace Flit.Infrastructure.Ocr;

/// <summary>Adaptador del puerto <see cref="IIaConsumoAmbito"/> sobre el contexto ambiental del SDK (ADR-0090).</summary>
internal sealed class IaConsumoAmbitoTramites(IIaCallContext contexto) : IIaConsumoAmbito
{
    public IDisposable Abrir(string proceso, string? placa = null, string? entidadTipo = null, string? entidadId = null) =>
        contexto.Iniciar(new IaAmbito
        {
            Modulo = IaProcesosFlit.ModuloTramites,
            Proceso = proceso,
            TipoTarea = IaTiposTarea.Ocr,
            Placa = placa,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
        });
}

/// <summary>Ámbitos de las llamadas de visión de trámites (ADR-0090). Describen el archivo sin guardarlo.</summary>
internal static class IaAmbitosOcr
{
    /// <summary>
    /// OCR de un documento con el prompt de <paramref name="tipo"/>. Si quien llama ya abrió el proceso (endpoint del
    /// lote, Licencia de Tránsito) se respeta; sin ámbito previo queda como <c>tramites/ocr_documento</c>.
    /// </summary>
    public static IDisposable AbrirOcr(IIaCallContext ctx, string tipo, ReadOnlySpan<byte> contenido, string mime)
    {
        var actual = ctx.Actual?.Ambito;
        return ctx.Iniciar(IaConsumoTexto.DescribirArchivo(contenido, mime) with
        {
            Modulo = actual?.Modulo is null ? IaProcesosFlit.ModuloTramites : null,
            Proceso = actual?.Proceso is null ? IaProcesosFlit.OcrDocumento : null,
            TipoTarea = IaTiposTarea.Ocr,
            PromptCodigo = tipo,
        });
    }

    /// <summary>Proceso propio de <c>tramites</c> (clasificación del lote, enderezado de la página).</summary>
    public static IDisposable Abrir(
        IIaCallContext ctx, string proceso, string tipoTarea, string promptCodigo, ReadOnlySpan<byte> contenido, string mime) =>
        ctx.Iniciar(IaConsumoTexto.DescribirArchivo(contenido, mime) with
        {
            Modulo = IaProcesosFlit.ModuloTramites,
            Proceso = proceso,
            TipoTarea = tipoTarea,
            PromptCodigo = promptCodigo,
        });
}
