namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// Persistencia de <c>platform.tenant_product_credentials</c>. La auditoría legible va a
/// <c>admin.tenant_config_audit_logs</c> sin valor ni huella (ADR-0092).
/// </summary>
public interface ITenantProductCredentialRepository
{
    Task<TenantProductCredential?> GetAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default);

    Task<TenantProductCredentialWrite> SetAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string secretProtected,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14694 (D-CRED-DIAG) — deja en <c>admin.tenant_config_audit_logs</c> que <paramref name="lector"/> leyó la
    /// credencial para copiarla (sin valor ni huella). Se llama también si no hay fila (<paramref name="existe"/> en false).
    /// </summary>
    Task RegistrarLecturaParaCopiaAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string lector,
        bool existe,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14694 (M04b, D-ALTA-IDEM) — REEMPLAZA el valor cifrado de una fila que ya existe; sin fila devuelve false y no crea
    /// nada. Deja en <c>admin.tenant_config_audit_logs</c>, en el mismo guardado, el reemplazo con el cliente de servicio
    /// <paramref name="servicio"/> (sin valor ni huella).
    /// </summary>
    Task<bool> ReplaceExistingFromServiceAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string secretProtected,
        string servicio,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 — guarda en UN solo <c>SaveChanges</c> las credenciales que trajo el alta del proveedor: crea o reemplaza cada
    /// fila de <paramref name="escrituras"/>, deja una auditoría por fila (operación create/update, sin valor ni huella) y una
    /// fila resumen con <paramref name="motivo"/> (<c>CREDENCIAL_OBTENIDA</c> o <c>CLAVE_ROTADA</c>). Lo que
    /// <paramref name="antesDeGuardar"/> encole en el mismo contexto (la outbox) se confirma o se revierte con las filas; recibe el
    /// instante del guardado.
    /// </summary>
    /// <returns>El instante del guardado (<c>updated_at</c> de las filas escritas).</returns>
    Task<DateTimeOffset> GuardarAltaProveedorAsync(
        Guid tenantId,
        string productCode,
        IReadOnlyList<EscrituraCredencialAlta> escrituras,
        string motivo,
        Guid? changedBy,
        Action<DateTimeOffset>? antesDeGuardar,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #14711 (L-3) — la fila resumen del alta del proveedor (<c>obtain_from_provider</c>) cuando no se escribió ninguna
    /// credencial: <paramref name="resultado"/> = <c>SIN_CAMBIOS</c> (<paramref name="exito"/> true) o el código del fallo
    /// (false; también en <c>error_code</c>). En su propio guardado y sin valores sensibles.
    /// </summary>
    Task RegistrarAuditoriaAltaProveedorAsync(
        Guid tenantId,
        string productCode,
        string resultado,
        bool exito,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HU #14711 (M-3) — el guardado del alta chocó con otro guardado de las mismas credenciales (unique o <c>row_version</c>):
/// el botón y la ronda escribieron a la vez. El repositorio ya soltó lo que no se guardó.
/// </summary>
public sealed class TenantProductCredentialConflictException : Exception
{
    public TenantProductCredentialConflictException()
        : base("Otro guardado de las credenciales del alta llegó primero.")
    {
    }

    public TenantProductCredentialConflictException(string message)
        : base(message)
    {
    }

    public TenantProductCredentialConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>HU #14711 — una credencial ya cifrada para <see cref="ITenantProductCredentialRepository.GuardarAltaProveedorAsync"/>.</summary>
public sealed record EscrituraCredencialAlta(string CredentialCode, string SecretProtected)
{
    public override string ToString() => $"EscrituraCredencialAlta {{ CredentialCode = {CredentialCode} }}";
}
