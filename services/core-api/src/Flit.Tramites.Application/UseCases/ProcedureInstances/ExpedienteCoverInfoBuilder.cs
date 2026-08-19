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
    /// <param name="instance">Instancia con <c>FieldValues</c> cargados.</param>
    /// <param name="companiaRadicadora">
    /// Bug #11612 - compania radicadora resuelta EN MEMORIA por <see cref="CompaniaRadicadoraResolver"/>
    /// (razon social del tenant dueno del tramite). Es un valor de RESERVA: solo se usa cuando el tramite
    /// no trae la clave en <c>field_values</c>, asi que nunca pisa lo que capturo el operador. No se
    /// persiste: el trigger de inmutabilidad de <c>field_values</c> prohibe escribir en un tramite ya
    /// radicado (ver el resolver).
    /// </param>
    public static ExpedienteCoverInfo FromInstance(ProcedureInstance instance, string? companiaRadicadora = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Tolerante a claves duplicadas: field_values no tiene indice unico y un ToDictionary directo
        // tumbaria la portada entera con ArgumentException.
        var fv = ProcedureFieldValues.ToDictionary(instance);

        return new ExpedienteCoverInfo(
            CodigoTramite: instance.ReferenceNumber,
            Placa: Get(fv, "plate"),
            TipoTramite: HumanizeModalidad(instance.ModalidadEntrada),
            SecretariaTransito: Get(fv, "transit_office_name"),
            CompaniaRadicadora: Get(fv, "company_name")
                                ?? Get(fv, "radicadora")
                                ?? Normalizar(companiaRadicadora));
    }

    private static string? Normalizar(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
