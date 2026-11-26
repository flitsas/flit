using System.Text.Json.Nodes;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Flit.Platform.Sdk.IaConsumo;
using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// HU #12036 — sonda de orientación contra el modelo de visión. Es deliberadamente pequeña: mira UNA
/// página, no pide leer nada y responde un booleano, así que cuesta ~3.300 tokens de entrada y
/// unas decenas de salida. Ese precio es lo que compra no enviar a analizar un documento que el modelo
/// no puede leer y sobre el que respondería inventándose los datos.
/// <para>Va por el router con la herramienta <c>ocr</c>: usa el proveedor activo del OCR y su cola de
/// respaldo, igual que la lectura que viene después. Antes llamaba siempre a Anthropic, así que con
/// Anthropic caído el documento se analizaba sin enderezar aunque el OCR sí cambiara de proveedor. Los
/// topes (salida y espera) los pone cada proveedor según <see cref="AiVisionPerfil.Sonda"/>.</para>
/// </summary>
internal sealed class RoutedOrientationProbe(
    IAiProviderRouter router,
    ILogger<RoutedOrientationProbe> logger,
    IIaCallContext? iaContexto = null) : IDocumentOrientationProbe
{
    private readonly IIaCallContext _ia = iaContexto ?? IaCallContext.Instancia;

    public async Task<PageOrientation> ProbeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(pdf.Span);
        // ADR-0090: cada sonda (hasta cuatro por documento) es una fila de tramites/orientacion_pagina.
        using var ambito = IaAmbitosOcr.Abrir(
            _ia, IaProcesosFlit.OrientacionPagina, IaTiposTarea.Orientacion, "orientacion", pdf.Span, "application/pdf");
        var vision = await router
            .InvokeVisionAsync(
                AiToolCodes.Ocr, base64, "application/pdf", DocumentOcrPrompts.OrientationProbePrompt, ct,
                perfil: AiVisionPerfil.Sonda)
            .ConfigureAwait(false);

        if (!vision.Ok)
            return PageOrientation.Unknown;

        if (OcrModelJson.ExtractObject(vision.Text) is not { } obj)
        {
            OrientationProbeLog.ParseFailed(logger, vision.ProviderCode ?? "?");
            return PageOrientation.Unknown;
        }

        // Solo un false explícito significa «girada». Un campo ausente o de otro tipo es Unknown, y
        // Unknown deja el documento como está: ante la duda no se toca lo que el usuario subió.
        return obj["derecha"] switch
        {
            JsonValue v when v.TryGetValue<bool>(out var derecha) => derecha ? PageOrientation.Upright : PageOrientation.Rotated,
            _ => PageOrientation.Unknown,
        };
    }
}

/// <summary>Logging source-generated (CA1848). No loguea el contenido del documento.</summary>
internal static partial class OrientationProbeLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sonda de orientación: no se pudo parsear la respuesta del modelo (provider {Provider})")]
    public static partial void ParseFailed(ILogger logger, string provider);
}
