using Microsoft.AspNetCore.Mvc;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Tope de tamaño del cuerpo de una ruta, en dos capas: <see cref="CuerpoAcotado{TBuilder}"/> pone <c>RequestSizeLimit</c> para que
/// Kestrel corte antes de leer, y <see cref="LeerAsync"/> rechaza un <c>Content-Length</c> mayor sin leer nada y, sin él (por
/// trozos), deja de leer al pasar el tope. En los dos casos responde 413 <see cref="CodigoMuyGrande"/>. Topes del plan: JSON del
/// cliente <see cref="MaximoJson"/> y aviso del webhook <see cref="MaximoAviso"/> (256 KB, §1.5).
/// </summary>
/// <example>
/// <code>
/// api.MapPost("/avisos/{token}", Recibir).CuerpoAcotado(CuerpoAcotadoFiltro.MaximoAviso);
/// await using var cuerpo = await CuerpoAcotadoFiltro.LeerAsync(request, CuerpoAcotadoFiltro.MaximoAviso, ct);
/// </code>
/// </example>
internal static class CuerpoAcotadoFiltro
{
    public const int MaximoJson = 64 * 1024;
    public const int MaximoAviso = 256 * 1024;
    public const string CodigoMuyGrande = "CUERPO_MUY_GRANDE";

    public static TBuilder CuerpoAcotado<TBuilder>(this TBuilder builder, int maximoBytes = MaximoJson)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequestSizeLimitAttribute(maximoBytes));

    /// <summary>El cuerpo entero en memoria (a lo sumo <paramref name="maximoBytes"/>), en la posición 0; 413 si lo pasa.</summary>
    public static async Task<MemoryStream> LeerAsync(HttpRequest request, int maximoBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength > maximoBytes)
            throw MuyGrande(maximoBytes);
        var memoria = new MemoryStream();
        var bufer = new byte[8192];
        try
        {
            int leidos;
            while ((leidos = await request.Body.ReadAsync(bufer, ct).ConfigureAwait(false)) > 0)
            {
                if (memoria.Length + leidos > maximoBytes)
                    throw MuyGrande(maximoBytes);
                memoria.Write(bufer, 0, leidos);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await memoria.DisposeAsync().ConfigureAwait(false);
            throw MuyGrande(maximoBytes);
        }
        catch
        {
            await memoria.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        memoria.Position = 0;
        return memoria;
    }

    private static ReglaException MuyGrande(int maximoBytes) =>
        new(CodigoMuyGrande, $"El cuerpo admite hasta {maximoBytes / 1024} KB.", StatusCodes.Status413PayloadTooLarge);
}
