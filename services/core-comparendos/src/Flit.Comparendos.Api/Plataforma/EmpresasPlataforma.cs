using Flit.Comparendos.Domain.Identificadores;
using Flit.Identidad.Grpc.V1;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>Una empresa de la plataforma con Comparendos encendido. <c>NitBase</c> va sin dígito de verificación.</summary>
public sealed record EmpresaPlataforma(Guid Id, string Nombre, string? NitBase);

/// <summary>
/// Las empresas con Comparendos encendido, con su nombre y su NIT tal como los tiene la plataforma (hallazgos B2-1 y
/// B2-4): el SuperAdmin elige de esta lista y Comparendos no le pide el id, el nombre ni el NIT a mano. Null si no hay con
/// quién hablar.
/// </summary>
public interface IEmpresasPlataforma
{
    Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct);
}

/// <summary>Por el gRPC global de empresas de Identidad (<c>flit.identidad.v1.EmpresasService</c>).</summary>
internal sealed class EmpresasPlataformaIdentidad(EmpresasService.EmpresasServiceClient cliente) : IEmpresasPlataforma
{
    public async Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarEmpresasConProductoAsync(new ListarEmpresasConProductoRequest { Producto = ProductosHabilitadosIdentidad.Producto },
            cancellationToken: ct).ConfigureAwait(false);
        return [.. r.Empresas.Where(e => Guid.TryParse(e.Id, out _))
            .Select(e => new EmpresaPlataforma(Guid.Parse(e.Id), e.Nombre.Trim(), NitBase(e.Nit)))];
    }

    /// <summary>
    /// La plataforma guarda solo el número; si por error trae el dígito con guion («890903938-8»), se quita. Null si no
    /// queda un NIT válido.
    /// </summary>
    internal static string? NitBase(string? nit) =>
        Nit.Separar(nit)?.Base;
}

/// <summary>Sin <c>Comparendos:Identidad:Address</c> (local sin Identidad): no hay lista.</summary>
internal sealed class EmpresasPlataformaNoConectada : IEmpresasPlataforma
{
    public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<EmpresaPlataforma>?>(null);
}
