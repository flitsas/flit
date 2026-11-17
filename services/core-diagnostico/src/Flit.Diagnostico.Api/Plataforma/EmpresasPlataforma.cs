using Flit.Identidad.Grpc.V1;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>Una empresa de la plataforma con Diagnóstico encendido, tal como la tiene Identidad. <c>Nit</c> null si no lo informa.</summary>
public sealed record EmpresaPlataforma(Guid Id, string Nombre, string? Nit);

/// <summary>
/// Las empresas con Diagnóstico encendido (HU #13741, escenario 15; patrón de Comparendos): el editor de contrato del
/// backoffice elige de esta lista y no pide el uuid a mano. Devuelve null si no hay con quién hablar (Identidad sin
/// configurar); si Identidad no responde, la llamada lanza (el endpoint responde 503, nunca una lista vacía).
/// </summary>
public interface IEmpresasPlataforma
{
    Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct);
}

/// <summary>
/// Por el gRPC global de empresas de Identidad (<c>flit.identidad.v1.EmpresasService.ListarEmpresasConProducto</c>, scope
/// <c>platform.identidad.empresas</c>, sin <c>x-flit-tenant-id</c>). Descarta los registros cuyo id no es un uuid y repite
/// una empresa una sola vez.
/// </summary>
internal sealed class EmpresasPlataformaIdentidad(EmpresasService.EmpresasServiceClient cliente) : IEmpresasPlataforma
{
    public async Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarEmpresasConProductoAsync(
            new ListarEmpresasConProductoRequest { Producto = ProductosHabilitadosIdentidad.Producto }, cancellationToken: ct).ConfigureAwait(false);
        return [.. r.Empresas
            .Select(e => (Valido: Guid.TryParse(e.Id, out var id) && id != Guid.Empty, Id: id, e))
            .Where(x => x.Valido)
            .DistinctBy(x => x.Id)
            .Select(x => new EmpresaPlataforma(x.Id, x.e.Nombre.Trim(), string.IsNullOrWhiteSpace(x.e.Nit) ? null : x.e.Nit.Trim()))];
    }
}

/// <summary>Sin <c>Diagnostico:Identidad:Address</c> (local sin Identidad): no hay lista (null), no una lista vacía.</summary>
internal sealed class EmpresasPlataformaNoConectada : IEmpresasPlataforma
{
    public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<EmpresaPlataforma>?>(null);
}
