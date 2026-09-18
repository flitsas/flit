namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Resolutor del tema de correo aplicable a un envío (HU #12428 AC1/AC4). Implementación EF Core en
/// <c>Flit.Infrastructure.Notifications.Theme.DbEmailThemeResolver</c>: cabeza MARCA_BLANCA activa con
/// marca publicada ⇒ <see cref="EmailTheme.Kind"/> <c>Brand</c> con su propia marca; hija de una
/// cabeza así ⇒ <c>Brand</c> con la marca de la cabeza (<c>parent_tenant_id</c>, punto único #12320);
/// cualquier otro caso (Concesión, sin red, cabeza sin publicar, <paramref name="tenantId"/>
/// <c>null</c>) ⇒ <see cref="EmailTheme.Flit"/>.
/// </summary>
/// <remarks>
/// AC4 — CUALQUIER excepción de la implementación real se atrapa dentro de ella misma y resuelve
/// <see cref="EmailTheme.Flit"/> con log estructurado: el envío del correo NUNCA falla por causa del
/// tema. Este contrato no declara excepciones checked porque, en la práctica, la única
/// implementación de producción no las propaga.
/// </remarks>
public interface IEmailThemeResolver
{
    Task<EmailTheme> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Objeto nulo (mismo patrón que <c>NullBrandingCacheInvalidator</c>) — sitios de llamada que no
/// reciben el resolutor por DI (tests unitarios de handlers que no ejercitan HU #12428, o
/// compilación antes de que Infrastructure registre el real) resuelven siempre
/// <see cref="EmailTheme.Flit"/>, preservando el comportamiento previo a esta historia.
/// </summary>
public sealed class NullEmailThemeResolver : IEmailThemeResolver
{
    public static readonly NullEmailThemeResolver Instance = new();

    private NullEmailThemeResolver()
    {
    }

    public Task<EmailTheme> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(EmailTheme.Flit);
}
