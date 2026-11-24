using System.Text;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// Doble de <see cref="ClienteConsultas"/> para la prueba de fuga de la descarga de liquidación del impuesto (D5): ejecuta la
/// llamada real contra un cliente gRPC falso que devuelve un PDF mínimo con el <c>archivo_id</c> pedido dentro. Así la lectura
/// propia de A trae su <c>fileId</c> (sale del payload de su celda) y la de B con el token de A demuestra que no cruza.
/// Solo atiende <c>DescargarLiquidacionImpuesto</c>; cualquier otro rpc falla.
/// </summary>
/// <example>
/// <code>
/// b.Services.RemoveAll&lt;ClienteConsultas&gt;();
/// b.Services.AddSingleton&lt;ClienteConsultas&gt;(new ClienteConsultasDeFuga());
/// </code>
/// </example>
public sealed class ClienteConsultasDeFuga() : ClienteConsultas(null!, NullLogger<ClienteConsultas>.Instance)
{
    public override async Task<RespuestaConsultas<T>> LlamarAsync<T>(
        Guid empresa, Func<ConsultasService.ConsultasServiceClient, CallOptions, AsyncUnaryCall<T>> llamada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(llamada);
        using var call = llamada(new GrpcEco(), new CallOptions(cancellationToken: ct));
        return new RespuestaConsultas<T>(await call.ResponseAsync.ConfigureAwait(false), null);
    }

    /// <summary>PDF mínimo válido (cabecera y <c>%%EOF</c>) con el identificador de la liquidación como comentario.</summary>
    public static byte[] Pdf(string archivoId) => Encoding.ASCII.GetBytes($"%PDF-1.4\n% liquidacion {archivoId}\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private sealed class GrpcEco : ConsultasService.ConsultasServiceClient
    {
        public override AsyncUnaryCall<DescargarLiquidacionImpuestoResponse> DescargarLiquidacionImpuestoAsync(
            DescargarLiquidacionImpuestoRequest request, CallOptions options)
        {
            var respuesta = new DescargarLiquidacionImpuestoResponse
            {
                Contenido = ByteString.CopyFrom(Pdf(request.ArchivoId)),
                ContentType = "application/pdf",
            };
            return new AsyncUnaryCall<DescargarLiquidacionImpuestoResponse>(
                Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }
}
