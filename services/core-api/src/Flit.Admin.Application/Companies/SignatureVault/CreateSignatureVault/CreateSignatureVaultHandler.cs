using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;

/// <summary>
/// Alta de una firma del baúl (ADR-0025 §5): valida los datos y el artefacto, sube el PNG a storage
/// vía <see cref="ISignatureVaultArtifactStorage"/> y persiste la fila (solo path + hash, nunca el
/// material — ADR-0025 §3). La exclusividad "una 'activa' por (tenant, NIT, documento)" la garantiza
/// el índice único parcial de BD; el <c>23505</c> se traduce a
/// <see cref="SignatureVaultActiveConflictException"/> en el repositorio y aquí a un 422 legible
/// (<c>firma_activa_existente</c>). <c>DocumentNumber</c> es PII (Ley 1581): no se loguea.
/// </summary>
public sealed class CreateSignatureVaultHandler
{
    private readonly ISignatureVaultArtifactStorage _artifactStorage;
    private readonly ISignatureVaultRepository _repository;

    public CreateSignatureVaultHandler(
        ISignatureVaultArtifactStorage artifactStorage,
        ISignatureVaultRepository repository)
    {
        _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateSignatureVaultResult> HandleAsync(
        CreateSignatureVaultCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (errors, artifact) = Validate(command);
        if (errors.Count > 0)
        {
            return CreateSignatureVaultResult.Invalid(errors);
        }

        // Custodia del artefacto en S3 ANTES del insert: se necesita el path + hash para la fila.
        // signature_hash = hash del artefacto (se reutiliza el SHA-256 del storage, ADR-0025 §2/§3).
        var stored = await _artifactStorage
            .SaveAsync(command.TenantId, artifact!, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var id = await _repository.CreateAsync(
                new CreateSignatureVaultData(
                    command.TenantId,
                    command.DocumentType!.Trim(),
                    command.DocumentNumber!.Trim(),
                    command.NitEmpresa!.Trim(),
                    command.FullName!.Trim(),
                    SignatureHash: stored.Sha256,
                    StoragePath: stored.StoragePath,
                    StorageSha256: stored.Sha256,
                    command.VigenciaDesde,
                    command.VigenciaHasta,
                    command.MandateSignerId,
                    command.CreatedBy,
                    command.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            return CreateSignatureVaultResult.Success(id);
        }
        catch (SignatureVaultActiveConflictException)
        {
            // El artefacto ya subido queda huérfano en S3 (lo recupera el job de limpieza del
            // file-manager, igual que otros adjuntos): no se persiste ninguna referencia en BD.
            return CreateSignatureVaultResult.Invalid(
            [
                new SignatureVaultValidationError(
                    "documentNumber",
                    "firma_activa_existente",
                    "Ya existe una firma activa para esta compañía y documento."),
            ]);
        }
    }

    private static (List<SignatureVaultValidationError> Errors, byte[]? Artifact) Validate(
        CreateSignatureVaultCommand command)
    {
        var errors = new List<SignatureVaultValidationError>();

        Require(errors, "documentType", command.DocumentType);
        Require(errors, "documentNumber", command.DocumentNumber);
        Require(errors, "nitEmpresa", command.NitEmpresa);
        Require(errors, "fullName", command.FullName);

        if (command.VigenciaHasta < command.VigenciaDesde)
        {
            errors.Add(new SignatureVaultValidationError(
                "vigenciaHasta", "vigencia_invalida",
                "La vigencia hasta no puede ser anterior a la vigencia desde."));
        }

        var artifact = TryDecodeArtifact(command.ArtefactoFirmaBase64);
        if (artifact is null || artifact.Length == 0)
        {
            errors.Add(new SignatureVaultValidationError(
                "artefactoFirmaBase64", "artefacto_invalido",
                "El artefacto de firma es obligatorio y debe ser un PNG en base64 válido."));
        }

        return (errors, artifact);
    }

    private static void Require(List<SignatureVaultValidationError> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new SignatureVaultValidationError(field, "requerido", $"El campo {field} es obligatorio."));
        }
    }

    /// <summary>Decodifica el base64 (tolerante al prefijo <c>data:...;base64,</c>); <c>null</c> si es inválido.</summary>
    private static byte[]? TryDecodeArtifact(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        var payload = base64.Trim();
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            payload = payload[(comma + 1)..];
        }

        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
