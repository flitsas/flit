using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070 D8) — minimización de <c>consolidado_export_audit.filter_summary</c> (Ley 1581).
/// Lo que se guarda es la FORMA de la selección, nunca sus valores personales:
/// <list type="bullet">
///   <item>Condiciones del catálogo (listas pegadas de placa/VIN/radicado, compañía del Super Admin…):
///   <c>{campo, operador, cantidad}</c>.</item>
///   <item>Texto libre (búsqueda, placa, VIN, vendedor, comprador, gestor, organismo por nombre):
///   <c>{presente, longitud}</c>.</item>
///   <item>Valores de catálogo (estados, familia, tipo, atajo, orden), fechas y banderas: tal cual.</item>
///   <item>Ids sueltos y excluidos: solo el conteo.</item>
/// </list>
/// El filtro de la bandeja del OT (#13390) vive en Infrastructure y se resume con <see cref="ILoteFiltroResumible"/>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new TramitesLoteFiltro(criterios), excluidos));
/// // {"modo":"filtro","excluidos":{"cantidad":2},"origenFiltro":"tramites","filtro":{"placa":{"presente":true,"longitud":3}}}
/// </code>
/// </remarks>
public static class ConsolidadoLoteAuditoria
{
    /// <summary>Resume la selección para <c>filter_summary</c>. <paramref name="otTransitOfficeId"/> = organismo del lote OT (A5.5).</summary>
    public static string ResumirSeleccion(LoteSeleccion seleccion, Guid? otTransitOfficeId = null)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        var raiz = new JsonObject();

        switch (seleccion)
        {
            case SeleccionPorIds porIds:
                raiz["modo"] = ConsolidadoExportSelectionMode.Ids;
                raiz["ids"] = Cantidad(porIds.Ids?.Count ?? 0);
                break;
            case SeleccionPorFiltro porFiltro:
                raiz["modo"] = ConsolidadoExportSelectionMode.Filtro;
                raiz["excluidos"] = Cantidad(porFiltro.Excluidos?.Count ?? 0);
                var resumen = new LoteFiltroResumen();
                raiz["origenFiltro"] = ResumirFiltro(porFiltro.Filtro, resumen);
                raiz["filtro"] = resumen.Construir();
                break;
            default:
                raiz["modo"] = "desconocido";
                break;
        }

        if (otTransitOfficeId is Guid organismo)
            raiz["organismo"] = organismo.ToString("D");

        return raiz.ToJsonString(JsonOpciones);
    }

    /// <summary>Ids sueltos de la selección (modo ids), o <c>null</c>.</summary>
    public static int? ContarIds(LoteSeleccion seleccion) =>
        seleccion is SeleccionPorIds porIds ? porIds.Ids?.Count ?? 0 : null;

    /// <summary>Excluidos de la selección (modo filtro), o <c>null</c>.</summary>
    public static int? ContarExcluidos(LoteSeleccion seleccion) =>
        seleccion is SeleccionPorFiltro porFiltro ? porFiltro.Excluidos?.Count ?? 0 : null;

    private static string ResumirFiltro(LoteFiltro? filtro, LoteFiltroResumen resumen)
    {
        switch (filtro)
        {
            case TramitesLoteFiltro tramites:
                ResumirTramites(tramites.Criterios, resumen);
                return ConsolidadoExportOrigin.Tramites;
            case ILoteFiltroResumible resumible:
                resumible.Resumir(resumen);
                return resumible.OrigenFiltro;
            default:
                // Falla en privado: un filtro sin resumen declarado no deja ningún valor en la auditoría.
                return "no_resumible";
        }
    }

    private static void ResumirTramites(ProcedureInstanceListRequest c, LoteFiltroResumen r)
    {
        ArgumentNullException.ThrowIfNull(c);
        r.TextoLibre("busqueda", c.Busqueda)
            .TextoLibre("vin", c.Vin)
            .TextoLibre("placa", c.Placa)
            .TextoLibre("vendedor", c.Vendedor)
            .TextoLibre("comprador", c.Comprador)
            .TextoLibre("gestor", c.Gestor)
            .TextoLibre("organismoTransito", c.OrganismoTransito)
            .Literales("estados", c.Estados)
            .Literal("modalidad", c.Modalidad)
            .Literal("tipoCodigo", c.TipoCodigo)
            .Literal("busquedaRapida", c.BusquedaRapida)
            .Booleano("firmado", c.Firmado)
            .Booleano("prioritario", c.Prioritario)
            .Fecha("createdFrom", c.CreatedFrom)
            .Fecha("createdTo", c.CreatedTo)
            .Fecha("updatedFrom", c.UpdatedFrom)
            .Fecha("updatedTo", c.UpdatedTo)
            .Condiciones(c.Condiciones)
            .Literal("sortBy", c.SortBy)
            .Booleano("sortDescending", c.SortDescending);
    }

    private static JsonObject Cantidad(int n) => new() { ["cantidad"] = n };

    private static readonly JsonSerializerOptions JsonOpciones = new() { WriteIndented = false };
}

