using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Unicidad de partes (vendedor ≠ comprador) y resolución de la validación vigente por documento
/// (paridad <c>traspaso-partes.ts</c> de Johan). Puro.
/// </summary>
public static class TraspasoPartes
{
    /// <summary>
    /// Ranking de estados de validación: prioriza la validación más "fuerte" al resolver la vigente.
    /// </summary>
    private static readonly Dictionary<string, int> EstadoRank =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["aprobado"] = 50,
            ["en_proceso"] = 40,
            ["enviado"] = 30,
            ["rechazado"] = 20,
            ["expirado"] = 10,
        };

    /// <summary>Compara vendedor y comprador por documento y email normalizados (paridad <c>partesTraspasoDuplicadas</c>).</summary>
    public static PartesDuplicadas DetectarDuplicadas(ParteDatos vendedor, ParteDatos comprador)
    {
        ArgumentNullException.ThrowIfNull(vendedor);
        ArgumentNullException.ThrowIfNull(comprador);

        var vd = TramiteDocumento.Normalizar(vendedor.Documento);
        var cd = TramiteDocumento.Normalizar(comprador.Documento);
        var ve = TramiteDocumento.NormalizarEmail(vendedor.Email);
        var ce = TramiteDocumento.NormalizarEmail(comprador.Email);

        return new PartesDuplicadas(
            MismoDocumento: vd.Length > 0 && cd.Length > 0 && vd == cd,
            MismoEmail: ve.Length > 0 && ce.Length > 0 && ve == ce);
    }

    /// <summary>
    /// Mensaje accionable de duplicidad, o <c>null</c> si las partes son distintas (paridad
    /// <c>mensajePartesTraspasoDuplicadas</c>). HU #11019 — el CORREO COMPARTIDO ya no bloquea: es
    /// legítimo que vendedor y comprador usen el mismo buzón (la empresa que gestiona por su contacto,
    /// un familiar que recibe por ambos). El mismo DOCUMENTO sí sigue bloqueando: ahí serían la misma
    /// persona. <see cref="PartesDuplicadas.MismoEmail"/> se conserva como dato para quien lo consulte.
    /// </summary>
    public static string? MensajeDuplicadas(PartesDuplicadas dup)
    {
        ArgumentNullException.ThrowIfNull(dup);
        return dup.MismoDocumento
            ? "El vendedor y el comprador no pueden tener el mismo número de documento."
            : null;
    }

    /// <summary>
    /// ADR-0053 §4.4 nivel 1 (Múltiple Propietario) — ¿hay dos o más actores de un MISMO lado
    /// (vendedores entre sí, o compradores entre sí) que comparten documento? BLOQUEADA SIEMPRE, sin
    /// condición de conteo: "nadie es copropietario de sí mismo". Compara cada par de la lista por
    /// documento normalizado (mismo criterio de <see cref="TramiteDocumento.Normalizar"/> que
    /// <see cref="DetectarDuplicadas"/>); documentos vacíos no cuentan como coincidencia. Método NUEVO
    /// — <see cref="DetectarDuplicadas"/> no se toca, para no arriesgar el caso 1-a-1 vigente.
    /// </summary>
    public static bool DetectarDuplicadosIntraLado(IReadOnlyList<ParteDatos> lado)
    {
        ArgumentNullException.ThrowIfNull(lado);

        var documentos = lado
            .Select(p => TramiteDocumento.Normalizar(p.Documento))
            .Where(d => d.Length > 0)
            .ToList();

        return documentos.Count != documentos.Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>
    /// ¿La parte necesita (re)envío de enlace biométrico? No re-enviar si está aprobado/en curso/enviado
    /// (paridad <c>parteTraspasoRequiereReenvio</c>).
    /// </summary>
    public static bool RequiereReenvio(ValidacionRow? val)
    {
        if (val is null)
            return true;
        return !(string.Equals(val.Estado, "aprobado", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(val.Estado, "en_proceso", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(val.Estado, "enviado", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resuelve la validación VIGENTE por documento (no por índice/recencia): con documento conocido
    /// SOLO devuelve filas de ese documento (o <c>null</c>), eligiendo por ranking de estado y
    /// desempatando por id. Filas legacy sin documento caen al match por rol. Paridad
    /// <c>resolverValidacionTraspasoParte</c>.
    /// </summary>
    public static ValidacionRow? ResolverValidacionParte(
        IReadOnlyList<ValidacionRow> validaciones,
        string? documento,
        string? parte)
    {
        ArgumentNullException.ThrowIfNull(validaciones);

        var doc = TramiteDocumento.Normalizar(documento);
        var rol = parte ?? string.Empty;

        var matches = validaciones.Where(v =>
        {
            var vDoc = TramiteDocumento.Normalizar(v.Documento);
            if (doc.Length > 0)
            {
                if (vDoc.Length > 0)
                    return vDoc == doc && (rol.Length == 0 || string.IsNullOrEmpty(v.Parte) || v.Parte == rol);
                return rol.Length > 0 && v.Parte == rol;
            }
            return rol.Length > 0 && v.Parte == rol;
        }).ToList();

        return PickByRank(matches);
    }

    /// <summary>
    /// Devuelve la validación vigente del titular por documento (la de mayor estado, desempate por id).
    /// Con documento conocido devuelve SOLO filas de ese documento (nunca la de otra persona).
    /// Paridad <c>resolverValidacionVigentePorDocumento</c>.
    /// </summary>
    public static ValidacionRow? ResolverVigentePorDocumento(
        IReadOnlyList<ValidacionRow>? rows,
        string? documento)
    {
        if (rows is null || rows.Count == 0)
            return null;

        var doc = TramiteDocumento.Normalizar(documento);
        var candidatas = doc.Length > 0
            ? rows.Where(r => TramiteDocumento.Normalizar(r.Documento) == doc).ToList()
            : rows.ToList();

        return PickByRank(candidatas);
    }

    private static ValidacionRow? PickByRank(List<ValidacionRow> rows)
    {
        if (rows.Count == 0)
            return null;

        return rows.Aggregate((best, cur) =>
        {
            int br = Rank(best.Estado);
            int cr = Rank(cur.Estado);
            if (cr != br)
                return cr > br ? cur : best;
            return cur.Id > best.Id ? cur : best;
        });
    }

    private static int Rank(string? estado) =>
        estado is not null && EstadoRank.TryGetValue(estado, out var r) ? r : 0;
}
