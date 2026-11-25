using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Guarda en el contexto dado (sin <c>SaveChanges</c>: va en la transacción de quien llama, que debe existir; antes bloquea la
/// fila del aprovisionamiento, <see cref="BloqueoAprovisionamiento"/>) las credenciales que trajo el alta
/// idempotente (M07b, plan §5.8): las cifra con <see cref="AlmacenCredenciales"/> (que conserva el secreto anterior 24 h si cambió)
/// y, con <c>api_key_rotated</c>, deja el historial <c>CLAVE_ROTADA</c> y encola en la outbox del servicio el trabajo
/// <see cref="ConsumidorCredenciales.TipoPropagar"/> para llevar la llave nueva a Diagnóstico (D-ALTA-IDEM). El mensaje solo lleva
/// la empresa: la llave se lee cifrada al atenderlo y nunca viaja por el bus ni queda en la outbox.
/// </summary>
/// <example>
/// <code>
/// var (credencial, secretoNuevo) = await guardado.EscribirAsync(db, empresa, resultado, ct);
/// guardado.TrasEscribir(db, empresa, resultado, fila.Estado, fila.ActualizadoEn);
/// await db.SaveChangesAsync(ct);
/// </code>
/// </example>
public sealed class GuardadoCredencialAlta(AlmacenCredenciales almacen, PlatformMessagingOptions mensajeria, TimeProvider reloj)
{
    public const string ActorSistema = "SISTEMA";

    /// <summary>Escribe la credencial del resultado; devuelve la fila y si el webhook_secret cambió respecto del guardado.</summary>
    public async Task<(CredencialEmpresa Fila, bool SecretoNuevo)> EscribirAsync(ImpuestosDb db, Guid empresa, ResultadoEvaluacion resultado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(resultado);
        var credenciales = resultado.Credenciales ?? throw new ArgumentException("El resultado no trae credenciales.", nameof(resultado));
        // Orden de bloqueos: aprovisionamiento antes que credencial (BloqueoAprovisionamiento). Si quien llama ya la bloqueó, no cambia nada.
        await BloqueoAprovisionamiento.BloquearAsync(db, empresa, ct).ConfigureAwait(false);
        var actual = await almacen.FilaAsync(db, empresa, ct).ConfigureAwait(false);
        var secretoNuevo = actual?.WebhookSecretCifrado is null ? credenciales.WebhookSecret is not null : almacen.CambiaSecreto(actual, empresa, credenciales.WebhookSecret);
        var fila = almacen.Escribir(db, actual, empresa, credenciales, resultado.Origen ?? OrigenCredencial.Alta, null);
        return (fila, secretoNuevo);
    }

    /// <summary>Con la llave rotada: historial <c>CLAVE_ROTADA</c> en <paramref name="estado"/> y el trabajo de propagación.</summary>
    public void TrasEscribir(ImpuestosDb db, Guid empresa, ResultadoEvaluacion resultado, EstadoAprovisionamiento estado, DateTimeOffset en)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(resultado);
        if (!resultado.ClaveRotada)
            return;
        db.Add(new HistorialAprovisionamiento
        {
            TenantId = empresa,
            Desde = estado,
            Hacia = estado,
            MotivoCodigo = MotivosAprovisionamiento.ClaveRotada,
            ActorTipo = ActorSistema,
            En = en,
        });
        Encolar(db, ConsumidorCredenciales.TipoPropagar, empresa);
    }

    /// <summary>
    /// Deja en la outbox de <paramref name="db"/> un trabajo interno de <see cref="ConsumidorCredenciales"/> (exchange
    /// <c>flit.impuestos</c>, productor el del servicio) con solo la empresa. Es lo que haría <c>IPlatformOutbox.Enqueue</c>, pero
    /// sobre este contexto (el de la compañía fijada), para que el trabajo se confirme en el mismo guardado que la credencial.
    /// </summary>
    public void Encolar(ImpuestosDb db, string tipo, Guid empresa)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            tipo,
            1,
            reloj.GetUtcNow(),
            empresa,
            mensajeria.Producer,
            System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            System.Text.Json.JsonSerializer.SerializeToElement(new TrabajoCredencial(empresa), EventEnvelope.JsonOptions));
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = envelope.EventId,
            Exchange = EventEnvelope.ExchangeFor(mensajeria.Producer),
            RoutingKey = tipo,
            Payload = envelope.ToJson(),
            OccurredAt = envelope.OccurredAt,
        });
    }
}

/// <summary>Datos v1 de los trabajos internos de credenciales (<c>impuestos-events.v1.yaml</c> 1.1.0): solo la empresa.</summary>
public sealed record TrabajoCredencial(Guid TenantId);
