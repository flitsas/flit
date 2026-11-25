using System.Text.RegularExpressions;
using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Guarda o reemplaza la API key del impuesto de una empresa (ADR-0092). Cifra antes de persistir;
/// el valor nunca entra a errores ni a <see cref="SetTenantProductCredentialCommand.ToString"/>.
/// </summary>
public sealed partial class SetTenantProductCredentialHandler
{
    public const string InvalidValueMessage = "La credencial no cumple el formato permitido.";

    private const int ProtectedMinLength = 16;
    private const int ProtectedMaxLength = 4000;

    private readonly ITenantProductCredentialRepository _repository;
    private readonly ITenantProductSecretProtector _protector;

    public SetTenantProductCredentialHandler(ITenantProductCredentialRepository repository, ITenantProductSecretProtector protector)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<TenantProductCredentialWrite> HandleAsync(
        SetTenantProductCredentialCommand command,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (product, credential) = TenantProductCredentialCodes.Normalize(command.ProductCode, command.CredentialCode);
        if (!TenantProductCredentialCodes.IsSupported(product, credential))
            throw new TenantProductException(TenantProductException.CredentialNotSupported, "Credencial no soportada para este producto.");

        if (command.Value is null || !ValorValido().IsMatch(command.Value))
            throw new TenantProductException(TenantProductException.CredentialInvalid, InvalidValueMessage);

        var protectedValue = _protector.Protect(command.TenantId, credential, command.Value);
        if (protectedValue.Length is < ProtectedMinLength or > ProtectedMaxLength)
            throw new InvalidOperationException("El cifrado de la credencial quedó fuera del rango permitido.");

        return await _repository.SetAsync(command.TenantId, product, credential, protectedValue, changedBy, cancellationToken)
            .ConfigureAwait(false);
    }

    // \z (no $): en .NET `$` también hace match antes de un \n final y aceptaría llaves con salto.
    [GeneratedRegex(@"^[\x21-\x7E]{16,256}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValorValido();
}

/// <summary>Comando de escritura. <see cref="ToString"/> no incluye el valor.</summary>
public sealed record SetTenantProductCredentialCommand(
    Guid TenantId,
    string ProductCode,
    string CredentialCode,
    string? Value)
{
    public override string ToString() =>
        $"SetTenantProductCredentialCommand {{ TenantId = {TenantId}, ProductCode = {ProductCode}, CredentialCode = {CredentialCode} }}";
}
