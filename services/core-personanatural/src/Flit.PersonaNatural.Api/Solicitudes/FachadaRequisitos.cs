using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>
/// Requisitos previos del tipo (preflight de FLIT 2) sin crear el trámite (HU #14047). Interfaz hermana de
/// <see cref="IFachadaTramites"/>: llama al RPC <c>EvaluarRequisitos</c>, que devuelve solo etiqueta y estado de cada chequeo.
/// </summary>
public interface IFachadaRequisitos
{
    Task<DiagnosticoDto> EvaluarAsync(string tipoCodigo, string organismoId, string? placa, string? vin, string? tipoDocumentoPropietario, string? documentoPropietario, CancellationToken ct);
}

internal sealed class FachadaRequisitosNoConfigurada : IFachadaRequisitos
{
    public Task<DiagnosticoDto> EvaluarAsync(string tipoCodigo, string organismoId, string? placa, string? vin, string? tipoDocumentoPropietario, string? documentoPropietario, CancellationToken ct) =>
        throw new FachadaException("servicio_no_disponible", "El diagnóstico no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
}

internal sealed class FachadaRequisitosGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaRequisitos
{
    public async Task<DiagnosticoDto> EvaluarAsync(string tipoCodigo, string organismoId, string? placa, string? vin, string? tipoDocumentoPropietario, string? documentoPropietario, CancellationToken ct)
    {
        var metadata = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };
        var peticion = new EvaluarRequisitosRequest
        {
            TipoTramiteCodigo = tipoCodigo,
            OrganismoId = organismoId,
            Placa = placa ?? string.Empty,
            Vin = vin ?? string.Empty,
            TipoDocumentoPropietario = tipoDocumentoPropietario ?? string.Empty,
            NumeroDocumentoPropietario = documentoPropietario ?? string.Empty,
        };
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.EvaluarRequisitosAsync(peticion, metadata, cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return new DiagnosticoDto(r.PuedeContinuar, [.. r.Requisitos.Select(x => new RequisitoDto(x.Codigo, x.Descripcion, x.Estado))]);
    }
}
