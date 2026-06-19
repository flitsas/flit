using Flit.Admin.Domain.Companies.Whitelist;

namespace Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;

/// <summary>
/// Caso de uso de alta masiva en la lista blanca (HU #10191, AC4/AC5).
///
/// Flujo: (1) valida que haya al menos un correo y que todos tengan formato válido —
/// si algún correo es inválido retorna 422 con la lista de inválidos <em>sin tocar
/// BD ni auditoría</em> (AC5, atómico); (2) normaliza y deduplica preservando el
/// orden; (3) delega al repositorio el alta + audit log en una única transacción.
/// Los duplicados ya existentes se omiten sin error (idempotencia, 201 parcial).
/// </summary>
public sealed class AddWhitelistEmailsHandler
{
    private readonly IWhitelistRepository _repository;

    public AddWhitelistEmailsHandler(IWhitelistRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AddWhitelistEmailsResult> HandleAsync(
        AddWhitelistEmailsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emails = command.Request.Emails ?? [];
        var errors = new List<WhitelistValidationError>();

        if (emails.Count == 0)
        {
            errors.Add(new WhitelistValidationError(
                "emails", "Debe proporcionar al menos un correo.", null));
        }

        foreach (var email in emails)
        {
            if (!WhitelistEmailValidator.IsValid(email))
            {
                errors.Add(new WhitelistValidationError(
                    "emails", "Correo con formato inválido.", email));
            }
        }

        // AC5: cualquier correo inválido aborta toda la operación antes de persistir.
        if (errors.Count > 0)
        {
            return AddWhitelistEmailsResult.Invalid(errors);
        }

        // Normaliza (lowercase + trim) y deduplica preservando el orden de entrada.
        var normalized = new List<string>(emails.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var email in emails)
        {
            var canonical = WhitelistEmail.Normalize(email);
            if (seen.Add(canonical))
            {
                normalized.Add(canonical);
            }
        }

        var outcome = await _repository
            .AddEmailsAsync(command.TenantId, normalized, command.AddedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        return AddWhitelistEmailsResult.Success(outcome.Added, outcome.Skipped);
    }
}
