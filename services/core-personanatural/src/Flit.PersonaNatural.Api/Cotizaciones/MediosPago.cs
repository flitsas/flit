using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Cotizaciones;

/// <summary>Un medio de pago que el portal ofrece, con el total base (derechos de tránsito y servicio FLIT encendidos, sin adicionales).</summary>
public sealed record MedioPagoDto(string Medio, long TotalCop, IReadOnlyList<LineaCobroDto> Lineas);

/// <summary>
/// Medios de pago y cotización por medio (HU #14395, Épica #14297). Va aparte de <see cref="IFachadaTramites"/> para no tocar
/// a quienes ya la usan: sin medio, la cotización sigue saliendo por <see cref="IFachadaTramites.CotizarAsync"/>, igual que hoy.
/// <c>ambitoOrganismoId</c> nulo es la web de persona; con valor, el punto de ese organismo (el único con PIN).
/// </summary>
public interface IFachadaMediosPago
{
    Task<IReadOnlyList<MedioPagoDto>> ListarMediosPagoAsync(string tipoCodigo, string organismoId, string? ambitoOrganismoId, CancellationToken ct);

    Task<CotizacionDto> CotizarConMedioAsync(
        string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, string medio, string? ambitoOrganismoId, CancellationToken ct);
}

internal sealed class FachadaMediosPagoNoConfigurada : IFachadaMediosPago
{
    private static FachadaException NoDisponible() => new("servicio_no_disponible", "El catálogo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);

    public Task<IReadOnlyList<MedioPagoDto>> ListarMediosPagoAsync(string tipoCodigo, string organismoId, string? ambitoOrganismoId, CancellationToken ct) => throw NoDisponible();

    public Task<CotizacionDto> CotizarConMedioAsync(
        string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, string medio, string? ambitoOrganismoId, CancellationToken ct) => throw NoDisponible();
}

/// <summary>Cliente de los RPC de medios de pago de core-api (mismo cliente, token y empresa que el catálogo). Sin caché.</summary>
internal sealed class FachadaMediosPagoGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaMediosPago
{
    private static global::Grpc.Core.Metadata Empresa() =>
        new() { { Flit.Platform.Sdk.Grpc.PlatformServiceCallInterceptor.TenantMetadata, Flit.Platform.Sdk.PlatformTenants.PersonaNatural.ToString() } };

    public async Task<IReadOnlyList<MedioPagoDto>> ListarMediosPagoAsync(string tipoCodigo, string organismoId, string? ambitoOrganismoId, CancellationToken ct)
    {
        var request = new ListarMediosPagoRequest { TipoTramiteCodigo = tipoCodigo, OrganismoId = organismoId, AmbitoOrganismoId = ambitoOrganismoId ?? string.Empty };
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ListarMediosPagoAsync(request, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return [.. r.Medios.Select(m => new MedioPagoDto(m.Medio, m.TotalCop, [.. m.Lineas.Select(l => new LineaCobroDto(l.Codigo, l.Nombre, l.Subtitulo, l.ValorCop))]))];
    }

    public async Task<CotizacionDto> CotizarConMedioAsync(
        string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, string medio, string? ambitoOrganismoId, CancellationToken ct)
    {
        var request = new CotizarRequest { TipoTramiteCodigo = tipoCodigo, OrganismoId = organismoId, Medio = medio, AmbitoOrganismoId = ambitoOrganismoId ?? string.Empty };
        request.Adicionales.AddRange(adicionales);
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.CotizarAsync(request, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return new CotizacionDto(
            [.. r.Lineas.Select(l => new LineaCobroDto(l.Codigo, l.Nombre, l.Subtitulo, l.ValorCop))], r.TotalCop, r.VersionTarifa, [.. r.AdicionalesNoAplicados]);
    }
}

/// <summary>Registro de la fachada de medios de pago: el cliente gRPC solo existe si core-api está configurado.</summary>
internal static class MediosPagoRegistro
{
    public static void Registrar(IServiceCollection services, bool coreApiConfigurado)
    {
        if (coreApiConfigurado)
            services.TryAddSingleton<IFachadaMediosPago, FachadaMediosPagoGrpc>();
        else
            services.TryAddSingleton<IFachadaMediosPago, FachadaMediosPagoNoConfigurada>();
    }
}
