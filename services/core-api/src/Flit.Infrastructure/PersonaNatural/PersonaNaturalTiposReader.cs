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
/// HU #14079 (Épica #14021) — lee los tipos publicados y su matriz documental y le aplica
/// <see cref="ElegibilidadPersonaNatural"/>. Solo lectura sobre tablas existentes; ningún tipo se nombra aquí.
/// </summary>
public sealed class PersonaNaturalTiposReader(FlitDbContext db)
{
    /// <summary>Todos los tipos activos y publicados, elegibles o no (los no elegibles llevan su motivo).</summary>
    public Task<IReadOnlyList<TipoPersonaNatural>> ListarAsync(CancellationToken ct, string? codigo = null) =>
        LeerAsync(soloActivos: true, codigo, ct);

    /// <summary>
    /// Solo para la pantalla de administración: los tipos ACTIVOS en FLIT 2 (publicados y activos), elegibles o no, con su
    /// motivo; más los ya habilitados en la lista blanca que dejaron de estar activos (para poder apagarlos). Borradores y
    /// archivados no se listan. El portal NO usa este método.
    /// </summary>
    public Task<IReadOnlyList<TipoPersonaNatural>> ListarParaAdministracionAsync(IReadOnlyCollection<Guid> habilitados, CancellationToken ct) =>
        LeerAsync(soloActivos: false, null, ct, habilitados);

    private async Task<IReadOnlyList<TipoPersonaNatural>> LeerAsync(
        bool soloActivos, string? codigo, CancellationToken ct, IReadOnlyCollection<Guid>? habilitados = null)
    {
        var extra = habilitados?.ToList() ?? [];
        var tipos = await db.ProcedureTypes.AsNoTracking()
            .Where(t => soloActivos
                ? t.IsActive && t.PublicationStatus == PublicationStatus.Published && (codigo == null || t.Code == codigo)
                : (t.IsActive && t.PublicationStatus == PublicationStatus.Published) || extra.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Code, t.Name, t.Family, t.Version, t.GateProfile, t.WizardEnabled, t.IsActive, t.PublicationStatus })
            .ToListAsync(ct).ConfigureAwait(false);
        if (tipos.Count == 0)
            return [];

        var ids = tipos.Select(t => t.Id).ToList();
        var documentos = await (
                from r in db.ProcedureDocumentRequirements.AsNoTracking()
                join d in db.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals d.Id
                where ids.Contains(r.ProcedureTypeId) && r.IsMandatory && !r.IsDummy && r.ConditionGroup == null
                select new { r.ProcedureTypeId, d.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        var porTipo = documentos.GroupBy(d => d.ProcedureTypeId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.Code).ToList());

        return tipos.Select(t =>
        {
            var perfil = ProcedureTypeGateProfile.FromJson(t.GateProfile);
            var obligatorios = porTipo.GetValueOrDefault(t.Id) ?? [];
            var eleg = ElegibilidadPersonaNatural.Evaluar(new PerfilTipoParaElegibilidad(
                t.Code, t.Family, Publicado: t.PublicationStatus == PublicationStatus.Published, t.IsActive, t.WizardEnabled, perfil, obligatorios));
            return new TipoPersonaNatural(
                t.Id, t.Code, t.Name, t.Family, t.Version, perfil, eleg, t.WizardEnabled,
                ActivoEnFlit: t.IsActive && t.PublicationStatus == PublicationStatus.Published);
        }).ToList();
    }
}
