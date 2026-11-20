using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.Services;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>Un tipo publicado de FLIT 2 con su elegibilidad para persona natural ya calculada.</summary>
public sealed record TipoPersonaNatural(
    Guid Id,
    string Codigo,
    string Nombre,
    string Familia,
    int Version,
    ProcedureTypeGateProfile Perfil,
    ResultadoElegibilidad Elegibilidad,
    bool WizardHabilitado = true,
    bool ActivoEnFlit = true);

/// <summary>
/// HU #14079 (Épica #14021) — lee los tipos publicados y le aplica <see cref="ElegibilidadPersonaNatural"/>. Solo lectura
/// sobre tablas existentes; ningún tipo se nombra aquí.
/// </summary>
public sealed class PersonaNaturalTiposReader(FlitDbContext db)
{
    /// <summary>Todos los tipos activos y publicados, elegibles o no (los no elegibles llevan su motivo).</summary>
    public Task<IReadOnlyList<TipoPersonaNatural>> ListarAsync(CancellationToken ct, string? codigo = null) =>
        LeerAsync(soloHabilitadosEnFlit2: false, codigo, ct);

    /// <summary>
    /// Solo para la pantalla de administración: los tipos HABILITADOS en FLIT 2 (activos, publicados y con el wizard
    /// encendido). Un tipo que deja de cumplirlo desaparece de la tabla (HU 2, decisión D4 del PO); su fila en la lista blanca
    /// no estorba porque el portal ya no lo ofrece sin el wizard. El portal NO usa este método.
    /// </summary>
    public Task<IReadOnlyList<TipoPersonaNatural>> ListarParaAdministracionAsync(CancellationToken ct) =>
        LeerAsync(soloHabilitadosEnFlit2: true, null, ct);

    private async Task<IReadOnlyList<TipoPersonaNatural>> LeerAsync(bool soloHabilitadosEnFlit2, string? codigo, CancellationToken ct)
    {
        var tipos = await db.ProcedureTypes.AsNoTracking()
            .Where(t => t.IsActive && t.PublicationStatus == PublicationStatus.Published
                && (!soloHabilitadosEnFlit2 || t.WizardEnabled)
                && (codigo == null || t.Code == codigo))
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Code, t.Name, t.Family, t.Version, t.GateProfile, t.WizardEnabled, t.IsActive, t.PublicationStatus })
            .ToListAsync(ct).ConfigureAwait(false);

        return tipos.Select(t =>
        {
            var perfil = ProcedureTypeGateProfile.FromJson(t.GateProfile);
            var eleg = ElegibilidadPersonaNatural.Evaluar(new PerfilTipoParaElegibilidad(
                t.Code, t.Family, Publicado: t.PublicationStatus == PublicationStatus.Published, t.IsActive, t.WizardEnabled, perfil, []));
            return new TipoPersonaNatural(
                t.Id, t.Code, t.Name, t.Family, t.Version, perfil, eleg, t.WizardEnabled,
                ActivoEnFlit: t.IsActive && t.PublicationStatus == PublicationStatus.Published);
        }).ToList();
    }
}
