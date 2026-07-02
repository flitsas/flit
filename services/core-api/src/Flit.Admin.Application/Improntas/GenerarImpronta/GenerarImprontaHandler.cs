using System.Globalization;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Improntas;

namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>
/// Caso de uso de generación del Certificado de Improntas Digitales vehicular (Res. 17145/2023,
/// Mintransporte), HU #10467.
///
/// Flujo: (1) valida placa + al menos uno de numMotor/numChasis/numSerie + los campos de
/// organización/operador (obligatorios de facto, ver plan de Feature #10462) — 422 sin tocar el
/// proveedor externo ni persistir nada; (2) invoca <see cref="IImprontaExternalClient.GenerarAsync"/>
/// (HU #10465); (3) decodifica el Data URI base64 del PDF devuelto (recorta el prefijo
/// <c>data:application/pdf;base64,</c>) a <c>byte[]</c>; (4) persiste el registro de trazabilidad vía
/// <see cref="IImprontaRepository.SaveAsync"/> (HU #10466); (5) retorna los bytes + metadata para que
/// el endpoint sirva el archivo con <c>Results.File</c>.
///
/// Manejo de errores del proveedor (AC3): <see cref="ImprontaRuntException"/> no expone el código de
/// error crudo de Kyverum (solo <see cref="ImprontaRuntException.Message"/>, ya traducido a español, y
/// <see cref="ImprontaRuntException.IsTransient"/> — ver <c>ImprontaRuntClient.BuildErrorAsync</c>,
/// HU #10465). Se clasifica así: <see cref="ImprontaRuntException.IsTransient"/> true ⇒
/// <c>UPSTREAM_UNAVAILABLE</c>/timeout/red ⇒ 502 (<see cref="GenerarImprontaResult.ProviderUnavailable"/>);
/// false + mensaje con el prefijo estable "Datos de la impronta inválidos:" (armado por
/// <c>ImprontaRuntClient.BuildValidationMessage</c> para <c>VALIDATION_ERROR</c>) ⇒ 422/400
/// (<see cref="GenerarImprontaResult.ProviderValidation"/>); cualquier otro caso no transitorio
/// (<c>UNAUTHORIZED</c> u otro código desconocido) ⇒ 401 (<see cref="GenerarImprontaResult.ProviderUnauthorized"/>).
/// Ningún fallo del proveedor persiste un registro incompleto.
/// </summary>
public sealed class GenerarImprontaHandler(IImprontaExternalClient externalClient, IImprontaRepository repository)
{
    private const string PdfDataUriPrefix = "data:application/pdf;base64,";

    /// <summary>
    /// Prefijo estable del mensaje que <c>ImprontaRuntClient.BuildValidationMessage</c> arma para
    /// <c>VALIDATION_ERROR</c> — único indicio disponible en esta capa para distinguirlo de
    /// <c>UNAUTHORIZED</c>, ya que <see cref="ImprontaRuntException"/> no expone el código crudo.
    /// </summary>
    private const string ProviderValidationMessagePrefix = "Datos de la impronta inválidos:";

    /// <summary>
    /// Prefijo que <c>ImprontaRuntClient</c> arma cuando Kyverum responde 200 con <c>ok:false</c> (no
    /// es un error HTTP, es un motivo de negocio — ej. "el RUNT no expone identificadores para este
    /// vehículo"). Se clasifica igual que <see cref="ProviderValidationMessagePrefix"/> (422): no se
    /// puede generar el certificado con los datos enviados, pero no es un fallo de autenticación.
    /// </summary>
    private const string ProviderUnavailableForRequestMessagePrefix = "Impronta no disponible:";

