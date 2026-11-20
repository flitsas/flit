using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Lo que comparten el individual, el masivo y el consumidor (Punto 4): el tipo de trámite del borrador (decisión 4.3) y el código del
/// organismo de matrícula para el rpc (diseño §5.4).
/// </summary>
internal static class ApoyoDerivacion
{
    public const string CodigoTipoRequerido = "TIPO_TRAMITE_REQUERIDO";
    public const string CodigoTipoInvalido = "TIPO_TRAMITE_INVALIDO";
    public const string CampoTipo = "tipo_tramite_codigo";

    /// <summary>
    /// El tipo del borrador: el del lote (Con análisis) o el del cuerpo (Simple), validado contra el catálogo de la etapa
    /// (<c>GET /tipos-tramite</c>). 400 <see cref="CodigoTipoRequerido"/> sin tipo, 400 <see cref="CodigoTipoInvalido"/> fuera del
    /// catálogo, 503 CATALOGO_NO_DISPONIBLE si el catálogo no responde.
    /// </summary>
    public static async Task<string> TipoAsync(ICatalogoTiposTramite catalogo, ModoDiagnostico modo, string? delLote, string? delCuerpo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var tipo = TipoTramiteDerivacion.Resolver(modo, delLote, delCuerpo)
            ?? throw new ReglaException(CodigoTipoRequerido, "Elige el tipo de trámite.", StatusCodes.Status400BadRequest) { Campo = CampoTipo };
        if (modo == ModoDiagnostico.ConAnalisis)
            return tipo;

        IReadOnlyList<TipoTramite> tipos;
        try
        {
            tipos = await catalogo.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (CatalogoTiposTramiteNoDisponibleException ex)
        {
            throw ReglaException.CatalogoNoDisponible(ex);
        }

        return tipos.Any(t => string.Equals(t.Codigo, tipo, StringComparison.Ordinal))
            ? tipo
            : throw new ReglaException(CodigoTipoInvalido, "El tipo de trámite no está disponible.", StatusCodes.Status400BadRequest) { Campo = CampoTipo };
    }

    /// <summary>Código de la réplica de organismos si el nombre del RUNT casa con uno solo; null si no hay o es ambiguo.</summary>
    public static async Task<string?> OrganismoCodigoAsync(DiagnosticoDb db, string? nombre, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var norm = NombreNormalizado.Normalizar(nombre);
        if (norm.Length == 0)
            return null;
        var codigos = await db.Set<Organismo>().AsNoTracking().Where(o => o.NombreNorm == norm).Select(o => o.Codigo).Take(2).ToListAsync(ct).ConfigureAwait(false);
        return codigos.Count == 1 ? codigos[0] : null;
    }
}
