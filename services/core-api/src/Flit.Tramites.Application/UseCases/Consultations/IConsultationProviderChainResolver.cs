namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Resuelve y ejecuta la cadena de proveedores de consulta para un <see cref="ConsultationKind"/>
/// (HU #10478): intenta el primario (Kyverum RUNT) y cae al siguiente (Verifik) cuando el resultado
/// NO se pudo verificar (algún check con status <c>error</c>) o el primario excede el presupuesto de
/// failover. Un resultado definitivo (incluido "no encontrado" = <c>fail</c>) NO dispara fallback.
/// Nunca lanza excepciones de transporte: los providers ya mapean sus fallos a checks.
/// </summary>
public interface IConsultationProviderChainResolver
{
    /// <summary>Orden de provider keys configurado para el kind (defaults globales; override por tenant en Fase 4).</summary>
    IReadOnlyList<string> ResolveChain(ConsultationKind kind);

    /// <summary>
    /// Ejecuta la cadena con fallback y devuelve el primer resultado verificable. Si todos fallan,
    /// devuelve el último (con su check <c>error</c>). <paramref name="failoverTimeoutMs"/> acota la
    /// espera del primario antes de caer al siguiente; si es null se usa el default de
    /// <see cref="ConsultationChainOptions.FailoverTimeoutMs"/> (en Fase 5 el handler pasará el valor
    /// del tenant <c>runt_failover_timeout_ms</c>).
    /// </summary>
    Task<ConsultationResult> ConsultAsync(
        ConsultationKind kind, ConsultationContext ctx, int? failoverTimeoutMs, CancellationToken ct);
}
