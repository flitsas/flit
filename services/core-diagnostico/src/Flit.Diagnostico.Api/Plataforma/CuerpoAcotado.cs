using Microsoft.AspNetCore.Mvc;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// CR-11 / SF-3 (saneamiento, HU #14576): tope propio de <see cref="MaximoBytes"/> (64 KiB) al cuerpo JSON de las rutas nuevas del
/// cliente y del backoffice. Dos capas:
/// <list type="number">
/// <item><see cref="CuerpoAcotado{TBuilder}"/> pone <c>RequestSizeLimit</c> en la ruta, para que Kestrel corte antes de leer.</item>
/// <item><see cref="LeerAsync"/> rechaza un <c>Content-Length</c> mayor sin leer nada y, sin <c>Content-Length</c> (por trozos), deja de
/// leer al pasar el tope. En los dos casos responde 413 <see cref="CodigoMuyGrande"/>, antes de parsear el JSON.</item>
/// </list>
/// 500 uuids (el masivo más grande) ocupan unos 20 KB. La subida de la evidencia no lo usa: tiene su propio tope.
/// </summary>
/// <example>
/// <code>
/// api.MapPost(RutaSolicitudes, Solicitar).CuerpoAcotado();
/// await using var cuerpo = await CuerpoAcotadoFiltro.LeerAsync(request, ct);
/// var raiz = await JsonSerializer.DeserializeAsync&lt;JsonElement&gt;(cuerpo, cancellationToken: ct);
/// </code>
/// </example>
internal static class CuerpoAcotadoFiltro
{
    public const int MaximoBytes = 64 * 1024;
    public const string CodigoMuyGrande = "CUERPO_MUY_GRANDE";

    public static TBuilder CuerpoAcotado<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequestSizeLimitAttribute(MaximoBytes));

    /// <summary>El cuerpo entero en memoria (a lo sumo <see cref="MaximoBytes"/>), en la posición 0; 413 si lo pasa.</summary>
    public static async Task<MemoryStream> LeerAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength > MaximoBytes)
            throw MuyGrande();
        var memoria = new MemoryStream();
        var bufer = new byte[8192];
        try
        {
            int leidos;
            while ((leidos = await request.Body.ReadAsync(bufer, ct).ConfigureAwait(false)) > 0)
            {
                if (memoria.Length + leidos > MaximoBytes)
                    throw MuyGrande();
                memoria.Write(bufer, 0, leidos);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await memoria.DisposeAsync().ConfigureAwait(false);
            throw MuyGrande();
        }
        catch
        {
            await memoria.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        memoria.Position = 0;
        return memoria;
    }

    private static ReglaException MuyGrande() =>
        new(CodigoMuyGrande, $"El cuerpo admite hasta {MaximoBytes / 1024} KB.", StatusCodes.Status413PayloadTooLarge);
}
