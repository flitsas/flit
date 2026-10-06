using Flit.Infrastructure.KyverumRunt;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor de consultas Kyverum RUNT persona/conductor (HU #10478), key
/// <c>kyverum_runt_conductor</c>. Consulta por documento (+ tipo opcional) vía
/// <see cref="KyverumRuntApiClient"/> y normaliza con <see cref="KyverumRuntConductorResultMapper"/>
/// al mismo <see cref="ConsultationResult"/> que <c>verifik_conductor</c>. NUNCA lanza al handler:
/// persona no encontrada (<c>ok:false</c>) → check unknown; error de transporte → check "error". Lee
/// las mismas claves de contexto que <see cref="VerifikConductorConsultationProvider"/>.
/// </summary>
internal sealed class KyverumRuntConductorConsultationProvider(KyverumRuntApiClient client) : IConsultationProvider
{
    public string Key => "kyverum_runt_conductor";

    public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "document_number") ?? GetValue(ctx, "documentNumber");

        if (string.IsNullOrWhiteSpace(documentNumber))
            return InputError("Se requiere document_number para consultar RUNT conductor");

        try
        {
            var response = await client.ConsultarPersonaAsync(
                new KyverumRuntPersonaQuery(documentNumber, KyverumRuntDocType.Normalize(documentType)), ct);
            return KyverumRuntConductorResultMapper.Map(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (KyverumRuntException ex) when (ex.IsNotFound)
        {
            return NotFound();
        }
        catch (KyverumRuntException)
        {
            return ProviderUnavailable();
        }
    }

    // Persona no hallada: sin campos de nombre + check unknown → el caller cae al fallback manual.
    private static ConsultationResult NotFound() =>
        new(Key_, "yellow",
            [new ConsultationCheck("conductor", "Persona en RUNT", "unknown", Key_, "Persona no encontrada en RUNT")],
            []);

    // No se pudo consultar (no-200/timeout/red): check "error" (contrato consistente); el lookup de
    // persona solo lee HydratedFields y cae a ingreso manual, así que no bloquea la identidad.
    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta de persona (RUNT)", "error", Key_,
                "No fue posible verificar la información en el RUNT en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "kyverum_runt_conductor";
}
