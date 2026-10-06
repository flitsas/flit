using Flit.Infrastructure.KyverumRunt;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor de consultas Kyverum RUNT vehículo (HU #10478), key <c>kyverum_runt</c>. Consulta por
/// VIN (prioritario) o por placa+documento del propietario vía <see cref="KyverumRuntApiClient"/> y
/// normaliza con <see cref="KyverumRuntVehicleResultMapper"/> al mismo <see cref="ConsultationResult"/>
/// que Verifik. NUNCA lanza al handler: <c>ok:false</c> (no encontrado) y errores de transporte se
/// mapean a checks (<c>fail</c>/<c>error</c>). Lee las mismas claves de contexto que
/// <see cref="VerifikConsultationProvider"/> para ser intercambiable en la cadena de proveedores.
/// </summary>
internal sealed class KyverumRuntVehicleConsultationProvider(KyverumRuntApiClient client) : IConsultationProvider
{
    public string Key => "kyverum_runt";

    public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var vin = GetValue(ctx, "vin");
        var plate = GetValue(ctx, "plate");

        KyverumRuntVehicleQuery query;
        if (IsValidVin(vin))
        {
            query = new KyverumRuntVehicleQuery(Vin: vin, Placa: null, Documento: null, TipoDocumento: null);
        }
        else if (!string.IsNullOrWhiteSpace(plate))
        {
            var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
            var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");

            if (string.IsNullOrWhiteSpace(documentNumber))
                return RequiresOwnerDocument();

            query = new KyverumRuntVehicleQuery(
                Vin: null, Placa: plate, Documento: documentNumber, TipoDocumento: KyverumRuntDocType.Normalize(documentType));
        }
        else
        {
            return InputError("Se requiere VIN o placa para consultar el vehículo");
        }

        try
        {
            var (response, raw) = await client.ConsultarVehiculoConCrudoAsync(query, ct);
            var result = KyverumRuntVehicleResultMapper.MapVehicle(response);

            // HU #11304 — se adjunta la respuesta cruda para que la ingesta la persista (sanitizada).
            // Es lo que permite corregir un mapeo y reprocesar sin volver a pagar la consulta.
            return result with
            {
                RawPayload = new RawProviderPayload(
                    ProviderKey: Key,
                    SubjectKind: RawProviderPayload.VehicleSubject,
                    SubjectKey: query.Vin ?? query.Placa,
                    PayloadJson: raw,
                    QueriedAt: DateTimeOffset.UtcNow),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (KyverumRuntException ex) when (ex.IsNotFound)
        {
            return VehicleNotFound();
        }
        catch (KyverumRuntException)
        {
            return ProviderUnavailable();
        }
    }

    private static ConsultationResult VehicleNotFound() =>
        new("kyverum_runt", "red",
            [new ConsultationCheck("vehiculo", "Vehículo RUNT", "fail", "kyverum_runt", "Vehículo no encontrado en RUNT")],
            []);

    // No se pudo verificar el vehículo (no-200/timeout/red). Dato crítico → check "error" (bloqueo
    // duro, overall red) con mensaje amigable que NO expone proveedor ni código HTTP crudo.
    private static ConsultationResult ProviderUnavailable() =>
        new("kyverum_runt", "red",
            [new ConsultationCheck("provider", "Consulta de vehículo", "error", "kyverum_runt",
                "No fue posible verificar la información del vehículo en el RUNT en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult RequiresOwnerDocument() =>
        new("kyverum_runt", "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", "kyverum_runt",
                "Se requiere documento del propietario para consulta por placa")],
            []);

    private static ConsultationResult InputError(string message) =>
        new("kyverum_runt", "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", "kyverum_runt", message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var value) ? value : null;

    private static bool IsValidVin(string? vin) =>
        !string.IsNullOrWhiteSpace(vin) &&
        vin.Length == 17 &&
        vin.All(char.IsLetterOrDigit);
}
