using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>Capacidades que se contratan por empresa (especificación §3.2), validadas en cada endpoint (RN-005).</summary>
public enum Capacidad
{
    DerechosPeticion,
    ConsultaContraventor,
    CambioContraventor,
    Ocr,
    Reportes,
}

/// <summary>
/// Configuración de Comparendos de una empresa. Si la empresa todavía no tiene fila, se crea con los valores por
/// defecto (todo apagado salvo lo básico) la primera vez que alguien la necesita.
/// </summary>
public sealed class Capacidades(ComparendosDb db, Reloj reloj)
{
    public async Task<ConfiguracionEmpresa> DeAsync(Guid tenantId, CancellationToken ct, string? nombreEmpresa = null)
    {
        var config = await db.ConfiguracionesEmpresa.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct).ConfigureAwait(false);
        if (config is not null)
        {
            if (nombreEmpresa is { Length: > 0 } && config.EmpresaNombre != nombreEmpresa)
            {
                config.EmpresaNombre = nombreEmpresa;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return config;
        }

        config = new ConfiguracionEmpresa { TenantId = tenantId, EmpresaNombre = nombreEmpresa, CreadoEn = reloj.Ahora };
        db.ConfiguracionesEmpresa.Add(config);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Otra petición la creó a la vez: se usa la suya.
            db.Entry(config).State = EntityState.Detached;
            config = await db.ConfiguracionesEmpresa.FirstAsync(c => c.TenantId == tenantId, ct).ConfigureAwait(false);
        }

        return config;
    }

    public static bool Tiene(ConfiguracionEmpresa config, Capacidad capacidad)
    {
        ArgumentNullException.ThrowIfNull(config);
        return capacidad switch
        {
            Capacidad.DerechosPeticion => config.HasRightPetitionModule,
            Capacidad.ConsultaContraventor => config.AllowsContravenerConsultation,
            Capacidad.CambioContraventor => config.AllowsContravenerChange,
            Capacidad.Ocr => config.HasOcrAccess,
            Capacidad.Reportes => config.HasReportesModule,
            _ => false,
        };
    }

    /// <summary>403 <c>CAPACIDAD_NO_HABILITADA</c> si la empresa no tiene la capacidad.</summary>
    public async Task<ConfiguracionEmpresa> ExigirAsync(Guid tenantId, Capacidad capacidad, CancellationToken ct)
    {
        var config = await DeAsync(tenantId, ct).ConfigureAwait(false);
        if (!Tiene(config, capacidad))
        {
            throw ReglaException.Prohibido("CAPACIDAD_NO_HABILITADA",
                $"Tu empresa no tiene habilitada la capacidad «{Nombre(capacidad)}» de Comparendos.");
        }

        return config;
    }

    public static string Nombre(Capacidad capacidad) => capacidad switch
    {
        Capacidad.DerechosPeticion => "Derechos de petición",
        Capacidad.ConsultaContraventor => "Consulta de contraventor",
        Capacidad.CambioContraventor => "Cambio de contraventor",
        Capacidad.Ocr => "Carga por OCR",
        Capacidad.Reportes => "Reportes",
        _ => capacidad.ToString(),
    };
}
