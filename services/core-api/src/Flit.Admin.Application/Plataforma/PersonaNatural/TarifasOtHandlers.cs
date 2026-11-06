using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error estables de los precios del portal vistos desde el dashboard del organismo (HU #14388).</summary>
public static class TarifaOtErrores
{
    public const string ConceptoAjeno = "PORTAL_TARIFAS_CONCEPTO_AJENO";
    public const string OtroOrganismo = "PORTAL_TARIFAS_OTRO_ORGANISMO";
    public const string SinOrganismo = "PORTAL_TARIFAS_SIN_ORGANISMO";
    public const string OrganismoRequerido = "PORTAL_TARIFAS_ORGANISMO_REQUERIDO";
    public const string TipoObligatorio = "PORTAL_TARIFAS_TIPO_OBLIGATORIO";
}

/// <summary>
/// Con quién se trabaja: el organismo (siempre uno) y si quien llama es el SuperAdmin. El <c>ot_admin</c> trabaja con el
/// organismo de su token y solo con los conceptos de <see cref="ConceptosTarifaPn.EditablesPorOrganismo"/>; el SuperAdmin
/// escribe todos los conceptos del organismo que indique.
/// </summary>
public sealed record AlcanceTarifasOt(Guid OrganismoId, bool EsSuperAdmin);

/// <summary>Una versión de tarifa tal como la ve el organismo: los datos de siempre más si ese usuario puede cambiarla.</summary>
public sealed record TarifaOtView(
    Guid Id,
    string Concepto,
    Guid? TipoTramiteId,
    string? TipoTramiteCodigo,
    string? TipoTramiteNombre,
    Guid? OrganismoId,
    string? OrganismoCodigo,
    string? OrganismoNombre,
    long ValorCop,
    DateOnly VigenteDesde,
    DateOnly? VigenteHasta,
    string Estado,
    DateTimeOffset CreadaEn,
    Guid? CreadaPor,
    DateTimeOffset? ActualizadaEn,
    Guid? ActualizadaPor,
    string? CreadaPorNombre,
    bool Editable)
{
    /// <summary>
    /// <c>Editable</c> es falso en los valores generales (organismo nulo) y en los conceptos que el usuario no puede tocar.
    /// </summary>
    public static TarifaOtView De(TarifaPnView t, AlcanceTarifasOt alcance)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(alcance);
        var editable = t.OrganismoId == alcance.OrganismoId && (alcance.EsSuperAdmin || ConceptosTarifaPn.EditablePorOrganismo(t.Concepto));
        // Un valor general lo fija FLIT: al organismo no se le entrega el nombre ni el id de la persona (Ley 1581).
        var ocultarAutor = !alcance.EsSuperAdmin && t.OrganismoId is null;
        return new TarifaOtView(
            t.Id, t.Concepto, t.TipoTramiteId, t.TipoTramiteCodigo, t.TipoTramiteNombre, t.OrganismoId, t.OrganismoCodigo,
            t.OrganismoNombre, t.ValorCop, t.VigenteDesde, t.VigenteHasta, t.Estado, t.CreadaEn,
            ocultarAutor ? null : t.CreadaPor, t.ActualizadaEn, ocultarAutor ? null : t.ActualizadaPor,
            ocultarAutor ? AutorGeneral : t.CreadaPorNombre, editable);
    }

    /// <summary>Lo que el organismo ve como autor de un valor general.</summary>
    public const string AutorGeneral = "FLIT";
}

public sealed record TarifasOtResult(IReadOnlyList<TarifaOtView>? Tarifas, string? Error);

/// <summary>HU #14388 AC4 — las tarifas del organismo y los valores generales que le aplican (estos últimos, no editables).</summary>
public sealed class ListarTarifasOtHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public async Task<TarifasOtResult> HandleAsync(
        AlcanceTarifasOt alcance, bool soloVigentes, Guid? tipoTramiteId, string? concepto, DateOnly? fecha, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var filas = await tarifas.ListarDelOrganismoAsync(
            alcance.OrganismoId, tipoTramiteId, string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim().ToUpperInvariant(),
            soloVigentes, fecha, ct).ConfigureAwait(false);
        return new TarifasOtResult(filas.Select(t => TarifaOtView.De(t, alcance)).ToList(), null);
    }
}

/// <summary>HU #14388 — historial de un ámbito, solo si es del organismo o un valor general.</summary>
public sealed class HistorialTarifaOtHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public async Task<TarifasOtResult> HandleAsync(AlcanceTarifasOt alcance, Guid id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var tarifa = await tarifas.ObtenerAsync(id, ct).ConfigureAwait(false);
        if (tarifa is null)
            return new TarifasOtResult(null, TarifaPnErrores.NoEncontrada);
        // Un valor de OTRO organismo no se muestra: 403 (no 404) para no confundirlo con una tarifa inexistente.
        if (tarifa.OrganismoId is { } organismo && organismo != alcance.OrganismoId)
            return new TarifasOtResult(null, TarifaOtErrores.OtroOrganismo);

        var historial = await tarifas.HistorialAsync(id, ct).ConfigureAwait(false);
        return historial is null
            ? new TarifasOtResult(null, TarifaPnErrores.NoEncontrada)
            : new TarifasOtResult(historial.Select(t => TarifaOtView.De(t, alcance)).ToList(), null);
    }
}

/// <summary>HU #14388 — tipos del portal para el formulario de precios.</summary>
public sealed class TiposTarifaOtHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public Task<IReadOnlyList<TipoTarifaPnView>> HandleAsync(CancellationToken ct = default) => tarifas.ListarTiposDelPortalAsync(ct);
}

/// <summary>
/// HU #14388 AC1/AC2 — registra una versión para el organismo del alcance. Servicio FLIT y envío son del SuperAdmin: el
/// <c>ot_admin</c> recibe <see cref="TarifaOtErrores.ConceptoAjeno"/> y no cambia nada. Reutiliza el caso de uso de
/// Plataforma (validaciones, versionado transaccional y auditoría), así que el mismo día reemplaza (HU #14389).
/// </summary>
public sealed class VersionarTarifasOtHandler(VersionarTarifasPnHandler versionar)
{
    public async Task<TarifasOtResult> HandleAsync(
        AlcanceTarifasOt alcance, Guid? tipoTramiteId, IReadOnlyList<TarifaPnValor>? valores, DateOnly? vigenteDesde, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        if (!alcance.EsSuperAdmin)
        {
            // Antes de cualquier otra validación y antes de tocar la base: un concepto ajeno no cambia nada.
            foreach (var v in valores ?? [])
            {
                var concepto = v.Concepto?.Trim().ToUpperInvariant() ?? string.Empty;
                if (ConceptosTarifaPn.Todos.Contains(concepto) && !ConceptosTarifaPn.EditablePorOrganismo(concepto))
                    return new TarifasOtResult(null, TarifaOtErrores.ConceptoAjeno);
            }

            if (tipoTramiteId is null)
                return new TarifasOtResult(null, TarifaOtErrores.TipoObligatorio);
        }

        var resultado = await versionar.HandleAsync(
            new VersionarTarifasPnCommand(tipoTramiteId, alcance.OrganismoId, valores, vigenteDesde, actor), ct).ConfigureAwait(false);
        return resultado.Error is not null
            ? new TarifasOtResult(null, resultado.Error)
            : new TarifasOtResult(resultado.Tarifas!.Select(t => TarifaOtView.De(t, alcance)).ToList(), null);
    }
}
