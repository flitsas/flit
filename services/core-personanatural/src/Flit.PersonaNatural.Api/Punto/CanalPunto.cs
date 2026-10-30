using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Lo que core-api responde al resolver el código público del organismo (HU #14216).</summary>
public sealed record ResolucionCanalPunto(string Estado, Guid OrganismoId, string Codigo, string Nombre);

/// <summary>Resolución del canal del punto contra la fachada de core-api. El BFF no lee la tabla.</summary>
public interface ICanalPunto
{
    Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct);
}

internal sealed class CanalPuntoNoConfigurado : ICanalPunto
{
    public Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct) =>
        throw new FachadaException("servicio_no_disponible", "El canal del punto no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
}

internal sealed class CanalPuntoGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : ICanalPunto
{
    public async Task<ResolucionCanalPunto> ResolverAsync(string codigo, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ResolverCanalPuntoAsync(
            new ResolverCanalPuntoRequest { Codigo = codigo },
            new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } },
            cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        _ = Guid.TryParse(r.OrganismoId, out var id);
        return new ResolucionCanalPunto(r.Estado, id, r.Codigo, r.Nombre);
    }
}
