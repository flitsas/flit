using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error estables de la administración de tarifas del portal.</summary>
public static class TarifaPnErrores
{
    public const string ConceptoInvalido = "concepto_invalido";
    public const string ValoresVacios = "valores_vacios";
    public const string ConceptoRepetido = "concepto_repetido";
    public const string ValorInvalido = "valor_invalido";
    public const string TipoObligatorio = "tipo_obligatorio";
    public const string TipoNoExiste = "tipo_no_existe";
    public const string OrganismoNoExiste = "organismo_no_existe";
    public const string FechaInicioInvalida = "fecha_inicio_invalida";
    public const string FechaInicioPasada = "fecha_inicio_pasada";
    public const string Solapada = "tarifa_solapada";
    public const string NoEncontrada = "tarifa_no_encontrada";
}

/// <summary>Una versión nueva: los valores por concepto de un ámbito (tipo y organismo; ambos opcionales) desde una fecha.</summary>
public sealed record VersionarTarifasPnCommand(
    Guid? TipoTramiteId, Guid? OrganismoId, IReadOnlyList<TarifaPnValor>? Valores, DateOnly? VigenteDesde, Guid Actor);

public sealed record VersionarTarifasPnResult(IReadOnlyList<TarifaPnView>? Tarifas, string? Error);

/// <summary>HU #14071 — listado de tarifas del portal (todas las versiones o solo las vigentes a una fecha).</summary>
public sealed class ListarTarifasPnHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public Task<IReadOnlyList<TarifaPnView>> HandleAsync(TarifaPnFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        return tarifas.ListarAsync(
            filtro with { Concepto = string.IsNullOrWhiteSpace(filtro.Concepto) ? null : filtro.Concepto.Trim().ToUpperInvariant() }, ct);
    }
}

/// <summary>HU #14071 AC3 — historial de un ámbito: las versiones anteriores se ven, no se editan.</summary>
public sealed class HistorialTarifaPnHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public Task<IReadOnlyList<TarifaPnView>?> HandleAsync(Guid id, CancellationToken ct = default) => tarifas.HistorialAsync(id, ct);
}

/// <summary>HU #14071 AC2/AC5 — la «tarifa vigente» de un tipo en un organismo a una fecha, con la precedencia de la cotización.</summary>
public sealed class TarifaVigentePnHandler(IPersonaNaturalTarifaRepository tarifas)
{
    public Task<IReadOnlyList<TarifaPnView>> HandleAsync(Guid tipoId, Guid organismoId, DateOnly? fecha, CancellationToken ct = default) =>
        tarifas.VigentesDelTipoEnOrganismoAsync(tipoId, organismoId, fecha ?? tarifas.HoyColombia(), ct);
}

/// <summary>
/// HU #14071 AC1/AC3/AC6 — registra una versión nueva de tarifa. Valida (conceptos, pesos enteros ≥ 0, inicio no pasado),
/// versiona en una transacción y audita cada concepto con el valor anterior y el nuevo.
/// </summary>
public sealed class VersionarTarifasPnHandler(IPersonaNaturalTarifaRepository tarifas, IAdminAuditWriter audit)
{
    /// <summary>Tope de cordura de un valor (mil millones de pesos): un error de dígitos no pasa en silencio.</summary>
    public const long ValorMaximoCop = 1_000_000_000;

    public async Task<VersionarTarifasPnResult> HandleAsync(VersionarTarifasPnCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Valores is not { Count: > 0 } valores)
            return Error(TarifaPnErrores.ValoresVacios);
        var normalizados = new List<TarifaPnValor>(valores.Count);
        foreach (var v in valores)
        {
            var concepto = v.Concepto?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!ConceptosTarifaPn.Todos.Contains(concepto))
                return Error(TarifaPnErrores.ConceptoInvalido);
            if (normalizados.Any(n => n.Concepto == concepto))
                return Error(TarifaPnErrores.ConceptoRepetido);
            if (v.ValorCop < 0 || v.ValorCop > ValorMaximoCop)
                return Error(TarifaPnErrores.ValorInvalido);
            if (command.TipoTramiteId is null && ConceptosTarifaPn.ExigeTipo(concepto))
                return Error(TarifaPnErrores.TipoObligatorio);
            normalizados.Add(new TarifaPnValor(concepto, v.ValorCop));
        }

        if (command.VigenteDesde is not { } desde)
            return Error(TarifaPnErrores.FechaInicioInvalida);
        if (desde < tarifas.HoyColombia())
            return Error(TarifaPnErrores.FechaInicioPasada);

        if (command.TipoTramiteId is { } tipo && !await tarifas.TipoExisteAsync(tipo, ct).ConfigureAwait(false))
            return Error(TarifaPnErrores.TipoNoExiste);
        if (command.OrganismoId is { } organismo && !await tarifas.OrganismoExisteAsync(organismo, ct).ConfigureAwait(false))
            return Error(TarifaPnErrores.OrganismoNoExiste);

        TarifaPnVersionadaResult resultado;
        try
        {
            resultado = await tarifas.VersionarAsync(command.TipoTramiteId, command.OrganismoId, normalizados, desde, command.Actor, ct).ConfigureAwait(false);
        }
        catch (TarifaPnSolapadaException)
        {
            return Error(TarifaPnErrores.Solapada);
        }

        foreach (var nueva in resultado.Nuevas)
        {
            var anterior = resultado.Anteriores.FirstOrDefault(a => a.Concepto == nueva.Concepto);
            await PersonaNaturalAuditoria.EscribirAsync(
                audit, "persona_natural_tariffs", AuditVocabulary.Operations.Create, command.Actor, nueva.Id,
                anterior is null ? null : Resumen(anterior), Resumen(nueva), ct).ConfigureAwait(false);
        }

        return new VersionarTarifasPnResult(resultado.Nuevas, null);
    }

    private static string Resumen(TarifaPnView t) => PersonaNaturalAuditoria.Json(
        ("concepto", t.Concepto), ("tipoTramiteId", t.TipoTramiteId), ("organismoId", t.OrganismoId), ("valorCop", t.ValorCop),
        ("vigenteDesde", t.VigenteDesde), ("vigenteHasta", t.VigenteHasta));

    private static VersionarTarifasPnResult Error(string codigo) => new(null, codigo);
}
