namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13084 — datos para dar de alta un cliente de integración. El secreto llega ya como hash
/// Argon2id: el repositorio nunca ve el secreto en claro.
/// </summary>
public sealed record NewExternalClient(
    string ClientId,
    string DisplayName,
    string Purpose,
    string SecretHash,
    IReadOnlyList<string> Scopes,
    Guid? CreatedBy);

/// <summary>
/// Vista de un cliente para administración y listados. No incluye ningún hash.
/// </summary>
public sealed record ExternalClientView(
    Guid Id,
    string ClientId,
    string DisplayName,
    string Purpose,
    IReadOnlyList<string> Scopes,
    bool IsActive,
    bool MustRotate,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastTokenAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Lo que necesita la obtención del pase (HU #13087) para verificar un cliente: incluye los hashes.
/// Solo lo devuelve la búsqueda por <c>client_id</c>; nunca se serializa hacia fuera.
/// </summary>
public sealed record ExternalClientCredentials(
    Guid Id,
    string ClientId,
    string SecretHash,
    string? PreviousSecretHash,
    DateTimeOffset? SecretRotatedAt,
    bool MustRotate,
    bool IsActive,
    IReadOnlyList<string> Scopes,
    int FailedAttempts,
    DateTimeOffset? LockedUntil);

/// <summary>HU #13088 — cambios de administración; <c>null</c> = sin cambio.</summary>
public sealed record ExternalClientChanges(
    string? DisplayName = null,
    string? Purpose = null,
    IReadOnlyList<string>? Scopes = null,
    bool? IsActive = null,
    bool? MustRotate = null);

/// <summary>
/// El <c>client_id</c> ya existe (uq_external_clients_client_id). Los identificadores no se reutilizan,
/// tampoco los de clientes dados de baja.
/// </summary>
public sealed class ExternalClientAlreadyExistsException(string clientId)
    : Exception($"El cliente de integración '{clientId}' ya existe.")
{
    public string ClientId { get; } = clientId;
}
