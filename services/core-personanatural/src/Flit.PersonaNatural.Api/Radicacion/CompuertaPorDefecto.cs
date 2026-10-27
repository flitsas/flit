using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// Compuerta por defecto de la radicación (HU #14049): mientras la HU C3 no la reemplace, exige que cada parte que la
/// definición congelada marca con identidad (<c>biometria</c>) tenga su validación APROBADA en ESTA solicitud. Mira solo las
/// personas de la solicitud (nunca busca la cédula en otros trámites: la decisión del PO es que no hay reutilización) y no
/// devuelve datos personales, solo rol, posición y estado. La implementación completa de C3 se registra antes y esta no entra
/// (<c>TryAdd</c>): <see cref="ICompuertaDeRadicacion"/> es el único punto de contacto.
/// </summary>
public sealed class CompuertaDePartesAprobadas(PersonaNaturalDb db) : ICompuertaDeRadicacion
{
    /// <summary>Estados que cuentan como identidad validada (el portal no usa vigencia en días).</summary>
    public static readonly IReadOnlySet<string> EstadosValidos = new HashSet<string>(StringComparer.Ordinal) { "aprobada", "validada" };

    public const string EstadoRechazada = "rechazada";

    public async Task<ResultadoCompuerta> EvaluarAsync(Guid solicitudId, CancellationToken ct)
    {
        var snapshot = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == solicitudId).Select(s => s.DefinicionSnapshot).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var def = snapshot is null ? null : DefinicionesService.Leer(snapshot);
        if (def is null)
            return ResultadoCompuerta.Bloqueada(MotivosCompuerta.IdentidadPendiente, []);

        var obligadas = def.Actores.Where(a => a.Biometria).Select(a => a.Rol).ToHashSet(StringComparer.Ordinal);
        if (obligadas.Count == 0)
            return ResultadoCompuerta.Aprobada;

        var personas = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == solicitudId && obligadas.Contains(p.Parte))
            .Select(p => new { p.Parte, p.Ordinal, p.ValidacionEstado }).ToListAsync(ct).ConfigureAwait(false);

        var faltantes = new List<ParteFaltante>();
        foreach (var rol in def.Actores.Where(a => a.Biometria))
        {
            var delRol = personas.Where(p => p.Parte == rol.Rol).ToList();
            if (delRol.Count < rol.Min)
                faltantes.Add(new ParteFaltante(rol.Rol, 1, "sin_solicitar"));
            faltantes.AddRange(delRol.Where(p => !EstadosValidos.Contains(p.ValidacionEstado)).Select(p => new ParteFaltante(p.Parte, p.Ordinal, p.ValidacionEstado)));
        }

        if (faltantes.Count == 0)
            return ResultadoCompuerta.Aprobada;

        var motivo = faltantes.Any(f => f.Estado == EstadoRechazada) ? MotivosCompuerta.IdentidadRechazada : MotivosCompuerta.IdentidadPendiente;
        return ResultadoCompuerta.Bloqueada(motivo, faltantes);
    }
}