/// <summary>
/// Filtro de un origen que no vive en Application (p. ej. la bandeja del OT, #13390) y declara su propio resumen
/// minimizado con <see cref="LoteFiltroResumen"/>.
/// </summary>
public interface ILoteFiltroResumible
{
    /// <summary>Origen del filtro (<c>origenFiltro</c> en el resumen).</summary>
    string OrigenFiltro { get; }

    void Resumir(LoteFiltroResumen resumen);
}

/// <summary>
/// Constructor del resumen minimizado de un filtro. Solo agrega claves con valor. Los métodos deciden QUÉ se guarda:
/// <see cref="TextoLibre"/> y <see cref="Condiciones"/> nunca guardan el valor.
/// </summary>
public sealed class LoteFiltroResumen
{
    /// <summary>Tope de un literal de catálogo: un valor fuera de catálogo no se copia entero.</summary>
    public const int MaxLiteral = 64;

    private readonly JsonObject _filtro = new();

    /// <summary>Valor de catálogo (estado, familia, tipo, orden) tal cual, truncado a <see cref="MaxLiteral"/>.</summary>
    public LoteFiltroResumen Literal(string campo, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            _filtro[campo] = Truncar(valor.Trim());
        return this;
    }

    /// <summary>Lista de valores de catálogo (p. ej. estados).</summary>
    public LoteFiltroResumen Literales(string campo, IReadOnlyList<string>? valores)
    {
        if (valores is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var v in valores.Where(v => !string.IsNullOrWhiteSpace(v)))
                arr.Add(Truncar(v.Trim()));
            _filtro[campo] = arr;
        }

        return this;
    }

    public LoteFiltroResumen Booleano(string campo, bool? valor)
    {
        if (valor is bool b)
            _filtro[campo] = b;
        return this;
    }

    /// <summary>Identificador de catálogo (p. ej. tipo de trámite). No usar con ids de personas.</summary>
    public LoteFiltroResumen Identificador(string campo, Guid? valor)
    {
        if (valor is Guid g && g != Guid.Empty)
            _filtro[campo] = g.ToString("D");
        return this;
    }

    public LoteFiltroResumen Fecha(string campo, DateTimeOffset? valor)
    {
        if (valor is DateTimeOffset f)
            _filtro[campo] = f.ToString("O", CultureInfo.InvariantCulture);
        return this;
    }

    /// <summary>Texto libre: SOLO <c>{presente, longitud}</c>, nunca el texto (puede llevar placa, nombre o documento).</summary>
    public LoteFiltroResumen TextoLibre(string campo, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            _filtro[campo] = new JsonObject { ["presente"] = true, ["longitud"] = valor.Trim().Length };
        return this;
    }

    /// <summary>Condiciones del catálogo: <c>{campo, operador, cantidad}</c>, nunca los valores (listas pegadas).</summary>
    public LoteFiltroResumen Condiciones(IReadOnlyList<QueryCondition>? condiciones)
    {
        if (condiciones is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var c in condiciones)
            {
                arr.Add(new JsonObject
                {
                    ["campo"] = Truncar(c.FieldId ?? string.Empty),
                    ["operador"] = Truncar(c.Operator ?? string.Empty),
                    ["cantidad"] = c.Values?.Count ?? 0,
                });
            }

            _filtro["condiciones"] = arr;
        }

        return this;
    }

    internal JsonObject Construir() => _filtro;

    private static string Truncar(string v) => v.Length <= MaxLiteral ? v : v[..MaxLiteral];
}
