using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.UpdateSignatureVault;

/// <summary>Desenlace de la edición de una firma del baúl.</summary>
public enum UpdateSignatureVaultOutcome
{
    /// <summary>La firma existía, estaba activa y quedó editada.</summary>
    Updated,

    /// <summary>No existe una firma con ese id en el tenant.</summary>
    NotFound,

    /// <summary>Existe pero está revocada: su contenido es histórico y no se corrige.</summary>
    Revoked,

    /// <summary>Datos inválidos (422).</summary>
    Invalid,
}

/// <summary>Petición de edición (los campos corregibles de una firma).</summary>
public sealed class UpdateSignatureVaultCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public string? FullName { get; init; }
    public string? CodigoHash { get; init; }
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}

/// <summary>Resultado de la edición: desenlace + errores cuando es inválida.</summary>
public sealed record UpdateSignatureVaultResult(
    UpdateSignatureVaultOutcome Outcome,
    IReadOnlyList<SignatureVaultValidationError> Errors)
{
    public static UpdateSignatureVaultResult Ok() => new(UpdateSignatureVaultOutcome.Updated, []);
    public static UpdateSignatureVaultResult NotFound() => new(UpdateSignatureVaultOutcome.NotFound, []);
    public static UpdateSignatureVaultResult Revoked() => new(UpdateSignatureVaultOutcome.Revoked, []);

    public static UpdateSignatureVaultResult Invalid(IReadOnlyList<SignatureVaultValidationError> errors) =>
        new(UpdateSignatureVaultOutcome.Invalid, errors);
}

/// <summary>
/// Edita una firma del baúl. Cierra el CRUD, que hasta ahora era alta + consulta + revocación: un dato
/// mal capturado —el código hash sobre todo, que es lo que se estampa en los documentos— solo se podía
/// corregir revocando la firma y volviéndola a registrar.
///
/// <para><b>Lo que NO se edita.</b> El <b>documento</b> identifica a la persona dueña de la firma:
/// cambiarlo convertiría la fila en la firma de otra persona conservando su historial. El
/// <b>artefacto</b> tampoco: lo ya emitido se estampó con esa imagen y con su huella, así que
/// sustituirla en sitio invalidaría en silencio documentos ya firmados. Para cambiar la imagen se
/// captura una firma nueva, que revoca la anterior y la conserva.</para>
///
/// <para>Solo sobre firmas ACTIVAS: el contenido de una revocada es histórico.</para>
/// </summary>
public sealed class UpdateSignatureVaultHandler
{
    /// <summary>Longitud de <c>admin.signature_vault.codigo_hash</c> (varchar(100)).</summary>
    private const int CodigoHashMaxLength = 100;

    private readonly ISignatureVaultReader _reader;
    private readonly ISignatureVaultRepository _repository;

    public UpdateSignatureVaultHandler(ISignatureVaultReader reader, ISignatureVaultRepository repository)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateSignatureVaultResult> HandleAsync(
        UpdateSignatureVaultCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _reader
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return UpdateSignatureVaultResult.NotFound();
        }

        if (existing.Estado != SignatureVaultEstado.Activa)
        {
            return UpdateSignatureVaultResult.Revoked();
        }

        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return UpdateSignatureVaultResult.Invalid(errors);
        }

        var codigo = command.CodigoHash?.Trim();
        var updated = await _repository.UpdateAsync(
            new UpdateSignatureVaultData(
                command.Id,
                command.TenantId,
                command.FullName!.Trim(),
                string.IsNullOrEmpty(codigo) ? null : codigo,
                command.VigenciaDesde,
                command.VigenciaHasta,
                command.ChangedBy,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        // El repositorio devuelve false si entre la lectura y la escritura la firma se revocó.
        return updated ? UpdateSignatureVaultResult.Ok() : UpdateSignatureVaultResult.Revoked();
    }

    private static List<SignatureVaultValidationError> Validate(UpdateSignatureVaultCommand command)
    {
        var errors = new List<SignatureVaultValidationError>();

        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            errors.Add(new SignatureVaultValidationError(
                "fullName", "requerido", "El nombre del firmante es obligatorio."));
        }

        if (command.VigenciaHasta < command.VigenciaDesde)
        {
            errors.Add(new SignatureVaultValidationError(
                "vigenciaHasta", "vigencia_invalida",
                "La vigencia hasta no puede ser anterior a la vigencia desde."));
        }

        if (command.CodigoHash?.Trim().Length > CodigoHashMaxLength)
        {
            errors.Add(new SignatureVaultValidationError(
                "codigoHash", "codigo_hash_invalido",
                $"El código hash no puede superar {CodigoHashMaxLength} caracteres."));
        }

        return errors;
    }
}
