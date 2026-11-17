using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// <c>GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato</c> (HU #13741, plan §6.1, ADR-0073). Cuelgan del grupo
/// <c>/backoffice</c>: <see cref="PoliticaBackoffice"/> (permiso <c>diagnostico.backoffice.ver</c> + <c>tenant_type=FLIT</c>),
/// la empresa explícita del segmento <c>{empresa}</c> (la lectura queda en <c>lecturas_backoffice</c>) y, encima,
/// <c>diagnostico.configuracion.contrato.editar</c>. La escritura sobre la fila de la otra compañía va por
/// <see cref="EscrituraEntreEmpresas"/> (solo <c>configuracion_empresa</c> y <c>cambios_contrato</c> de esa empresa) y deja
/// cada cambio en <c>cambios_contrato</c>. Las reglas las pone <see cref="ServicioConfiguracion.AplicarContrato"/>. El GET y el PUT
/// traen <c>fuentes_disponibles</c> (HU #13863) con la misma regla del 422 <c>FUENTE_NO_DISPONIBLE</c>.
/// </summary>
internal static class ContratoBackofficeEndpoints
{
    public const string Ruta = "/empresas/{empresa}/configuracion/contrato";

    /// <summary>Lo único que la edición del contrato puede escribir en la otra compañía.</summary>
    private static readonly Type[] TiposDelContrato = [typeof(ConfiguracionEmpresa), typeof(CambioContrato)];

    public static void Map(RouteGroupBuilder backoffice)
    {
        backoffice.MapGet(Ruta, Obtener).RequirePermiso(Permisos.ConfiguracionContratoEditar).WithName("ObtenerContratoEmpresaBackoffice");
        backoffice.MapPut(Ruta, Editar).RequirePermiso(Permisos.ConfiguracionContratoEditar).WithName("EditarContratoEmpresaBackoffice");
    }

    private static async Task<IResult> Obtener(HttpContext http, DiagnosticoDb db, Consultas.FuentesDisponibles fuentes, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var configuracion = await db.DeLaEmpresa<ConfiguracionEmpresa>(empresa).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new ConfiguracionEmpresa { TenantId = empresa };
        return Results.Ok(ContratoBackofficeDto.De(configuracion, fuentes.Todas()));
    }

    private static async Task<IResult> Editar(
        HttpContext http, JsonElement cuerpo, DiagnosticoDb db, ServicioConfiguracion servicio, Consultas.FuentesDisponibles fuentes, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var configuracion = await db.ParaEditarDeLaEmpresa<ConfiguracionEmpresa>(empresa).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var nueva = configuracion is null;
        configuracion ??= new ConfiguracionEmpresa { TenantId = empresa };

        var cambios = servicio.AplicarContrato(configuracion, cuerpo);
        if (cambios.Count > 0)
        {
            if (nueva)
                db.Add(configuracion);
            db.AddRange(cambios);
            await db.GuardarDeLaEmpresaAsync(empresa, TiposDelContrato, ct).ConfigureAwait(false);
        }

        return Results.Ok(ContratoBackofficeDto.De(configuracion, fuentes.Todas()));
    }
}
