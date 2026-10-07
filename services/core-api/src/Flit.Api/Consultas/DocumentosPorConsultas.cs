using Flit.Consultas.Grpc.V1;
using Flit.Modules.Improntas.Domain;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Grpc.Core;

namespace Flit.Api.Consultas;

/// <summary>
/// HU #13348 (Epic #13316; ADR-0065): lo que core-api pedía directo a los proveedores — el RUNT crudo de la Confirmación
/// RUNT, la impronta de Kyverum RUNT y el certificado RUES — va ahora a core-consultas, que tiene los clientes y los
/// secretos. Cada cliente conserva el contrato que ya consumían los handlers: mismo resultado, mismas excepciones.
/// </summary>
internal static class DocumentosPorConsultas
{
    /// <summary>La empresa por la que se pide va explícita; sin ella, la del usuario de la petición (interceptor del SDK).</summary>
    internal static Metadata? Empresa(Guid? tenantId) =>
        tenantId is { } t && t != Guid.Empty ? new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, t.ToString() } } : null;

    /// <summary>Consultas caído o rechazando: transitorio salvo entrada inválida o permisos.</summary>
    internal static bool EsTransitorio(RpcException ex) =>
        ex.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.Unauthenticated or StatusCode.FailedPrecondition);
}

/// <summary>Confirmación RUNT: el crudo del vehículo por <c>ConsultarVehiculoCrudo</c>. Nunca lanza al runner.</summary>
internal sealed class RuntCrudoPorConsultas(ConsultasService.ConsultasServiceClient client) : IRuntVehicleRawClient
{
    public async Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var request = new ConsultarVehiculoCrudoRequest { Proveedor = providerKey };
        if (query.Vin is not null)
            request.Vin = new Flit.Platform.Grpc.V1.Vin { Valor = query.Vin };
        if (query.Plate is not null)
            request.Placa = new Flit.Platform.Grpc.V1.Placa { Valor = query.Plate };
        if (query.Document is not null)
            request.Propietario = new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = query.Document.Type, Numero = query.Document.Number };

        try
        {
            var r = await client.ConsultarVehiculoCrudoAsync(request, DocumentosPorConsultas.Empresa(query.TenantId), cancellationToken: ct).ConfigureAwait(false);
            var outcome = r.Resultado switch
            {
                ResultadoCrudo.Encontrado => RuntRawOutcome.Found,
                ResultadoCrudo.NoEncontrado => RuntRawOutcome.NotFound,
                _ => RuntRawOutcome.Error,
            };
            return new RuntRawQueryResult(outcome, r.HasRespuestaCruda ? r.RespuestaCruda : null, r.HasMensaje ? r.Mensaje : null);
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            // Igual que un proveedor caído: intento «error», no consume intento y se reintenta en la siguiente corrida.
            return RuntRawQueryResult.Error($"El servicio de consultas no respondió ({(ex is RpcException rpc ? rpc.StatusCode.ToString() : "sin token")})");
        }
    }
}

/// <summary>Impronta de Kyverum RUNT por <c>GenerarImpronta</c>; los fallos llegan como <see cref="ImprontaRuntException"/>.</summary>
internal sealed class ImprontaPorConsultas(ConsultasService.ConsultasServiceClient client) : IImprontaExternalClient
{
    public async Task<ImprontaExternalResult> GenerarAsync(ImprontaExternalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pedido = new GenerarImprontaRequest { Documento = request.Documento, OrgNombre = request.OrgNombre, Operador = request.Operador };
        if (request.Placa is not null) pedido.Placa = request.Placa;
        if (request.NumMotor is not null) pedido.NumMotor = request.NumMotor;
        if (request.NumChasis is not null) pedido.NumChasis = request.NumChasis;
        if (request.NumSerie is not null) pedido.NumSerie = request.NumSerie;
        if (request.Marca is not null) pedido.Marca = request.Marca;
        if (request.Linea is not null) pedido.Linea = request.Linea;
        if (request.Modelo is not null) pedido.Modelo = request.Modelo;
        if (request.OrgNit is not null) pedido.OrgNit = request.OrgNit;
        if (request.OrgCiudad is not null) pedido.OrgCiudad = request.OrgCiudad;
        if (request.Vin is not null) pedido.Vin = request.Vin;

        GenerarImprontaResponse r;
        try
        {
            r = await client.GenerarImprontaAsync(pedido, DocumentosPorConsultas.Empresa(request.TenantId), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            throw new ImprontaRuntException("UPSTREAM_UNAVAILABLE: el servicio de consultas no respondió.", DocumentosPorConsultas.EsTransitorio(ex));
        }

        if (r.Error is { } error)
            throw new ImprontaRuntException(error.Mensaje, error.Transitorio);
        return new ImprontaExternalResult(r.PdfDataUri, r.Hash, r.Radicado, r.FechaImpresa);
    }
}

/// <summary>Certificado RUES por <c>GenerarCertificadoRues</c>; «no habilitado» sigue siendo el respaldo de carga manual.</summary>
internal sealed class RuesPorConsultas(ConsultasService.ConsultasServiceClient client) : IRuesExternalClient
{
    public async Task<RuesExternalResult> GenerarAsync(RuesExternalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pedido = new GenerarCertificadoRuesRequest { Nit = request.Nit };
        if (request.RazonSocial is not null)
            pedido.RazonSocial = request.RazonSocial;

        GenerarCertificadoRuesResponse r;
        try
        {
            r = await client.GenerarCertificadoRuesAsync(pedido, DocumentosPorConsultas.Empresa(request.TenantId), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            throw new RuesExternalException("El servicio de consultas no respondió.", DocumentosPorConsultas.EsTransitorio(ex));
        }

        if (!r.Habilitado)
            throw new RuesExternalException("La autogeneración del Certificado RUES no está habilitada.", isTransient: false, autogenDeshabilitada: true);
        if (r.Error is { } error)
            throw new RuesExternalException(error.Mensaje, error.Transitorio);
        return new RuesExternalResult(r.PdfDataUri, r.HasRadicado ? r.Radicado : null);
    }
}
