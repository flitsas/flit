using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Infrastructure.OtClientProcedures;

/// <summary>
/// HU #13389 (Épica #13216, D-FB2) — acceso del OT a un trámite de cliente, scope transaccional de la
/// compañía cliente y precedencia de la matriz documental, sin HTTP. Lo consumen el
/// <c>POST consolidado-maestro</c> y la entrega del consolidado de <c>AdminOtEndpoints</c>, y el lote de la
/// bandeja OT, para que los tres apliquen la misma regla.
/// <para>Vive en Infrastructure porque <c>Flit.Tramites.Application</c> y <c>Flit.Admin.Application</c> no
/// se referencian entre sí.</para>
/// </summary>
public interface IOtClientProcedureConsolidadoContext
{
    /// <summary>
    /// Trámite accesible para el tenant OT (o para el organismo <paramref name="transitOfficeIdOverride"/>
    /// que fija un Super Admin), con la misma regla que la bandeja. <c>null</c> = no accesible (otro
    /// organismo, borrador, borrado lógico, sin grant): el endpoint lo traduce a 404 y el lote a
    /// <c>acceso_revocado</c>. Nunca produce un resultado HTTP.
    /// </summary>
    Task<OtClientProcedure?> ResolverAccesoAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta <paramref name="accion"/> dentro del scope RLS del tenant cliente
    /// (<c>ExecuteInClientTenantScopeAsync</c>, con la compensación post-commit de la HU #12797) y le
    /// pasa la precedencia de la matriz resuelta DENTRO de ese scope. Si el resolver de la matriz
    /// falla, la acción recibe una lista vacía (el generador cae al orden por modalidad).
    /// </summary>
    Task<T> EjecutarEnContextoClienteAsync<T>(
        OtClientProcedure access,
        Func<IReadOnlyList<string>, Task<T>> accion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Variante de <see cref="EjecutarEnContextoClienteAsync{T}(OtClientProcedure, Func{IReadOnlyList{string}, Task{T}}, CancellationToken)"/>
    /// que solo resuelve la matriz si <paramref name="resolverPrecedencia"/> es <c>true</c>; si no, la
    /// acción recibe <c>null</c> y el resolver no se llama (la entrega que no va a generar).
    /// </summary>
    Task<T> EjecutarEnContextoClienteAsync<T>(
        OtClientProcedure access,
        bool resolverPrecedencia,
        Func<IReadOnlyList<string>?, Task<T>> accion,
        CancellationToken cancellationToken = default);
}
