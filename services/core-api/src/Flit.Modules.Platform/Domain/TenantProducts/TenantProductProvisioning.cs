namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2, decisión R1) — último estado de aprovisionamiento de un producto para una empresa, tal como lo
/// reportó el servicio del producto (<c>platform.tenant_product_provisioning</c>, DDL 205). Sin datos personales ni secretos.
/// </summary>
/// <param name="TenantId">Empresa.</param>
/// <param name="ProductCode">Producto con aprovisionamiento.</param>
/// <param name="Estado">Estado del plan §3.1 (p. ej. <c>LISTO</c>).</param>
/// <param name="MotivoCodigo">Código estable del motivo, o <c>null</c>.</param>
/// <param name="Mensaje">Motivo legible (hasta 300), o <c>null</c>.</param>
/// <param name="Accion">Acción del SuperAdmin, o <c>null</c>.</param>
/// <param name="FirmaConfirmada">Solo en <c>LISTO</c>; <c>null</c> en los demás estados.</param>
/// <param name="Version">Versión monotónica del servicio del producto.</param>
/// <param name="ActualizadoEn">Cuándo lo guardó la plataforma.</param>
public sealed record TenantProductProvisioning(
    Guid TenantId,
    string ProductCode,
    string Estado,
    string? MotivoCodigo,
    string? Mensaje,
    string? Accion,
    bool? FirmaConfirmada,
    long Version,
    DateTimeOffset ActualizadoEn);

/// <summary>Resultado de <see cref="ITenantProductProvisioningStore.GuardarSiEsMasNuevoAsync"/>.</summary>
/// <param name="EmpresaExiste"><c>false</c> si la empresa no existe (no se guarda nada).</param>
/// <param name="Aplicado"><c>true</c> si se guardó el reporte.</param>
/// <param name="VersionGuardada">Versión que queda guardada; 0 sin fila.</param>
public sealed record ProvisioningGuardado(bool EmpresaExiste, bool Aplicado, long VersionGuardada);

/// <summary>Persistencia de <c>platform.tenant_product_provisioning</c>.</summary>
public interface ITenantProductProvisioningStore
{
    /// <summary>Guarda el reporte solo si su versión es mayor que la guardada (atómico en la base).</summary>
    Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 — prepara (sin guardar) el paso a <c>PENDIENTE</c> del alta automática de la credencial del impuesto de
    /// <c>diagnostico</c>: lee la fila con seguimiento y devuelve si existe y la acción que la crea o la reinicia (intentos en
    /// 0) en el contexto compartido. Una reserva de una ronda en curso se conserva (otra réplica no toma la fila a la vez). La
    /// acción se invoca justo antes del guardado del cambio del producto.
    /// </summary>
    Task<PreparacionPendienteAltaDiagnostico> PrepararPendienteAltaDiagnosticoAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 — reserva hasta <paramref name="lote"/> filas de <c>diagnostico</c> en <c>PENDIENTE</c> cuyo próximo intento
    /// venció (<c>FOR UPDATE SKIP LOCKED</c>, varias réplicas) y les corre <c>next_attempt_at</c> a <paramref name="ahora"/> +
    /// <paramref name="reserva"/> para que otra ronda no las tome mientras se atienden.
    /// </summary>
    Task<IReadOnlyList<PendienteAltaDiagnostico>> ReservarPendientesAltaDiagnosticoAsync(
        DateTimeOffset ahora, TimeSpan reserva, int lote, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 (M-1) — guarda el estado que dejó una ronda solo si la fila sigue en la versión que la ronda reservó
    /// (<paramref name="versionReservada"/>) y la sube en 1. <c>false</c> = la fila cambió durante la ronda (se reinició o el
    /// botón guardó otro estado): el resultado es obsoleto y no se escribe.
    /// </summary>
    Task<bool> RegistrarEstadoAltaDiagnosticoAsync(EstadoAltaDiagnostico estado, long versionReservada, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 (N-3) — estado que deja el botón «Obtener del proveedor»: crea la fila o la reemplaza subiendo
    /// <c>reported_version</c> en 1, pero solo si el estado cambia (estado, motivo, mensaje, acción, intentos o próximo intento).
    /// </summary>
    Task RegistrarEstadoAltaDiagnosticoManualAsync(EstadoAltaDiagnostico estado, CancellationToken cancellationToken = default);

    /// <summary>Los estados guardados de la empresa.</summary>
    Task<IReadOnlyList<TenantProductProvisioning>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>HU #14711 — una empresa con el alta automática de <c>diagnostico</c> por atender.</summary>
/// <param name="TenantId">Empresa.</param>
/// <param name="Intentos">Intentos automáticos fallidos hasta ahora.</param>
/// <param name="Version"><c>reported_version</c> de la fila al reservarla; condiciona el guardado del resultado (M-1).</param>
public sealed record PendienteAltaDiagnostico(Guid TenantId, int Intentos, long Version = 0);

/// <summary>HU #14711 — resultado de <see cref="ITenantProductProvisioningStore.PrepararPendienteAltaDiagnosticoAsync"/>.</summary>
/// <param name="FilaExiste">Ya hay fila del alta de <c>diagnostico</c> para la empresa.</param>
/// <param name="Marcar">Crea o reinicia la fila en <c>PENDIENTE</c> en el contexto compartido (sin guardar).</param>
public sealed record PreparacionPendienteAltaDiagnostico(bool FilaExiste, Action<DateTimeOffset> Marcar);

/// <summary>HU #14711 — estado del alta de la credencial de <c>diagnostico</c> que escribe la plataforma (no un servicio).</summary>
/// <param name="TenantId">Empresa.</param>
/// <param name="Estado"><c>PENDIENTE</c>, <c>LISTO</c>, <c>LISTO_SIN_WEBHOOK</c>, <c>ERROR</c>, <c>REQUIERE_REVISION_NIT</c> o <c>SUSPENDIDO</c>.</param>
/// <param name="MotivoCodigo">Código del motivo (NIT_*, PROVEEDOR_*, …) o <c>null</c>.</param>
/// <param name="Mensaje">Literal del motivo o <c>null</c>.</param>
/// <param name="Accion">Acción del SuperAdmin (<c>CORREGIR_NIT</c>) o <c>null</c>.</param>
/// <param name="Intentos">Intentos automáticos fallidos.</param>
/// <param name="ProximoIntentoEn">Cuándo se reintenta; <c>null</c> fuera de <c>PENDIENTE</c>.</param>
/// <param name="En">Momento del estado.</param>
public sealed record EstadoAltaDiagnostico(
    Guid TenantId,
    string Estado,
    string? MotivoCodigo,
    string? Mensaje,
    string? Accion,
    int Intentos,
    DateTimeOffset? ProximoIntentoEn,
    DateTimeOffset En);
