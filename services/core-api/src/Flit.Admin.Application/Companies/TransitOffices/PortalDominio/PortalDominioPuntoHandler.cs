using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

/// <summary>
/// Dominio activo de un organismo tal como lo ve el portal. Nunca revela por qué un host no abre un portal: pendiente, fallido,
/// retirado, reservado o desconocido responden igual (<c>Activo = false</c>).
/// </summary>
public sealed record PortalDominioPuntoDto(bool Activo, string? Codigo, string? Host)
{
    public static readonly PortalDominioPuntoDto Ninguno = new(false, null, null);
}

/// <summary>
/// HU #14383 — entrega del dominio activo al portal: del host al código del organismo (la raíz del dominio pinta ese punto) y del
/// código al host (enlace, QR y enlaces de correo usan el dominio). Sin ninguna caché: lo recién activado o retirado se ve en la
/// siguiente lectura. Ante cualquier falla responde «sin dominio» y el portal sigue en su enlace del punto.
/// </summary>
public sealed partial class PortalDominioPuntoHandler(IPortalDominioRepository repo, ILogger<PortalDominioPuntoHandler> logger)
{
    private const int CodigoMaxLargo = 20;

    public async Task<PortalDominioPuntoDto> PorHostAsync(string? host, CancellationToken ct = default)
    {
        if (!HostNormalizer.TryNormalize(host, out var normalizado, out _))
            return PortalDominioPuntoDto.Ninguno;

        try
        {
            return await repo.ObtenerOrganismoActivoPorHostAsync(normalizado, ct).ConfigureAwait(false) is { } organismo
                ? new PortalDominioPuntoDto(true, organismo.Codigo, normalizado)
                : PortalDominioPuntoDto.Ninguno;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LecturaFallida(logger, ex);
            return PortalDominioPuntoDto.Ninguno;
        }
    }

    public async Task<PortalDominioPuntoDto> PorCodigoAsync(string? codigo, CancellationToken ct = default)
    {
        var buscado = (codigo ?? string.Empty).Trim();
        if (buscado.Length is 0 or > CodigoMaxLargo)
            return PortalDominioPuntoDto.Ninguno;

        try
        {
            return await repo.ObtenerHostActivoPorCodigoAsync(buscado, ct).ConfigureAwait(false) is { } host
                ? new PortalDominioPuntoDto(true, buscado.ToUpperInvariant(), host)
                : PortalDominioPuntoDto.Ninguno;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LecturaFallida(logger, ex);
            return PortalDominioPuntoDto.Ninguno;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo leer el dominio del portal; se responde sin dominio y el portal sigue en su enlace del punto.")]
    private static partial void LecturaFallida(ILogger logger, Exception ex);
}
