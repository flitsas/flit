namespace Flit.Consultas.Api.Impuesto.Credencial;

/// <summary>Llave fija no secreta para simulación local. No llama a Identidad.</summary>
internal sealed class FuenteCredencialImpuestoSimulada : IFuenteCredencialImpuesto
{
    public const string LlaveSimulada = "simulada-no-secreta";

    public Task<CredencialImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct)
    {
        _ = tenantId;
        _ = forzar;
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new CredencialImpuesto(true, LlaveSimulada, 1, MotivoSinCredencial.Ninguno));
    }
}
