using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Grpc;

/// <summary>
/// <c>flit.impuestos.v1.ImpuestosService</c> (plan §5.3): el resumen de una empresa para el hub, con la forma de
/// <c>ObtenerResumenEmpresa</c> de los demás productos. La empresa sale de la metadata <c>x-flit-tenant-id</c> ya validada por el
/// interceptor del SDK, y el filtro por empresa de <see cref="ImpuestosDb"/> la aplica. Se mapea en <c>Program</c> con
/// <c>RequireServiceToken</c> cuando el contrato de plataforma (§3) publique su scope y exista el primer consumidor; hasta entonces
/// no se expone.
/// </summary>
internal sealed class ImpuestosGrpcService(ImpuestosDb db) : ImpuestosService.ImpuestosServiceBase
{
    public override async Task<ObtenerResumenEmpresaResponse> ObtenerResumenEmpresa(ObtenerResumenEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var empresa = PlatformServiceCaller.From(context).TenantId;
        var porEstado = await db.Set<Liquidacion>().AsNoTracking()
            .Where(l => l.TenantId == empresa)
            .GroupBy(l => l.Estado)
            .Select(g => new { Estado = g.Key, Total = g.Count() })
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);
        return new ObtenerResumenEmpresaResponse
        {
            EnProceso = porEstado.Where(e => !CierreLote.EsTerminal(e.Estado)).Sum(e => e.Total),
            Finalizados = porEstado.Where(e => CierreLote.EsTerminal(e.Estado)).Sum(e => e.Total),
        };
    }
}
