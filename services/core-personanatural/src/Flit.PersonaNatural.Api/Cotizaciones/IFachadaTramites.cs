using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Cotizaciones;

public sealed record OrganismoDto(string Id, string Codigo, string Nombre, string Municipio, string Departamento, string EnlaceTarifas);

public sealed record TipoTramiteDto(string Code, string Name, string Family, int Version, bool Disponible, string MotivoNoDisponible);

public sealed record LineaCobroDto(string Codigo, string Nombre, string Subtitulo, long ValorCop);

public sealed record CotizacionDto(IReadOnlyList<LineaCobroDto> Lineas, long TotalCop, string VersionTarifa, IReadOnlyList<string> AdicionalesNoAplicados);

/// <summary>Error de la fachada ya traducido: el <c>Codigo</c> es la razón estable de FLIT 2 (<c>tarifa_no_vigente</c>, …).</summary>
public sealed class FachadaException(string codigo, string mensaje, int estadoHttp) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;

    public int EstadoHttp { get; } = estadoHttp;
}

/// <summary>
/// Lo que el BFF le pide a la fachada gRPC de core-api (HU #14043). Una interfaz para poder probarlo con una fachada falsa y
/// para que, sin dirección configurada, el servicio arranque con <see cref="FachadaTramitesNoConfigurada"/>.
/// </summary>
public interface IFachadaTramites
{
    Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct);

    Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct);

    Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct);
}

internal sealed class FachadaTramitesNoConfigurada : IFachadaTramites
{
    private static FachadaException NoDisponible() => new("servicio_no_disponible", "El catálogo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);

    public Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct) => throw NoDisponible();

    public Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct) => throw NoDisponible();

    public Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct) => throw NoDisponible();
}

/// <summary>
/// Cliente de la fachada <c>PersonaNaturalService</c> de core-api con el token de servicio del SDK (scope
/// <c>platform.tramites.persona_natural</c>) y la empresa de Persona Natural en la metadata. No reenvía nada genérico: solo
/// los tres métodos del catálogo.
/// </summary>
internal sealed class FachadaTramitesGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaTramites
{
    public const string Scope = "platform.tramites.persona_natural";

    private static Metadata Empresa() =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public async Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct)
    {
        var r = await Llamar(() => cliente.ListarOrganismosAsync(new ListarOrganismosRequest(), Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return [.. r.Organismos.Select(o => new OrganismoDto(o.Id, o.Codigo, o.Nombre, o.Municipio, o.Departamento, o.EnlaceTarifas))];
    }

    public async Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct)
    {
        var r = await Llamar(() => cliente.ListarTiposDisponiblesAsync(
            new ListarTiposDisponiblesRequest { OrganismoId = organismoId ?? string.Empty }, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return [.. r.Tipos.Select(t => new TipoTramiteDto(t.Codigo, t.Nombre, t.Familia, t.Version, t.DisponiblePn, t.MotivoNoDisponible))];
    }

    public async Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct)
    {
        var request = new CotizarRequest { TipoTramiteCodigo = tipoCodigo, OrganismoId = organismoId };
        request.Adicionales.AddRange(adicionales);
        var r = await Llamar(() => cliente.CotizarAsync(request, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return new CotizacionDto(
            [.. r.Lineas.Select(l => new LineaCobroDto(l.Codigo, l.Nombre, l.Subtitulo, l.ValorCop))], r.TotalCop, r.VersionTarifa, [.. r.AdicionalesNoAplicados]);
    }

    /// <summary>Traduce el estado gRPC a un error con código estable y estado HTTP; nada del detalle interno sale al ciudadano.</summary>
    internal static async Task<T> Llamar<T>(Func<Task<T>> llamada)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var razon = PlatformRpcErrors.Reason(ex);
            throw razon switch
            {
                "tipo_no_disponible" => new FachadaException(razon, "El tipo de trámite no está disponible.", StatusCodes.Status404NotFound),
                "organismo_no_habilitado" => new FachadaException(razon, "El organismo de tránsito no está habilitado.", StatusCodes.Status422UnprocessableEntity),
                "tipo_no_habilitado" => new FachadaException(razon, "Este trámite no está habilitado en el organismo elegido.", StatusCodes.Status422UnprocessableEntity),
                "tarifa_no_vigente" => new FachadaException(razon, "Este trámite aún no tiene tarifa en el organismo elegido.", StatusCodes.Status422UnprocessableEntity),
                _ when ex.StatusCode == StatusCode.InvalidArgument => new FachadaException("datos_invalidos", "Revise los datos de la cotización.", StatusCodes.Status400BadRequest),
                _ => new FachadaException("servicio_no_disponible", "El catálogo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable),
            };
        }
    }
}
