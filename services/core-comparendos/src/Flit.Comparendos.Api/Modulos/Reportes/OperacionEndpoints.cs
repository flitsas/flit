using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>
/// Los dos Inicios del rediseño R2. <c>/comparendos/operacion</c> es operación, no reporte: pide <c>BandejaVer</c> y el
/// mismo alcance que <c>/comparendos/contadores</c>, NO la capacidad de reportes; los pesos solo van si la empresa tiene el
/// módulo y el usuario <c>ReportesVer</c>. <c>/admin/resumen</c> es la salud de la plataforma para el SuperAdmin.
/// </summary>
public static class OperacionEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/comparendos/operacion", Operacion).RequirePermiso(Permisos.BandejaVer);
        api.MapGet("/admin/resumen", Resumen).RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
    }

    private static async Task<IResult> Operacion(Guid? empresa, UsuarioActual usuario, ServicioReportes reportes, ConsultaBandeja bandeja, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa)!.Value;
        // Solo lectura: no se usa Capacidades.DeAsync, que crea la configuración si falta.
        var conValores = usuario.EsSuperAdmin
            || (usuario.Tiene(Permisos.ReportesVer)
                && await db.ConfiguracionesEmpresa.AsNoTracking().AnyAsync(c => c.TenantId == tenant && c.HasReportesModule, ct).ConfigureAwait(false));
        return Results.Ok(new { empresa = tenant, operacion = await reportes.OperacionAsync(tenant, conValores, bandeja, ct).ConfigureAwait(false) });
    }

    /// <remarks><c>?partes=impacto</c> (R4): «Impacto de FLIT» del año en curso; sin <c>partes</c>, la salud y las empresas que necesitan algo.</remarks>
    private static async Task<IResult> Resumen(string? partes, ServicioReportes reportes, CancellationToken ct)
    {
        if (partes is not null && partes != "impacto")
            throw new ReglaException("PARTES_INVALIDAS", "El único valor de «partes» es «impacto».");
        return partes == "impacto"
            ? Results.Ok(await reportes.ImpactoPlataformaAsync(ct).ConfigureAwait(false))
            : Results.Ok(await reportes.ResumenPlataformaAsync(ct).ConfigureAwait(false));
    }
}
