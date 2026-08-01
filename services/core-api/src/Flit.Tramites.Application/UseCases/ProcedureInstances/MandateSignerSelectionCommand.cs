using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #11203 — un mandatario que el gestor puede elegir para que firme el mandato del trámite. Lleva
/// los datos que necesita para decidir: quién es, con qué documento y hasta cuándo vale su validación
/// de identidad. Sin identidad vigente el mandato no se puede aprobar, así que se informa aquí y no en
/// el último paso.
/// </summary>
public sealed record MandateSignerOptionDto(
    Guid Id,
    string Nombre,
    string TipoDocumento,
    string Documento,
    bool IdentidadVigente,
    DateTimeOffset? IdentidadHasta);

/// <summary>
/// Mandatarios disponibles para el trámite y cuál está elegido. <see cref="Editable"/> es falso fuera
/// de borrador: entonces el mandatario se muestra pero no se cambia (AC5).
/// </summary>
public sealed record MandateSignerSelectionDto(
    IReadOnlyList<MandateSignerOptionDto> Opciones,
    Guid? ElegidoId,
    bool Editable);

/// <summary>
/// HU #11203 — mandatarios habilitados para el organismo del trámite en la compañía gestora. Adelanta
/// al registro lo que hasta ahora se resolvía al aprobar: el gestor sabe desde el principio quién va a
/// firmar, en vez de descubrirlo (o tener que decidirlo el OT) al final.
/// </summary>
public sealed class ListMandateSignerOptionsHandler(
    IProcedureInstanceRepository repo,
    IMandateSignerDirectory directory)
{
    public async Task<(MandateSignerSelectionDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var editable = TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva);
        var transitOfficeId = MandateSignerSelectionResolver.ResolveTransitOfficeId(instance);

        // Sin organismo todavía no hay a quién ofrecer: en traspaso lo fija el RUNT al consultar el
        // vehículo y en matrícula lo elige el gestor en el primer paso (HU #11199/#11200).
        if (transitOfficeId is not { } officeId)
            return (new MandateSignerSelectionDto([], instance.MandateSignerId, editable), null);

        var candidatos = await directory
            .GetCandidatesAsync(officeId, tenantId, ct)
            .ConfigureAwait(false);

        var opciones = candidatos
            .Select(c => new MandateSignerOptionDto(
                c.Id, c.Nombre, c.TipoDocumento ?? "CC", c.Documento, c.IdentityVigente, c.IdentityValidUntil))
            .ToList();

        // AC3 — con un único mandatario habilitado no hay nada que decidir: queda elegido.
        var elegido = instance.MandateSignerId
            ?? (opciones.Count == 1 ? opciones[0].Id : null);

        return (new MandateSignerSelectionDto(opciones, elegido, editable), null);
    }
}

/// <summary>
/// HU #11203 (AC4/AC5) — fija el mandatario que firma. Solo en borrador (o subsanación): una vez el
/// trámite salió de allí, cambiar quién firma alteraría un documento ya generado y radicado.
/// </summary>
public sealed class SetMandateSignerHandler(
    IProcedureInstanceRepository repo,
    IMandateSignerDirectory directory)
{
    public async Task<string?> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        Guid mandateSignerId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return "not_found";

        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return "not_draft";

        var transitOfficeId = MandateSignerSelectionResolver.ResolveTransitOfficeId(instance);
        if (transitOfficeId is not { } officeId)
            return "sin_organismo";

        // El elegido tiene que ser uno de los habilitados para ese organismo y esa compañía: aceptar
        // cualquier id dejaría el trámite apuntando a un mandatario que no puede firmarlo, y eso solo
        // se descubriría al aprobar.
        var candidatos = await directory
            .GetCandidatesAsync(officeId, tenantId, ct)
            .ConfigureAwait(false);
        if (candidatos.All(c => c.Id != mandateSignerId))
            return "mandatario_no_habilitado";

        instance.MandateSignerId = mandateSignerId;
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }
}

/// <summary>Resolución compartida del organismo del trámite, para no duplicarla entre los dos casos.</summary>
internal static class MandateSignerSelectionResolver
{
    /// <summary>
    /// Organismo del trámite. Durante el wizard vive en <c>field_values.transit_office_id</c>; la columna
    /// solo se fija al radicar (<c>TramiteLifecycleService</c>), así que mirar solo la columna dejaría la
    /// elección sin candidatos justo cuando hace falta.
    /// </summary>
    public static Guid? ResolveTransitOfficeId(Flit.Tramites.Domain.Entities.ProcedureInstance instance)
    {
        if (instance.TransitOfficeId is { } columna && columna != Guid.Empty)
            return columna;

        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
