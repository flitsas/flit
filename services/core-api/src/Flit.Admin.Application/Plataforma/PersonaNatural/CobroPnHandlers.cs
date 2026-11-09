using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error estables de los medios de pago y switches de cobro del portal (HU #14393).</summary>
public static class CobroPnErrores
{
    public const string MedioInvalido = "PORTAL_COBRO_MEDIO_INVALIDO";
    public const string PinEnWeb = "PORTAL_COBRO_PIN_EN_WEB";
    public const string CobroInvalido = "PORTAL_COBRO_CONCEPTO_INVALIDO";
    public const string TipoObligatorio = "PORTAL_COBRO_TIPO_OBLIGATORIO";
    public const string TipoNoExiste = "PORTAL_COBRO_TIPO_NO_EXISTE";
    public const string OrganismoNoExiste = "PORTAL_COBRO_ORGANISMO_NO_EXISTE";
    public const string SinMonto = "PORTAL_COBRO_SIN_MONTO";
    public const string GestionAjena = "PORTAL_COBRO_GESTION_AJENA";
    public const string OtroOrganismo = "PORTAL_COBRO_OTRO_ORGANISMO";
    public const string SinOrganismo = "PORTAL_COBRO_SIN_ORGANISMO";
    public const string OrganismoRequerido = "PORTAL_COBRO_ORGANISMO_REQUERIDO";
}

/// <summary>
/// Con quién se trabaja: el organismo, o <c>null</c> para la web de persona (solo el SuperAdmin), y si quien llama es el
/// SuperAdmin. El <c>ot_admin</c> trabaja con el organismo de su token y solo mueve el switch del trámite.
/// </summary>
public sealed record AlcanceCobroPn(Guid? OrganismoId, bool EsSuperAdmin)
{
    public bool Web => OrganismoId is null;
}

public sealed record MedioCobroPnView(string Method, bool Enabled);

public sealed record ConceptoCobroPnView(string Charge, bool Enabled, long MontoCop, bool Editable);

public sealed record MedioDelTipoPnView(string Method, bool Ofrecido, IReadOnlyList<ConceptoCobroPnView> Conceptos);

public sealed record TipoCobroPnView(Guid TipoTramiteId, string Nombre, IReadOnlyList<MedioDelTipoPnView> Medios);

public sealed record CobroPortalView(IReadOnlyList<MedioCobroPnView> Medios, IReadOnlyList<TipoCobroPnView> Tipos);

/// <summary>
/// HU #14393 — lo que el administrador ve en Cobro: las banderas de los medios del portal y, por cada tipo y medio, los dos
/// switches con su monto. <c>MontoCop</c> es el del renglón base del concepto (derechos de tránsito; servicio FLIT): las
/// transformaciones y el envío siguen el switch de su grupo. Sin caché: lo guardado se ve en la siguiente consulta.
/// </summary>
public sealed class LeerCobroPnHandler(IPersonaNaturalCobroRepository cobro, IPersonaNaturalTarifaRepository tarifas)
{
    public async Task<CobroPortalView> HandleAsync(AlcanceCobroPn alcance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var config = await cobro.LeerAsync(alcance.OrganismoId, ct).ConfigureAwait(false);
        var tipos = await tarifas.ListarTiposDelPortalAsync(ct).ConfigureAwait(false);
        var montos = await cobro.MontosAsync(alcance.OrganismoId, [.. tipos.Select(t => t.Id)], ct).ConfigureAwait(false);
        var medios = MediosPagoPn.DelPortal(alcance.Web);

        return new CobroPortalView(
            [.. medios.Select(m => new MedioCobroPnView(m, config.MedioHabilitado(m)))],
            [.. tipos.Select(t => new TipoCobroPnView(
                t.Id, t.Nombre,
                [.. medios.Select(m =>
                {
                    var conceptos = CobrosPn.Todos.Select(c =>
                    {
                        var monto = montos.TryGetValue((t.Id, c), out var v) ? v : 0;
                        return new ConceptoCobroPnView(c, config.SwitchEncendido(t.Id, m, c, monto), monto, CobrosPn.Mueve(alcance.EsSuperAdmin, c));
                    }).ToList();
                    return new MedioDelTipoPnView(m, config.MedioHabilitado(m) && conceptos.Any(c => c.Enabled), conceptos);
                })]))]);
    }
}

/// <summary>HU #14393 AC1/AC5 — enciende o apaga la bandera de un medio en el ámbito del llamante; no toca otros portales.</summary>
public sealed class GuardarMedioCobroPnHandler(IPersonaNaturalCobroRepository cobro, IPersonaNaturalTarifaRepository tarifas, IAdminAuditWriter audit)
{
    public async Task<string?> HandleAsync(AlcanceCobroPn alcance, string? medio, bool enabled, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var m = medio?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!MediosPagoPn.Todos.Contains(m))
            return CobroPnErrores.MedioInvalido;
        if (alcance.Web && m == MediosPagoPn.Pin)
            return CobroPnErrores.PinEnWeb;
        if (alcance.OrganismoId is { } org && !await tarifas.OrganismoExisteAsync(org, ct).ConfigureAwait(false))
            return CobroPnErrores.OrganismoNoExiste;

        var anterior = await cobro.GuardarMedioAsync(alcance.OrganismoId, m, enabled, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, "persona_natural_payment_methods", AuditVocabulary.Operations.Update, actor, alcance.OrganismoId,
            anterior is null ? null : PersonaNaturalAuditoria.Json(("medio", m), ("habilitado", anterior)),
            PersonaNaturalAuditoria.Json(("medio", m), ("habilitado", enabled), ("organismoId", alcance.OrganismoId)), ct).ConfigureAwait(false);
        return null;
    }
}

