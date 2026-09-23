using Flit.Admin.Application.Consolidados;

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
    private readonly IConsolidadoInvalidacionMasiva? _invalidacion;

    /// <param name="invalidacion">
    /// HU #12789 — invalida en bloque los consolidados de la compañía. Opcional para no romper a los
    /// llamadores que no lo necesitan; en DI siempre se inyecta.
    /// </param>
    public CreateLegalRepresentativeHandler(
        LegalRepresentativeWriter writer,
        IConsolidadoInvalidacionMasiva? invalidacion = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _invalidacion = invalidacion;
    }

    public async Task<LegalRepresentativeWriteResult> HandleAsync(
        CreateLegalRepresentativeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _writer.WriteAsync(
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
                command.Companies,
                command.SignatureVaultId),
            cancellationToken).ConfigureAwait(false);

        // HU #12789 AC2 — el RL (y la escritura que se le resuelve) entra al expediente: los
        // consolidados en curso de la compañía quedan invalidados. Solo si el guardado prosperó.
        if (result.IsValid && _invalidacion is not null)
        {
            await _invalidacion.InvalidarPorCompaniaAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
