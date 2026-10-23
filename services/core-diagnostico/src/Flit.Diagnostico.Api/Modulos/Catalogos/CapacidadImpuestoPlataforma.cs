using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Los departamentos (códigos DIVIPOLA, sin normalizar) donde core-consultas sabe consultar el impuesto vehicular (HU #13861,
/// F09-C2), o null si no hay con quién hablar o la llamada falló. Una lista vacía es una respuesta válida: ningún departamento
/// soportado (Q2=a). Capacidad global, igual para todas las empresas (Q4=a).
/// </summary>
public interface ICapacidadImpuestoPlataforma
{
    Task<IReadOnlyList<string>?> ListarAsync(CancellationToken ct);
}

/// <summary>
/// Capacidad por el rpc <c>ConsultasService.ListarDepartamentosImpuesto</c> de core-consultas (HU #13860), sin
/// <c>x-flit-tenant-id</c> (<see cref="ClienteConsultas.LlamarGlobalAsync"/>). Cualquier <see cref="RpcException"/> (también
/// UNIMPLEMENTED, que es lo que responde hasta C3) devuelve null y se registra con su código: la réplica no cambia.
/// </summary>
/// <example>
/// <code>
/// var departamentos = await new CapacidadImpuestoGrpc(cliente, log).ListarAsync(ct); // null si core-consultas no respondió OK
/// </code>
/// </example>
public sealed partial class CapacidadImpuestoGrpc(ClienteConsultas cliente, ILogger<CapacidadImpuestoGrpc> log) : ICapacidadImpuestoPlataforma
{
    public async Task<IReadOnlyList<string>?> ListarAsync(CancellationToken ct)
    {
        var respuesta = await cliente.LlamarGlobalAsync(
            (c, opciones) => c.ListarDepartamentosImpuestoAsync(new ListarDepartamentosImpuestoRequest(), opciones), ct).ConfigureAwait(false);
        if (respuesta.Respuesta is { } r)
            return [.. r.DepartamentoCodigos];

        CapacidadNoDisponible(log, respuesta.Codigo ?? StatusCode.Unknown, respuesta.Error?.TipoError ?? string.Empty);
        return null;
    }

    [LoggerMessage(EventId = 138612, Level = LogLevel.Warning, Message = "ListarDepartamentosImpuesto respondió {Codigo} ({TipoError}): la réplica de departamentos con impuesto no cambia")]
    private static partial void CapacidadNoDisponible(ILogger logger, StatusCode codigo, string tipoError);
}

/// <summary>
/// Capacidad simulada para LOCAL o DEV (con <c>Diagnostico:Simulacion:Habilitada=true</c>): dos departamentos con códigos
/// DIVIPOLA públicos, <c>05</c> (Antioquia) y <c>25</c> (Cundinamarca). No sale ninguna llamada.
/// </summary>
public sealed class CapacidadImpuestoSimulada : ICapacidadImpuestoPlataforma
{
    public static IReadOnlyList<string> Departamentos { get; } = ["05", "25"];

    public Task<IReadOnlyList<string>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>?>(Departamentos);
}

/// <summary>Sin dirección de core-consultas ni simulación: no hay con quién hablar (null) y la réplica queda como está.</summary>
public sealed class CapacidadImpuestoNoConectada : ICapacidadImpuestoPlataforma
{
    public Task<IReadOnlyList<string>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>?>(null);
}
