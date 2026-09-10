using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;

/// <summary>
/// Alta de una firma del baúl (ADR-0025 §5): valida los datos y el artefacto, sube el PNG a storage
/// vía <see cref="ISignatureVaultArtifactStorage"/> y persiste la fila (solo path + hash, nunca el
/// material — ADR-0025 §3). La exclusividad "una 'activa' por (tenant, NIT, documento)" la garantiza
/// el índice único parcial de BD; el <c>23505</c> se traduce a
/// <see cref="SignatureVaultActiveConflictException"/> en el repositorio.
/// <para>
/// HU #11193 (D7) — ese conflicto ya no es un 422: <b>la última firma capturada sustituye a la
/// anterior</b>, esté vencida o vigente. Se revoca la activa y se reintenta el alta una sola vez. La
/// sustituida queda <c>revocada</c> (no se borra), así que el historial y la trazabilidad de lo ya
/// firmado con ella se conservan. Solo se responde <c>firma_activa_existente</c> si no hay lector
/// para resolverla o si la revocación falla.
/// </para>
/// <c>DocumentNumber</c> es PII (Ley 1581): no se loguea.
/// </summary>
public sealed class CreateSignatureVaultHandler
{
    /// <summary>Longitud de <c>admin.signature_vault.codigo_hash</c> (varchar(100)).</summary>
    private const int CodigoHashMaxLength = 100;

    private readonly ISignatureVaultArtifactStorage _artifactStorage;
    private readonly ISignatureVaultRepository _repository;
    private readonly ISignatureVaultReader? _reader;

    /// <param name="reader">
    /// HU #11193 — necesario para resolver cuál es la firma activa que provoca el conflicto y poder
    /// revocarla. Sin él, el comportamiento es el anterior: cualquier conflicto se responde 422.
    /// </param>
    public CreateSignatureVaultHandler(
        ISignatureVaultArtifactStorage artifactStorage,
        ISignatureVaultRepository repository,
        ISignatureVaultReader? reader = null)
    {
        _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reader = reader;
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

        var data = new CreateSignatureVaultData(
            command.TenantId,
            command.DocumentType!.Trim(),
            command.DocumentNumber!.Trim(),
            command.NitEmpresa?.Trim(),
            command.FullName!.Trim(),
            SignatureHash: stored.Sha256,
            StoragePath: stored.StoragePath,
            StorageSha256: stored.Sha256,
            command.VigenciaDesde,
            command.VigenciaHasta,
            command.MandateSignerId,
            command.CreatedBy,
            command.CorrelationId,
            // Normalizado como el resto de campos: era el único que se persistía en crudo, así que un
            // código con espacios de sobra los arrastraba hasta la línea "Hash:" del documento. Vacío se
            // guarda como null para que el sello omita la línea en vez de imprimir "Hash:" sin valor.
            CodigoHash: NormalizeCodigoHash(command.CodigoHash));

        try
        {
            return CreateSignatureVaultResult.Success(
                await _repository.CreateAsync(data, cancellationToken).ConfigureAwait(false));
        }
        catch (SignatureVaultActiveConflictException)
        {
            // HU #11193 (D7) — la firma activa que ocupa el sitio se revoca y se reintenta una vez,
            // esté vencida o vigente: la última firma capturada de una persona es la que manda. El
            // índice único parcial solo admite una 'activa' por persona, así que sin revocar la
            // anterior no hay forma de registrar la nueva.
            // La revocación NO borra: la firma anterior queda en el baúl como 'revocada', así que el
            // historial y la trazabilidad de lo ya firmado con ella se conservan.
            var anterior = await ResolverActivaAsync(command, cancellationToken).ConfigureAwait(false);
            if (anterior is null)
            {
                // El artefacto ya subido queda huérfano en S3 (lo recupera el job de limpieza del
                // file-manager, igual que otros adjuntos): no se persiste ninguna referencia en BD.
                return CreateSignatureVaultResult.Invalid(
                [
                    new SignatureVaultValidationError(
                        "documentNumber",
                        "firma_activa_existente",
                        "Ya existe una firma activa para esta persona."),
                ]);
            }

            var revocada = await _repository.RevokeAsync(
                new RevokeSignatureVaultData(anterior.Value, command.TenantId, command.CreatedBy, command.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            if (!revocada)
            {
                return CreateSignatureVaultResult.Invalid(
                [
                    new SignatureVaultValidationError(
                        "documentNumber",
                        "firma_activa_existente",
                        "Ya existe una firma activa para esta persona y no se pudo revocar la anterior."),
                ]);
            }

            return CreateSignatureVaultResult.Success(
                await _repository.CreateAsync(data, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Id de la firma activa de la persona que bloquea el alta (HU #11193, D7); <c>null</c> si no se
    /// puede resolver o si no hay lector inyectado, en cuyo caso se conserva el 422 de siempre.
    /// <para>
    /// No distingue vencida de vigente a propósito: la decisión de negocio es que la última firma
    /// capturada sustituye a la anterior. La sustituida queda <c>revocada</c>, no borrada.
    /// </para>
    /// </summary>
    private async Task<Guid?> ResolverActivaAsync(
        CreateSignatureVaultCommand command,
        CancellationToken cancellationToken)
    {
        if (_reader is null)
        {
            return null;
        }

        // Bug #11659 — la fila que hay que revocar es la que BLOQUEA el índice, y ese índice es
        // (tenant, document_number): mira el número, no el tipo. Con la lectura de acreditación
        // —que desde el Bug #11659 exige tipo Y número— una firma histórica registrada con otro
        // tipo no se resolvería y la sustitución degradaría a 422 con el artefacto ya subido.
        var activa = await _reader
            .FindActiveByNumberAsync(command.TenantId, command.DocumentNumber!.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return activa?.Id;
    }

    /// <summary>
    /// Código hash recortado, o <c>null</c> si viene vacío o en blanco. El sello del documento decide si
    /// imprime la línea "Hash:" comprobando que no esté vacío, así que una cadena de espacios pasaría por
    /// código válido y pintaría una línea sin valor.
    /// </summary>
    private static string? NormalizeCodigoHash(string? codigoHash)
    {
        var v = codigoHash?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static (List<SignatureVaultValidationError> Errors, byte[]? Artifact) Validate(
        CreateSignatureVaultCommand command)
    {
        var errors = new List<SignatureVaultValidationError>();

        Require(errors, "documentType", command.DocumentType);
        Require(errors, "documentNumber", command.DocumentNumber);
        // nitEmpresa DEPRECADO (HU #10930, Feature #10929): ya no es obligatorio.
        Require(errors, "fullName", command.FullName);

        if (command.VigenciaHasta < command.VigenciaDesde)
        {
            errors.Add(new SignatureVaultValidationError(
                "vigenciaHasta", "vigencia_invalida",
                "La vigencia hasta no puede ser anterior a la vigencia desde."));
        }

        // El contrato ya declaraba `maxLength: 100` y la columna es varchar(100), pero nadie lo
        // comprobaba: un código más largo llegaba hasta PostgreSQL y salía como 500 (error 22001) en vez
        // de como el 422 legible que promete el contrato.
        if (command.CodigoHash?.Trim().Length > CodigoHashMaxLength)
        {
            errors.Add(new SignatureVaultValidationError(
                "codigoHash", "codigo_hash_invalido",
                $"El código hash no puede superar {CodigoHashMaxLength} caracteres."));
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
