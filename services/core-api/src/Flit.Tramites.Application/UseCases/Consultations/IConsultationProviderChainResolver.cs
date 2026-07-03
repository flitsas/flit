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
    /// <summary>
    /// Orden de provider keys para el kind. Si <paramref name="tenantOverride"/> trae una cadena para
    /// ese tipo, tiene prioridad; si no, se usan los defaults globales.
    /// </summary>
    IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null);

    /// <summary>
    /// Ejecuta la cadena con fallback y devuelve el primer resultado verificable. Si todos fallan,
    /// devuelve el último (con su check <c>error</c>). <paramref name="tenantOverride"/> puede sustituir
    /// la cadena por tipo y el presupuesto de failover; sus campos null caen a los defaults globales.
    /// </summary>
    Task<ConsultationResult> ConsultAsync(
        ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct);
}
