using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// <c>ResolverUsuarioCaja</c> de core-api (mismo cliente, token y scope que la fachada del catálogo). Se pregunta en CADA
/// petición, sin caché: un usuario desactivado pierde el acceso en el siguiente request.
/// </summary>
internal sealed class ResolverCajeroGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IResolverCajero
{
    public async Task<Cajero?> ResolverAsync(Guid tenantId, Guid usuarioId, CancellationToken ct)
    {
        var metadata = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };
        try
        {
            var r = await cliente.ResolverUsuarioCajaAsync(
                new ResolverUsuarioCajaRequest { TenantId = tenantId.ToString(), UserId = usuarioId.ToString() }, metadata, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
            return r.Activo && Guid.TryParse(r.OrganismoId, out var organismo) && organismo != Guid.Empty
                ? new Cajero(usuarioId, r.UsuarioNombre, organismo, r.OrganismoCodigo, r.OrganismoNombre)
                : null;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.PermissionDenied)
        {
            return null;
        }
        catch (RpcException)
        {
            throw new FachadaException("servicio_no_disponible", "La caja no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
        }
    }
}

internal sealed class ResolverCajeroNoConfigurado : IResolverCajero
{
    public Task<Cajero?> ResolverAsync(Guid tenantId, Guid usuarioId, CancellationToken ct) =>
        throw new FachadaException("servicio_no_disponible", "La caja no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
}
