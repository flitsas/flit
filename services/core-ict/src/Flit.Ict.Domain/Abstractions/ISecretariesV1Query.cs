namespace Flit.Ict.Domain.Abstractions;

/// <summary>Una secretaría (organismo) de tránsito activa (forma v1: Code + Name).</summary>
public sealed record SecretaryV1(string Code, string Name);

/// <summary>
/// Listado de secretarías de tránsito activas (v1 <c>GET /api/v1/secretaries</c>). Catálogo global
/// (<c>catalogs.transit_offices</c>): los clientes lo usan para conocer los <c>traffic_secretary_code</c>
/// válidos al registrar matrículas.
/// </summary>
public interface ISecretariesV1Query
{
    Task<IReadOnlyList<SecretaryV1>> GetActiveAsync(CancellationToken ct = default);
}
