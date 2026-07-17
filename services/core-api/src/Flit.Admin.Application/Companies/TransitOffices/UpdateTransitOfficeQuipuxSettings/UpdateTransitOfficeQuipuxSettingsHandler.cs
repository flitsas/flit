using System.Text.RegularExpressions;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;

/// <summary>
/// Caso de uso de parametrización Quipux de una secretaría del catálogo (HU #10710): carga
/// manual del <c>divipo_code</c> y de las tres banderas por familia de trámite
/// (matrícula / traspaso / otros), que replican
/// <c>id_parinttrasec_registration/_transfer/_otherservice</c> de FLIT 1.0.
///
/// Reglas de negocio:
/// <list type="bullet">
/// <item>Un DIVIPO vacío NO es un error MIENTRAS no haya banderas activas: es el estado normal
/// de una secretaría aún no integrada (hoy 311 de 317). Se normaliza a <c>null</c> y la
/// secretaría queda no elegible.</item>
/// <item>Activar una bandera sin DIVIPO SÍ es un error (<c>DivipoRequiredForFlags</c> → 422):
/// dejaría a la secretaría declarando que radica sin ser elegible, un estado inconsistente que
/// engaña al administrador. El DIVIPO es obligatorio en cuanto se enciende una familia, así el
/// estado «banderas sin DIVIPO» es imposible de persistir.</item>
/// </list>
/// </summary>
/// <remarks>
/// No confundir con <c>admin.transit_office_profiles.operation_mode = 'quipux'</c> (HU #10215):
/// aquel describe al OT-CLIENTE cuya consola queda en solo lectura. Esto describe a la
/// secretaría DESTINO a la que FLIT radica, normalmente ajena a FLIT como cliente.
/// </remarks>
public sealed partial class UpdateTransitOfficeQuipuxSettingsHandler
{
    /// <summary>
    /// El DIVIPO es un código numérico de la división político-administrativa y conserva los
    /// ceros a la izquierda (Medellín = <c>05001</c>), por eso se trata como texto y no como
    /// entero. La columna admite 20 caracteres.
    /// </summary>
    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex DivipoCodePattern();

    private readonly ITransitOfficeQuipuxSettingsWriter _writer;

    public UpdateTransitOfficeQuipuxSettingsHandler(ITransitOfficeQuipuxSettingsWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<UpdateTransitOfficeQuipuxSettingsResult> HandleAsync(
        UpdateTransitOfficeQuipuxSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (request.QuipuxRegistration is null || request.QuipuxTransfer is null || request.QuipuxOther is null)
        {
            return UpdateTransitOfficeQuipuxSettingsResult.Failure(
                UpdateTransitOfficeQuipuxSettingsStatus.MissingFlags);
        }

        // "" y "   " se tratan como "no lo sé" → null, no como un DIVIPO en blanco: la columna
        // distingue desconocido (null) de conocido, y un vacío nunca debe pasar por conocido.
        var divipoCode = string.IsNullOrWhiteSpace(request.DivipoCode)
            ? null
            : request.DivipoCode.Trim();

        if (divipoCode is not null && !DivipoCodePattern().IsMatch(divipoCode))
        {
            return UpdateTransitOfficeQuipuxSettingsResult.Failure(
                UpdateTransitOfficeQuipuxSettingsStatus.InvalidDivipoCode);
        }

        var matricula = request.QuipuxRegistration.Value;
        var traspaso = request.QuipuxTransfer.Value;
        var otros = request.QuipuxOther.Value;

        // El DIVIPO es obligatorio en cuanto se enciende una familia: sin él la secretaría no es
        // elegible y persistir la bandera crearía el estado inconsistente «declara pero no
        // radica». Sin banderas, un DIVIPO vacío sigue siendo válido (secretaría no integrada).
        if (divipoCode is null && (matricula || traspaso || otros))
        {
            return UpdateTransitOfficeQuipuxSettingsResult.Failure(
                UpdateTransitOfficeQuipuxSettingsStatus.DivipoRequiredForFlags);
        }

        var updated = await _writer.UpdateQuipuxSettingsAsync(
            command.TransitOfficeId,
            divipoCode,
            matricula,
            traspaso,
            otros,
            cancellationToken).ConfigureAwait(false);

        if (!updated)
        {
            return UpdateTransitOfficeQuipuxSettingsResult.Failure(
                UpdateTransitOfficeQuipuxSettingsStatus.NotFound);
        }

        return UpdateTransitOfficeQuipuxSettingsResult.Success(
            new TransitOfficeQuipuxSettingsResponse(
                command.TransitOfficeId,
                divipoCode,
                matricula,
                traspaso,
                otros,
                Elegible: divipoCode is not null && (matricula || traspaso || otros)));
    }
}