/// <summary>
/// HU #14393 AC2/AC3/AC4 — mueve un switch. Encender exige monto mayor que cero para el concepto en el tipo y el ámbito (si no,
/// no guarda). Apagar no toca el monto, que vive en las tarifas. El <c>ot_admin</c> no mueve la gestión.
/// </summary>
public sealed class GuardarSwitchCobroPnHandler(IPersonaNaturalCobroRepository cobro, IPersonaNaturalTarifaRepository tarifas, IAdminAuditWriter audit)
{
    public async Task<string?> HandleAsync(
        AlcanceCobroPn alcance, Guid? tipoId, string? medio, string? concepto, bool enabled, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var m = medio?.Trim().ToUpperInvariant() ?? string.Empty;
        var c = concepto?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CobrosPn.Todos.Contains(c))
            return CobroPnErrores.CobroInvalido;
        // El permiso se decide antes que cualquier otra validación: un organismo no debe poder tantear la gestión.
        if (!CobrosPn.Mueve(alcance.EsSuperAdmin, c))
            return CobroPnErrores.GestionAjena;
        if (!MediosPagoPn.Todos.Contains(m))
            return CobroPnErrores.MedioInvalido;
        if (alcance.Web && m == MediosPagoPn.Pin)
            return CobroPnErrores.PinEnWeb;
        if (tipoId is not { } tipo || tipo == Guid.Empty)
            return CobroPnErrores.TipoObligatorio;
        if (!await tarifas.TipoExisteAsync(tipo, ct).ConfigureAwait(false))
            return CobroPnErrores.TipoNoExiste;
        if (alcance.OrganismoId is { } org && !await tarifas.OrganismoExisteAsync(org, ct).ConfigureAwait(false))
            return CobroPnErrores.OrganismoNoExiste;

        if (enabled)
        {
            var montos = await cobro.MontosAsync(alcance.OrganismoId, [tipo], ct).ConfigureAwait(false);
            if (!montos.TryGetValue((tipo, c), out var monto) || monto <= 0)
                return CobroPnErrores.SinMonto;
        }

        var anterior = await cobro.GuardarSwitchAsync(alcance.OrganismoId, tipo, m, c, enabled, actor, ct).ConfigureAwait(false);
        await PersonaNaturalAuditoria.EscribirAsync(
            audit, "persona_natural_charge_switches", AuditVocabulary.Operations.Update, actor, tipo,
            anterior is null ? null : PersonaNaturalAuditoria.Json(("medio", m), ("concepto", c), ("habilitado", anterior)),
            PersonaNaturalAuditoria.Json(
                ("medio", m), ("concepto", c), ("habilitado", enabled), ("tipoTramiteId", tipo), ("organismoId", alcance.OrganismoId)), ct).ConfigureAwait(false);
        return null;
    }
}
