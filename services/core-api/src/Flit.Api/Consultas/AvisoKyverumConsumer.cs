using System.Text;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Api.Consultas;

/// <summary>Datos de <c>consultas.aviso.kyverum_verify</c> (contracts/asyncapi/consultas-events.v1.yaml).</summary>
internal sealed record AvisoKyverumVerify(Guid ValidacionId, string VerificationId, string Producto, Guid AvisoId, string Cuerpo);

/// <summary>
/// HU #13351 (ADR-0065 §6) — el aviso de Kyverum que recibió y verificó Consultas llega a Trámites por el bus y se
/// aplica con la lógica del webhook de siempre (<see cref="KyverumWebhookHandler.HandleVerifiedAsync"/>). Si Kyverum no
/// responde la consulta de respaldo, el evento se reintenta (10 s, 1 min, 10 min) y la reconciliación lo acompaña.
/// </summary>
internal sealed class AvisoKyverumConsumer(KyverumWebhookHandler handler, ILogger<AvisoKyverumConsumer> logger)
    : IEventConsumer<AvisoKyverumVerify>
{
    public const string Cola = "tramites.avisos-kyverum";
    public const string Tipo = "consultas.aviso.kyverum_verify";

    public async Task HandleAsync(EventEnvelope envelope, AvisoKyverumVerify data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (!string.Equals(data.Producto, "tramites", StringComparison.Ordinal))
            return; // la validación la pidió otro servicio

        var (_, error) = await handler.HandleVerifiedAsync(data.ValidacionId, Encoding.UTF8.GetBytes(data.Cuerpo), ct).ConfigureAwait(false);
        switch (error)
        {
            case null:
                return;
            case "reintentar" or "no_verificable":
                throw new InvalidOperationException($"El aviso {data.AvisoId} de la validación {data.ValidacionId} no se pudo aplicar ({error}); se reintenta.");
            default:
                // not_found / cuerpo_invalido: reintentar no lo arregla. Queda en la bitácora de identidad y en consultas.avisos.
                AvisoKyverumLog.Descartado(logger, data.AvisoId, data.ValidacionId, error);
                return;
        }
    }
}

internal static partial class AvisoKyverumLog
{
    [LoggerMessage(EventId = 7411, Level = LogLevel.Warning, Message = "Aviso de Kyverum {AvisoId} de la validación {ValidacionId} descartado: {Motivo}")]
    public static partial void Descartado(ILogger logger, Guid avisoId, Guid validacionId, string motivo);
}
