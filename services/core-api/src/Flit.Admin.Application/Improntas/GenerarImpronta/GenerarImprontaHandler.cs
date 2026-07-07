using System.Globalization;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Improntas;
using Flit.Modules.Improntas.Domain;

namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>
/// Caso de uso de generación del Certificado de Improntas Digitales vehicular (Res. 17145/2023,
/// Mintransporte), HU #10467.
///
/// Flujo: (1) valida placa + documento del propietario + los campos de organización/operador
/// (obligatorios de facto, ver plan de Feature #10462) — 422 sin tocar el proveedor externo ni
/// persistir nada; (2) invoca <see cref="IImprontaExternalClient.GenerarAsync"/>
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
    public async Task<GenerarImprontaResult> HandleAsync(
        GenerarImprontaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var placa = request.Placa?.Trim() ?? string.Empty;
        var documento = request.Documento?.Trim() ?? string.Empty;
        var orgNombre = request.OrgNombre?.Trim() ?? string.Empty;
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
                    NumMotor: null,
                    NumChasis: null,
                    NumSerie: null,
                    Marca: null,
                    Linea: null,
                    Modelo: null,
                    OrgNombre: orgNombre,
                    OrgNit: null,
                    OrgCiudad: null,
                    Operador: operador,
                    Vin: null),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ImprontaRuntException ex)
        {
            return MapProviderError(ex);
        }

        var pdfBytes = ImprontaPdfDecoder.Decode(providerResult.PdfDataUri);

        var generation = new ImprontaGeneration
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            FlitUserId = command.FlitUserId,
            Radicado = providerResult.Radicado,
            HashSha256 = providerResult.Hash,
            FechaImpresa = ParseFechaImpresa(providerResult.FechaImpresa),
            Placa = placa,
            NumMotor = null,
            NumChasis = null,
            NumSerie = null,
            Marca = null,
            Linea = null,
            Modelo = null,
            OrgNombre = orgNombre,
            OrgNit = string.Empty,
            OrgCiudad = string.Empty,
            Operador = operador,
            PdfContent = pdfBytes,
            PdfSizeBytes = pdfBytes.Length,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await repository.SaveAsync(generation, cancellationToken).ConfigureAwait(false);

        return GenerarImprontaResult.Success(pdfBytes, providerResult.Radicado, providerResult.Hash, providerResult.FechaImpresa);
    }

    /// <summary>Si el proveedor envía una fecha que no se puede parsear, se usa UtcNow como fallback defensivo.</summary>
    private static DateTimeOffset ParseFechaImpresa(string fechaImpresa) =>
        DateTimeOffset.TryParse(
            fechaImpresa, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static GenerarImprontaResult MapProviderError(ImprontaRuntException ex) =>
        ImprontaProviderErrorClassifier.Classify(ex) switch
        {
            ImprontaProviderErrorKind.Unavailable => GenerarImprontaResult.ProviderUnavailable(ex.Message),
            ImprontaProviderErrorKind.Validation => GenerarImprontaResult.ProviderValidation(ex.Message),
            _ => GenerarImprontaResult.ProviderUnauthorized(ex.Message),
        };
}
