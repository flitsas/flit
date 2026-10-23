using System.Security.Cryptography;
using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.RegisterDomain;

/// <summary>
/// Registra o cambia el dominio de una red (HU #12416 AC1, AC2, AC3, AC5). Orden de validación:
/// formato (AC1) → reservado (AC3) → concurrencia optimista por <c>rowVersion</c> (patrón
/// <c>UpsertBrandingDraftHandler</c>) → escritura (el disparador de BD decide AC2 fail-closed). Un
/// cambio de host retira el dominio anterior y crea uno nuevo en <c>pending</c> con token propio
/// (AC5) — responsabilidad del repositorio, en la misma transacción.
/// </summary>
public sealed class RegisterDomainHandler(ITenantDomainRepository repository, DomainOptions options)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly DomainOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<RegisterDomainResult> HandleAsync(RegisterDomainCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!HostNormalizer.TryNormalize(command.Host, out var normalizedHost, out var formatErrorCode))
        {
            return RegisterDomainResult.Invalid(formatErrorCode!);
        }

        if (ReservedHosts.IsReserved(normalizedHost, _options.Reserved, _options.Allowed))
        {
            return RegisterDomainResult.Invalid(DomainErrors.HostReserved);
        }

        var current = await _repository.GetByTenantIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (current is not null && command.RowVersion is { } expected && expected != current.RowVersion)
        {
            return RegisterDomainResult.Conflict();
        }

        TenantDomain updated;
        try
        {
            updated = await _repository
                .RegisterOrReplaceAsync(command.TenantId, normalizedHost, GenerateVerificationToken(), command.ChangedBy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DomainTenantNotMarcaBlancaException)
        {
            return RegisterDomainResult.TenantNotMarcaBlanca();
        }
        catch (DomainHostAlreadyRegisteredException)
        {
            return RegisterDomainResult.HostAlreadyRegistered();
        }
        catch (DomainAlreadyRegisteredForTenantException)
        {
            return RegisterDomainResult.AlreadyRegisteredForTenant();
        }
        catch (DomainHostInvalidException)
        {
            return RegisterDomainResult.Invalid(DomainErrors.HostInvalid);
        }

        return RegisterDomainResult.Success(updated);
    }

    /// <summary>
    /// Token aleatorio (256 bits, base64url sin relleno: 43 caracteres) — cumple
    /// <c>ck_tenant_domains_verification_token</c> (16-64, <c>[A-Za-z0-9_-]</c>) y es único en la
    /// práctica (colisión despreciable); <c>uq_tenant_domains_verification_token</c> lo garantiza en
    /// BD. Nunca se reutiliza: cada alta o cambio de host genera uno nuevo (AC1, AC5).
    /// </summary>
    private static string GenerateVerificationToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
