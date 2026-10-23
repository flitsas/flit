using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>
/// Quién se consulta y con qué proveedor (HU #13521). Una empresa entra a un ciclo solo si tiene Comparendos habilitado
/// en la plataforma y el canal configurado con proveedor. En un ciclo programado además su configuración está ACTIVA y
/// el canal encendido; una ejecución manual del SuperAdmin sirve como primera corrida aunque siga en borrador.
/// </summary>
public sealed class ElegibilidadConsultas(ComparendosDb db, IProductosHabilitados productos)
{
    public async Task<Dictionary<Guid, string>> ProveedoresAsync(CanalIngesta canal, Disparo disparo, Guid? tenant, CancellationToken ct)
    {
        var programado = disparo == Disparo.Programado;
        var filas = await (from c in db.ConfiguracionConsultaCanales.AsNoTracking()
                           join cfg in db.ConfiguracionesConsultas.AsNoTracking() on c.TenantId equals cfg.TenantId
                           where c.Canal == canal && c.Proveedor != "" && (tenant == null || c.TenantId == tenant)
                                 && (!programado || (cfg.Estado == EstadoConfiguracionConsultas.Activa && c.Encendido))
                           select new { c.TenantId, c.Proveedor }).ToListAsync(ct).ConfigureAwait(false);
        var resultado = new Dictionary<Guid, string>();
        foreach (var f in filas)
        {
            if (await productos.ComparendosHabilitadoAsync(f.TenantId, ct).ConfigureAwait(false))
                resultado[f.TenantId] = f.Proveedor;
        }

        return resultado;
    }

    /// <summary>Por qué una ejecución manual no se puede hacer (null = sí se puede).</summary>
    public async Task<string?> MotivoManualAsync(Guid tenant, CanalIngesta canal, CancellationToken ct)
    {
        if (!await productos.ComparendosHabilitadoAsync(tenant, ct).ConfigureAwait(false))
            return "La empresa no tiene Comparendos habilitado en la plataforma.";
        var proveedor = await db.ConfiguracionConsultaCanales.AsNoTracking().Where(c => c.TenantId == tenant && c.Canal == canal)
            .Select(c => c.Proveedor).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(proveedor) ? "Configura el canal antes de consultar." : null;
    }
}