    public async Task<GenerarImprontaResult> HandleAsync(
        GenerarImprontaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var placa = request.Placa?.Trim() ?? string.Empty;
        var documento = request.Documento?.Trim() ?? string.Empty;
        var numMotor = NormalizeOptional(request.NumMotor);
        var numChasis = NormalizeOptional(request.NumChasis);
        var numSerie = NormalizeOptional(request.NumSerie);
        var marca = NormalizeOptional(request.Marca);
        var linea = NormalizeOptional(request.Linea);
        var modelo = NormalizeOptional(request.Modelo);
        var orgNombre = request.OrgNombre?.Trim() ?? string.Empty;
        var orgNit = NormalizeOptional(request.OrgNit);
        var orgCiudad = NormalizeOptional(request.OrgCiudad);
        var operador = request.Operador?.Trim() ?? string.Empty;

        if (placa.Length == 0)
        {
            errors.Add(new CompanyValidationError("placa", "La placa es obligatoria."));
        }

        if (documento.Length == 0)
        {
            errors.Add(new CompanyValidationError(
                "documento", "El documento del propietario es obligatorio (requerido por Kyverum RUNT para consultas por placa)."));
        }

        // numMotor/numChasis/numSerie y orgNit/orgCiudad NO son obligatorios: verificado contra el
        // proveedor real (Kyverum genera la impronta sin ellos, resolviendo los identificadores del
        // vehículo directamente desde el RUNT vía placa+documento). CONTRATO-API.md documentaba "al
        // menos un identificador obligatorio", pero eso no se sostuvo en pruebas reales.

        if (orgNombre.Length == 0)
        {
            errors.Add(new CompanyValidationError("orgNombre", "El nombre de la organización es obligatorio."));
        }

        if (operador.Length == 0)
        {
            errors.Add(new CompanyValidationError("operador", "El operador es obligatorio."));
        }

        if (errors.Count > 0)
        {
            return GenerarImprontaResult.Invalid(errors);
        }

        ImprontaExternalResult providerResult;
        try
        {
            providerResult = await externalClient.GenerarAsync(
                new ImprontaExternalRequest(
                    Placa: placa,
                    Documento: documento,
                    NumMotor: numMotor,
                    NumChasis: numChasis,
                    NumSerie: numSerie,
                    Marca: marca,
                    Linea: linea,
                    Modelo: modelo,
                    OrgNombre: orgNombre,
                    OrgNit: orgNit,
                    OrgCiudad: orgCiudad,
                    Operador: operador),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ImprontaRuntException ex)
        {
            return MapProviderError(ex);
        }

        var pdfBytes = DecodePdfDataUri(providerResult.PdfDataUri);

        var generation = new ImprontaGeneration
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            FlitUserId = command.FlitUserId,
            Radicado = providerResult.Radicado,
            HashSha256 = providerResult.Hash,
            FechaImpresa = ParseFechaImpresa(providerResult.FechaImpresa),
            Placa = placa,
            NumMotor = numMotor,
            NumChasis = numChasis,
            NumSerie = numSerie,
            Marca = marca,
            Linea = linea,
            Modelo = modelo,
            OrgNombre = orgNombre,
            OrgNit = orgNit ?? string.Empty,
            OrgCiudad = orgCiudad ?? string.Empty,
            Operador = operador,
            PdfContent = pdfBytes,
            PdfSizeBytes = pdfBytes.Length,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await repository.SaveAsync(generation, cancellationToken).ConfigureAwait(false);

        return GenerarImprontaResult.Success(pdfBytes, providerResult.Radicado, providerResult.Hash, providerResult.FechaImpresa);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Decodifica el certificado a bytes recortando el prefijo <c>data:application/pdf;base64,</c> si
    /// está presente (defensivo: si Kyverum alguna vez devolviera el base64 puro, también funciona).
    /// </summary>
    private static byte[] DecodePdfDataUri(string dataUri)
    {
        var base64 = dataUri.StartsWith(PdfDataUriPrefix, StringComparison.Ordinal)
            ? dataUri[PdfDataUriPrefix.Length..]
            : dataUri;
        return Convert.FromBase64String(base64);
    }

    /// <summary>Si el proveedor envía una fecha que no se puede parsear, se usa UtcNow como fallback defensivo.</summary>
    private static DateTimeOffset ParseFechaImpresa(string fechaImpresa) =>
        DateTimeOffset.TryParse(
            fechaImpresa, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static GenerarImprontaResult MapProviderError(ImprontaRuntException ex)
    {
        if (ex.IsTransient)
        {
            return GenerarImprontaResult.ProviderUnavailable(ex.Message);
        }

        return ex.Message.StartsWith(ProviderValidationMessagePrefix, StringComparison.Ordinal)
            || ex.Message.StartsWith(ProviderUnavailableForRequestMessagePrefix, StringComparison.Ordinal)
            ? GenerarImprontaResult.ProviderValidation(ex.Message)
            : GenerarImprontaResult.ProviderUnauthorized(ex.Message);
    }
}
