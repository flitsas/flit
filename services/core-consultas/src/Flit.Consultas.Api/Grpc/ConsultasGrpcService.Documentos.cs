using Flit.Consultas.Grpc.V1;
using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Grpc.Core;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// HU #13348 (Epic #13316): lo que core-api aún hacía en proceso contra los proveedores — el RUNT crudo de la
/// Confirmación RUNT, la impronta de Kyverum RUNT y el certificado RUES — con los mismos clientes que tenía core-api.
/// Un error del proveedor viaja en la respuesta (no como error gRPC), con su clasificación de reintentable.
/// </summary>
internal sealed partial class ConsultasGrpcService
{
    public override Task<ConsultarVehiculoCrudoResponse> ConsultarVehiculoCrudo(ConsultarVehiculoCrudoRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "vehiculo_crudo", () => ConsultarVehiculoCrudoCore(request, context), r => (
            request.Proveedor,
            r.Resultado switch
            {
                ResultadoCrudo.Encontrado => "verde",
                ResultadoCrudo.NoEncontrado => "rojo",
                _ => "error",
            },
            false));

    public override Task<GenerarImprontaResponse> GenerarImpronta(GenerarImprontaRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "impronta", () => GenerarImprontaCore(request, context), r => ("kyverum_runt", r.Error is null ? "verde" : "error", false));

    public override Task<GenerarCertificadoRuesResponse> GenerarCertificadoRues(GenerarCertificadoRuesRequest request, ServerCallContext context) =>
        consumo.MedirAsync(context, "certificado_rues", () => GenerarCertificadoRuesCore(request, context), r => ("rues", r.Habilitado && r.Error is null ? "verde" : "error", false));

    private async Task<ConsultarVehiculoCrudoResponse> ConsultarVehiculoCrudoCore(ConsultarVehiculoCrudoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RuntConfirmationProviderKeys.IsValid(request.Proveedor))
            throw Invalido($"Proveedor no soportado: {request.Proveedor}.");

        var vin = request.Vin?.Valor;
        var placa = request.Placa?.Valor;
        RuntRawQuery query;
        if (!string.IsNullOrWhiteSpace(vin))
            query = RuntRawQuery.ByVin(vin);
        else if (!string.IsNullOrWhiteSpace(placa) && !string.IsNullOrWhiteSpace(request.Propietario?.Numero))
            query = RuntRawQuery.ByPlate(placa, new RuntDocument(request.Propietario.Tipo, request.Propietario.Numero));
        else
            throw Invalido("Se necesita el VIN, o la placa con el documento del propietario.");

        var resultado = await runtCrudo.ConsultAsync(request.Proveedor, query, context.CancellationToken).ConfigureAwait(false);
        var response = new ConsultarVehiculoCrudoResponse
        {
            Resultado = resultado.Outcome switch
            {
                RuntRawOutcome.Found => ResultadoCrudo.Encontrado,
                RuntRawOutcome.NotFound => ResultadoCrudo.NoEncontrado,
                _ => ResultadoCrudo.Error,
            },
        };
        if (resultado.RawJson is not null)
            response.RespuestaCruda = resultado.RawJson;
        if (resultado.Message is not null)
            response.Mensaje = resultado.Message;
        return response;
    }

    private async Task<GenerarImprontaResponse> GenerarImprontaCore(GenerarImprontaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Documento))
            throw Invalido("Se necesita el documento del propietario.");

        try
        {
            var r = await improntas.GenerarAsync(
                new ImprontaExternalRequest(
                    request.HasPlaca ? request.Placa : null,
                    request.Documento,
                    request.HasNumMotor ? request.NumMotor : null,
                    request.HasNumChasis ? request.NumChasis : null,
                    request.HasNumSerie ? request.NumSerie : null,
                    request.HasMarca ? request.Marca : null,
                    request.HasLinea ? request.Linea : null,
                    request.HasModelo ? request.Modelo : null,
                    request.OrgNombre,
                    request.HasOrgNit ? request.OrgNit : null,
                    request.HasOrgCiudad ? request.OrgCiudad : null,
                    request.Operador,
                    request.HasVin ? request.Vin : null),
                context.CancellationToken).ConfigureAwait(false);
            return new GenerarImprontaResponse { PdfDataUri = r.PdfDataUri, Hash = r.Hash, Radicado = r.Radicado, FechaImpresa = r.FechaImpresa };
        }
        catch (ImprontaRuntException ex)
        {
            return new GenerarImprontaResponse { Error = new ErrorProveedor { Mensaje = ex.Message, Transitorio = ex.IsTransient } };
        }
    }

    private async Task<GenerarCertificadoRuesResponse> GenerarCertificadoRuesCore(GenerarCertificadoRuesRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Nit))
            throw Invalido("Se necesita el NIT.");

        // Sin RUES_ENABLED/RUES_BASE_URL el cliente no se registra: Trámites cae a la carga manual.
        if (rues.LastOrDefault() is not { } cliente)
            return new GenerarCertificadoRuesResponse { Habilitado = false };

        try
        {
            var r = await cliente.GenerarAsync(
                new RuesExternalRequest(request.Nit, request.HasRazonSocial ? request.RazonSocial : null), context.CancellationToken).ConfigureAwait(false);
            var response = new GenerarCertificadoRuesResponse { Habilitado = true, PdfDataUri = r.PdfDataUri };
            if (r.Radicado is not null)
                response.Radicado = r.Radicado;
            return response;
        }
        catch (RuesExternalException ex)
        {
            return new GenerarCertificadoRuesResponse { Habilitado = true, Error = new ErrorProveedor { Mensaje = ex.Message, Transitorio = ex.IsTransient } };
        }
    }
}
