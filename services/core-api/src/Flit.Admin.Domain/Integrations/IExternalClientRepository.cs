namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13084 — persistencia de los clientes de integración externos (<c>integrations.external_clients</c>).
/// Entidad de plataforma: sin compañía, sin filtro de tenant.
/// </summary>
public interface IExternalClientRepository
{
    /// <summary>Da de alta un cliente activo. Lanza <see cref="ExternalClientAlreadyExistsException"/> si el identificador ya existe.</summary>
    Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default);

    Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Credenciales para verificar el login; <c>null</c> si no existe o está dado de baja.</summary>
    Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>Clientes no dados de baja, ordenados por identificador.</summary>
    Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #13087 — suma un intento fallido en una sola sentencia (dos intentos simultáneos no se pisan).
    /// Al llegar a <paramref name="maxFailedAttempts"/> bloquea hasta <c>now + lockDuration</c> y vuelve
    /// a poner el contador en cero. Devuelve el bloqueo vigente tras el intento (<c>null</c> si no quedó
    /// bloqueado).
    /// </summary>
    Task<DateTimeOffset?> RegisterFailedAttemptAsync(
        Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>HU #13087 — pase emitido: limpia intentos y bloqueo y sella <c>last_token_at</c>.</summary>
    Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #13088 — aplica los campos no nulos de <paramref name="changes"/> y sella <c>updated_by</c> (la
    /// auditoría guarda la fila completa: así queda quién hizo el cambio). <c>null</c> si no existe.
    /// </summary>
    Task<ExternalClientView?> UpdateAsync(
        Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #13088 — sustituye el secreto por <paramref name="newSecretHash"/>. El hash vigente pasa a ser el
    /// anterior (ventana de gracia) salvo con <paramref name="revokePrevious"/>, que lo anula al instante.
    /// Limpia rotación obligatoria, intentos y bloqueo. <c>null</c> si no existe.
    /// </summary>
    Task<ExternalClientView?> ReplaceSecretAsync(
        Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>HU #13088 — pone a cero los intentos y quita el bloqueo. <c>null</c> si no existe.</summary>
    Task<ExternalClientView?> UnlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default);
}
