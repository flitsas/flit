using Flit.Identidad.Grpc.V1;
using Grpc.Core;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Secreto del webhook de la empresa (HU #14711): el actual y, mientras siga vigente, el anterior. <see cref="ToString"/> nunca
/// incluye los valores.
/// </summary>
internal sealed record SecretoWebhookImpuesto(bool Configurado, string? Actual, string? Anterior, DateTimeOffset? AnteriorVenceEn, long Version)
{
    public static SecretoWebhookImpuesto SinConfigurar { get; } = new(false, null, null, null, 0);

    /// <summary>Los secretos con los que se verifica una firma en <paramref name="ahora"/>: el actual y el anterior si no venció.</summary>
    public IReadOnlyList<string> Vigentes(DateTimeOffset ahora)
    {
        if (!Configurado || string.IsNullOrEmpty(Actual))
            return [];
        return !string.IsNullOrEmpty(Anterior) && AnteriorVenceEn is { } vence && vence > ahora ? [Actual, Anterior] : [Actual];
    }

    public override string ToString() =>
        $"SecretoWebhookImpuesto {{ Configurado = {Configurado}, ConAnterior = {Anterior is not null}, Version = {Version} }}";
}

internal interface IFuenteSecretoWebhookImpuesto
{
    /// <summary>Con <paramref name="forzar"/> relee de Identidad aunque haya caché (firma que no coincide con la versión cacheada).</summary>
    Task<SecretoWebhookImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct);
}

/// <summary>Identidad no respondió al pedir el secreto: el receptor responde 503 y el envío sigue sin <c>callbackUrl</c>.</summary>
internal sealed class FalloSecretoWebhookImpuestoException(string motivo) : Exception(motivo);

/// <summary>
/// Cliente tipado de <c>CredencialesProductoService</c> con el scope propio del secreto del webhook
/// (<see cref="FuenteSecretoWebhookImpuesto.Scope"/>): el SDK registra un token de servicio por tipo de cliente, así que el de la llave
/// (<see cref="Credencial.FuenteCredencialImpuestoPlataforma.Scope"/>) no se mezcla con este.
/// </summary>
internal class ClienteSecretoWebhookImpuesto : CredencialesProductoService.CredencialesProductoServiceClient
{
    public ClienteSecretoWebhookImpuesto(CallInvoker callInvoker)
        : base(callInvoker)
    {
    }

    /// <summary>Solo pruebas (dobles).</summary>
    protected ClienteSecretoWebhookImpuesto()
    {
    }
}

/// <summary>Sin Identidad (simulación o sin dirección): ninguna empresa tiene secreto, no se pide aviso.</summary>
internal sealed class FuenteSecretoWebhookImpuestoSimulada : IFuenteSecretoWebhookImpuesto
{
    public Task<SecretoWebhookImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct) =>
        Task.FromResult(SecretoWebhookImpuesto.SinConfigurar);
}
