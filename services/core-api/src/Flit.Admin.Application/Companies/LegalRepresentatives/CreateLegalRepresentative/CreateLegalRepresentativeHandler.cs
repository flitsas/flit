namespace Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;

/// <summary>
/// Alta de un representante legal (HU #10901, AC2/AC3). Delega en <see cref="LegalRepresentativeWriter"/>
/// la validación (422), el upsert de la compañía, la resolución de firma/identidad y la persistencia
/// con la marca de tipos de trámite. Si no hay firma ni identidad vigente, el resultado sigue siendo
/// válido (201) e incluye la señal <c>sin_firma_ni_identidad</c>.
/// </summary>
public sealed class CreateLegalRepresentativeHandler
{
    private readonly LegalRepresentativeWriter _writer;

    public CreateLegalRepresentativeHandler(LegalRepresentativeWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public Task<LegalRepresentativeWriteResult> HandleAsync(
        CreateLegalRepresentativeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _writer.WriteAsync(
            new LegalRepresentativeWriteInput(
                command.TenantId,
                Id: null,
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
                command.Companies),
            cancellationToken);
    }
}
