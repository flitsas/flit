namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

public sealed class ValidateImprintSignatureCommand
{
    public required Guid TenantId { get; init; }

    public required Guid VehicleSignatureImprintId { get; init; }

    public required Guid ValidatedBy { get; init; }

    /// <summary>
    /// Firma digital pegada desde el PDF (Base64). Se normaliza quitando espacios/saltos
    /// y se verifica con <c>public_key</c> + <c>document_hash</c> de la impronta.
    /// </summary>
    public required string ProvidedSignature { get; init; }
}

public sealed class ValidateImprintSignatureResult
{
    public required Guid ValidationId { get; init; }

    public required Guid VehicleSignatureImprintId { get; init; }

    /// <summary><c>valid</c>, <c>invalid</c> o <c>not_found</c>.</summary>
    public required string Result { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset ValidatedAt { get; init; }

    public bool Found => Result != Domain.ImprintSignatures.ImprintSignatureValidationResults.NotFound;
}
