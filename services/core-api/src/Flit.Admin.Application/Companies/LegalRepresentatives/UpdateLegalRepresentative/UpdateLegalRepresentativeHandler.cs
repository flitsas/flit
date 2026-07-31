namespace Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;

/// <summary>
/// Edición de un representante legal (HU #10901, AC2/AC3). Delega en
/// <see cref="LegalRepresentativeWriter"/>: valida (422), verifica existencia en el tenant (404 si no
/// existe), re-upserta la compañía, re-resuelve firma/identidad y persiste con la marca de tipos de
/// trámite. Si no hay firma ni identidad vigente, incluye la señal <c>sin_firma_ni_identidad</c>.
/// </summary>
public sealed class UpdateLegalRepresentativeHandler
{
    private readonly LegalRepresentativeWriter _writer;

    public UpdateLegalRepresentativeHandler(LegalRepresentativeWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public Task<LegalRepresentativeWriteResult> HandleAsync(
        UpdateLegalRepresentativeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _writer.WriteAsync(
            new LegalRepresentativeWriteInput(
                command.TenantId,
                command.Id,
                command.CompanyNit,
                command.CompanyName,
                command.CompanyEmail,
                command.CompanyAddress,
                command.CompanyCity,
                command.CompanyPhone,
                command.DocumentType,
                command.DocumentNumber,
                command.FirstLastName,
                command.SecondLastName,
                command.Name,
                command.Email,
                command.Address,
                command.City,
                command.Phone,
                command.ProcedureTypeIds,
                command.ActorBy,
                command.Companies,
                command.SignatureVaultId),
            cancellationToken);
    }
}
