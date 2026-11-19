using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error de la ventana de anulación de la caja. El de validación es el del contrato con el front.</summary>
public static class CajaAnulacionErrores
{
    public const string VentanaInvalida = "ventana_anulacion_invalida";
}

/// <summary>
/// Resuelve la ventana de anulación efectiva de un organismo: la configurada o, si nunca la configuró, el valor por defecto (15). La usan
/// la vista de Cobro y la confirmación del cobro en caja, que manda estos minutos al portal para que los guarde en el cobro.
/// </summary>
public sealed class LeerVentanaAnulacionCajaHandler(IPersonaNaturalCajaAnulacionRepository repo, CajaAnulacionOpciones opciones)
{
    public async Task<VentanaAnulacionCajaView> HandleAsync(Guid organismoId, CancellationToken ct = default)
    {
        var guardado = await repo.LeerAsync(organismoId, ct).ConfigureAwait(false);
        return guardado is { } minutos
            ? new VentanaAnulacionCajaView(minutos, false, opciones.MaxMinutos)
            : new VentanaAnulacionCajaView(opciones.PorDefectoMinutos, true, opciones.MaxMinutos);
    }
}

/// <summary>
/// Cambia la ventana de anulación del organismo. Valida el rango 0..max, deja el cambio en el historial (quién, antes, después, cuándo) y en la
/// auditoría unificada. Solo aplica a cobros nuevos: el cobro guarda su propio límite al confirmarse. El permiso (ot_admin de su organismo,
/// SuperAdmin de cualquiera, nunca el cajero) lo decide el endpoint.
/// </summary>
public sealed class GuardarVentanaAnulacionCajaHandler(
    IPersonaNaturalCajaAnulacionRepository repo, IPersonaNaturalTarifaRepository tarifas, IAdminAuditWriter audit, CajaAnulacionOpciones opciones)
{
    /// <returns>El código de error o <c>null</c> si quedó guardado; la vista nueva sale en <paramref name="vista"/>.</returns>
    public async Task<(string? Falla, VentanaAnulacionCajaView? Vista)> HandleAsync(Guid organismoId, int? minutos, Guid actor, CancellationToken ct = default)
    {
        if (!opciones.Valida(minutos))
            return (CajaAnulacionErrores.VentanaInvalida, null);
        if (!await tarifas.OrganismoExisteAsync(organismoId, ct).ConfigureAwait(false))
            return (CobroPnErrores.OrganismoNoExiste, null);

        var nuevo = minutos!.Value;
        var antes = await repo.GuardarAsync(organismoId, nuevo, opciones.PorDefectoMinutos, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, "persona_natural_caja_settings", AuditVocabulary.Operations.Update, actor, organismoId,
            PersonaNaturalAuditoria.Json(("ventanaAnulacionMinutos", antes)),
            PersonaNaturalAuditoria.Json(("ventanaAnulacionMinutos", nuevo), ("organismoId", organismoId)), ct).ConfigureAwait(false);
        return (null, new VentanaAnulacionCajaView(nuevo, false, opciones.MaxMinutos));
    }
}

/// <summary>Historial de cambios de la ventana de un organismo, el más reciente primero (máximo <see cref="Maximo"/>).</summary>
public sealed class HistorialVentanaAnulacionCajaHandler(IPersonaNaturalCajaAnulacionRepository repo)
{
    public const int Maximo = 50;

    public Task<IReadOnlyList<CambioVentanaAnulacionView>> HandleAsync(Guid organismoId, CancellationToken ct = default) =>
        repo.HistorialAsync(organismoId, Maximo, ct);
}
