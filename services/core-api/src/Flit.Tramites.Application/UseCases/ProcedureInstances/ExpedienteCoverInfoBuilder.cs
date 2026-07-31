using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Arma los datos de la portada del expediente (HU #10857) desde la instancia: código = referencia,
/// placa y secretaría desde <c>field_values</c> (mismas claves que el FUR), tipo de trámite legible
/// desde la modalidad. Requiere que la consulta haya cargado <c>FieldValues</c>.
/// </summary>
public static class ExpedienteCoverInfoBuilder
{
    public static ExpedienteCoverInfo FromInstance(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        return new ExpedienteCoverInfo(
            CodigoTramite: instance.ReferenceNumber,
            Placa: Get(fv, "plate"),
            TipoTramite: HumanizeModalidad(instance.ModalidadEntrada),
            SecretariaTransito: Get(fv, "transit_office_name"),
            CompaniaRadicadora: Get(fv, "company_name") ?? Get(fv, "radicadora"));
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string HumanizeModalidad(string? modalidad)
    {
        if (string.IsNullOrWhiteSpace(modalidad))
            return "-";

        // "matricula_inicial" -> "Matricula inicial"; primera letra en mayúscula, guiones bajos a espacios.
        var text = modalidad.Trim().Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
