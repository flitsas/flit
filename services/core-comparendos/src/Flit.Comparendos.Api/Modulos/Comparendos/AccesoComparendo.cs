using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Comparendos;

/// <summary>Busca un comparendo con las reglas de empresa: un usuario solo el de su empresa; el SuperAdmin cualquiera, solo para leer.</summary>
public sealed class AccesoComparendo(ComparendosDb db, UsuarioActual usuario)
{
    public async Task<Comparendo> ParaLeerAsync(Guid id, CancellationToken ct, bool seguimiento = false)
    {
        var q = seguimiento ? db.Comparendos.Include(c => c.Contraventor) : db.Comparendos.AsNoTracking().Include(c => c.Contraventor);
        var c = await q.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (c is null || (!usuario.EsSuperAdmin && c.TenantId != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El comparendo");
        usuario.AnotarLectura(c.TenantId);
        return c;
    }

    /// <summary>Para operar: siempre de la empresa del usuario; el SuperAdmin no opera [Excel S2].</summary>
    public async Task<Comparendo> ParaOperarAsync(Guid id, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        return await db.Comparendos.Include(c => c.Contraventor).FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El comparendo");
    }
}
