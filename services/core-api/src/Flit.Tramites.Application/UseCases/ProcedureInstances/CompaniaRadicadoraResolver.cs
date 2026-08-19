using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11612 — resuelve el nombre de la <b>compañía radicadora</b> que va en la portada del
/// expediente consolidado. La portada (<see cref="ExpedienteCoverInfoBuilder"/>) lo leía de la clave
/// <c>company_name</c> de <c>field_values</c>, que tenía consumidor pero NINGÚN productor: imprimía
/// siempre un guion. La fuente autoritativa es el tenant dueño del trámite
/// (<c>identity.tenants.legal_name</c>), vía <see cref="ICompaniaRadicadoraDirectory"/>.
///
/// <para><b>El valor NO se persiste.</b> Escribirlo en <c>field_values</c> —como hacía la primera
/// versión de esta corrección— revienta en Postgres: el trigger
/// <c>tramites.trg_field_value_immutable</c> (BEFORE INSERT OR UPDATE OR DELETE) solo admite
/// escrituras en borrador, en rechazado con subsanación activa o sobre unas claves puntuales del flujo
/// de placa, y el consolidado se genera sobre trámites YA RADICADOS. El INSERT abortaba la transacción
/// completa y se perdía además el documento recién generado. Esa inmutabilidad es deliberada: los
/// documentos radicados tienen peso legal y cambiar la portada de un consolidado ya emitido es justo
/// lo que debe impedir.</para>
///
/// <para>Consecuencia asumida (afecta al <b>AC4</b> del Bug #11612): al no quedar marcador persistido,
/// un trámite antiguo cuyo consolidado siga marcado como VIGENTE conservará el guion en la portada
/// hasta que el consolidado se invalide por las vías normales (regenerar el FUR, cambiar de estado,
/// adjuntar documentos, forzar la regeneración). No se fuerza una regeneración por esta causa: sin
/// marcador, "una sola vez por trámite" sería en realidad "en cada acceso".</para>
///
/// <para>Si el tenant no tiene razón social registrada no se inventa nada: la portada mantiene el
/// guion (<c>FlitCoverPageGenerator.Val</c>), que es lo correcto para un trámite sin compañía
/// radicadora (AC3).</para>
/// </summary>
public static class CompaniaRadicadoraResolver
{
    /// <summary>Clave canónica que lee la portada.</summary>
    public const string FieldKey = "company_name";

    /// <summary>Alias histórico aceptado por la portada como valor de reserva.</summary>
    public const string FieldKeyAlterno = "radicadora";

    /// <summary>
    /// Resuelve la razón social SOLO si el trámite no trae ya un nombre de compañía en
    /// <c>field_values</c> (lo que capturó el operador manda). Devuelve <c>null</c> cuando no hay nada
    /// que rellenar —ni una consulta al directorio se hace en ese caso— o cuando el tenant no tiene
    /// razón social.
    /// </summary>
    public static async Task<string?> ResolverAsync(
        ProcedureInstance instance,
        Guid tenantId,
        ICompaniaRadicadoraDirectory directory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(directory);

        if (!Falta(instance))
            return null;

        var razonSocial = await directory.GetRazonSocialAsync(tenantId, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(razonSocial) ? null : razonSocial.Trim();
    }

    /// <summary>
    /// <c>true</c> si el trámite no tiene compañía radicadora utilizable en <c>field_values</c> —
    /// mismas dos claves, y en el mismo orden, que lee <see cref="ExpedienteCoverInfoBuilder"/>.
    /// </summary>
    public static bool Falta(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var fv = ProcedureFieldValues.ToDictionary(instance);
        return string.IsNullOrWhiteSpace(Valor(fv, FieldKey)) && string.IsNullOrWhiteSpace(Valor(fv, FieldKeyAlterno));
    }

    private static string? Valor(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var value) ? value : null;
}
