using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Modules.Quipux.Domain.Mapeo;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Modules.Quipux.Application.UseCases.MapeoTipoTramite;

/// <summary>
/// Parametrización Quipux de un tipo de trámite, tal como la edita el configurador.
/// <para>Se persiste en <c>procedure_types.external_refs -&gt; 'quipux'</c>, que es el punto central
/// de configuración del tipo: la misma fila que lleva su recorrido y sus documentos guarda también
/// sus equivalencias con los sistemas externos.</para>
/// </summary>
/// <param name="Familia">
/// Familia QUIPUX (<c>MATRICULA</c> | <c>TRASPASO</c> | <c>OTROS</c>), que es la taxonomía de la
/// secretaría y no tiene por qué coincidir con la de FLIT.
/// </param>
/// <param name="TipoTramite">Código de trámite que asigna la secretaría (16 traspaso, 13 matrícula).</param>
/// <param name="TipoRequisito">Código de requisito (51 en todos los flujos conocidos).</param>
/// <param name="Prefijo">Prefijo del nombre del documento radicado (TR, MI, TRU, MIL).</param>
/// <param name="CampoPlaca">Field value del que sale la placa, o null si el trámite no la usa.</param>
/// <param name="CampoVin">Field value del que sale el VIN, o null si el trámite no lo usa.</param>
/// <param name="MaxLongitudEmpresa">Tope de la parte de empresa del nombre (35 traspaso, 25 matrícula).</param>
public sealed record MapeoQuipuxDto(
    string Familia,
    int TipoTramite,
    int TipoRequisito,
    string Prefijo,
    string? CampoPlaca,
    string? CampoVin,
    int MaxLongitudEmpresa);

/// <summary>
/// Lee la parametrización Quipux del tipo. Devuelve <c>null</c> en <c>Mapeo</c> cuando el tipo no
/// tiene bloque: es un estado legítimo —ese trámite no se radica— y no un error.
/// </summary>
public sealed class ObtenerMapeoQuipuxHandler(IProcedureTypeRepository repository)
{
    public async Task<(MapeoQuipuxDto? Mapeo, string? Error)> HandleAsync(
        Guid procedureTypeId,
        CancellationToken ct = default)
    {
        var tipo = await repository.GetByIdAsync(procedureTypeId, ct);
        if (tipo is null)
            return (null, "not_found");

        var mapa = QuipuxTipoTramiteMap.Parse(tipo.ExternalRefs);
        if (mapa is null)
            return (null, null);

        return (new MapeoQuipuxDto(
            mapa.Familia, mapa.TipoTramite, mapa.TipoRequisito, mapa.Prefijo,
            mapa.CampoPlaca, mapa.CampoVin, mapa.MaxLongitudEmpresa), null);
    }
}

/// <summary>
/// Guarda o retira la parametrización Quipux del tipo.
///
/// <para>La validación es la MISMA que aplica el worker al radicar: se compone el bloque y se pasa
/// por <see cref="QuipuxTipoTramiteMap.Parse"/>. Si no sobrevive, no se guarda. Guardar un bloque a
/// medias no radicaría mal —la ausencia de bloque utilizable es el gate de elegibilidad— pero dejaría
/// al administrador creyendo que configuró algo que el worker descarta en silencio.</para>
///
/// <para>La variante (<c>es_leasing</c>, <c>es_unilateral</c>) NO se edita aquí a propósito: existía
/// para cuando el leasing y el unilateral eran una casilla dentro del trámite canónico. Ahora son
/// tipos propios del catálogo con su propio bloque, así que la variante es una forma heredada que no
/// hay motivo para seguir creando. Los bloques que ya la tienen se conservan intactos.</para>
/// </summary>
public sealed class GuardarMapeoQuipuxHandler(IProcedureTypeRepository repository)
{
    public const string NoUtilizable = "mapeo_no_utilizable";

    public async Task<(MapeoQuipuxDto? Mapeo, string? Error)> HandleAsync(
        Guid procedureTypeId,
        MapeoQuipuxDto? mapeo,
        CancellationToken ct = default)
    {
        var tipo = await repository.GetByIdAsync(procedureTypeId, ct);
        if (tipo is null)
            return (null, "not_found");

        var raiz = JsonNode.Parse(
            string.IsNullOrWhiteSpace(tipo.ExternalRefs) ? "{}" : tipo.ExternalRefs) as JsonObject
            ?? [];

        if (mapeo is null)
        {
            // Retirar el bloque deja el tipo NO elegible, que es como nace todo el catálogo. Es la
            // forma de decir «este trámite no se radica en la secretaría».
            raiz.Remove("quipux");
        }
        else
        {
            var bloque = Componer(mapeo, raiz["quipux"] as JsonObject);
            raiz["quipux"] = bloque;

            // Mismo camino que el worker: si el bloque no sobrevive al Parse, el tipo quedaría
            // silenciosamente sin radicar pese a aparecer configurado.
            if (QuipuxTipoTramiteMap.Parse(raiz.ToJsonString()) is null)
                return (null, NoUtilizable);
        }

        tipo.ExternalRefs = raiz.ToJsonString();
        tipo.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(tipo, ct);
        await repository.SaveChangesAsync(ct);

        return (mapeo, null);
    }

    /// <summary>
    /// Compone el bloque conservando la <c>variante</c> que ya tuviera: es configuración heredada
    /// que el formulario no ofrece, y sobrescribirla en silencio cambiaría cómo se radican los
    /// trámites del tipo canónico.
    /// </summary>
    private static JsonObject Componer(MapeoQuipuxDto m, JsonObject? previo)
    {
        var bloque = new JsonObject
        {
            ["familia"] = m.Familia?.Trim().ToUpperInvariant(),
            ["tipoTramite"] = m.TipoTramite,
            ["tipoRequisito"] = m.TipoRequisito,
            ["prefijo"] = m.Prefijo?.Trim().ToUpperInvariant(),
            ["campoPlaca"] = string.IsNullOrWhiteSpace(m.CampoPlaca) ? null : m.CampoPlaca.Trim(),
            ["campoVin"] = string.IsNullOrWhiteSpace(m.CampoVin) ? null : m.CampoVin.Trim(),
            ["maxLongitudEmpresa"] = m.MaxLongitudEmpresa,
        };

        if (previo?["variante"] is JsonNode variante)
            bloque["variante"] = JsonNode.Parse(variante.ToJsonString());

        return bloque;
    }
}
