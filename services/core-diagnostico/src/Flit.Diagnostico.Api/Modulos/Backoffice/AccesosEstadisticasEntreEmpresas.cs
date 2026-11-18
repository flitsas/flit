using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Lectura entre compañías de las estadísticas del backoffice (HU #14579, BE-1; SP-4, ADR-0088). No es un acceso genérico: un acceso
/// <c>internal</c> por cada tabla que lee la calculadora, todos sin seguimiento y con <c>tenant_id IN (empresas)</c>, donde
/// <c>empresas</c> son las habilitadas de Identidad (E2) o la empresa validada de E3. La calculadora (<c>Modulos/Estadisticas</c>)
/// recibe estas consultas ya acotadas y une siempre por <c>tenant_id</c> además del id. Quien llama audita antes de responder (SP-7).
/// </summary>
internal static class AccesosEstadisticasEntreEmpresas
{
    public static FuentesEstadisticas De(DiagnosticoDb db, IReadOnlyCollection<Guid> empresas)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(empresas);
        var ids = empresas.Distinct().ToList();
        return new FuentesEstadisticas(
            db.Set<CabeceraDiagnostico>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<VehiculoDiagnostico>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<CeldaConsulta>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<IntentoConsulta>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<DictamenVehiculo>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<SaneamientoEntidad>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<OperacionDerivacion>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<ItemDerivacion>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)));
    }
}

/// <summary>
/// Las compañías con Diagnóstico activo según Identidad, una llamada por petición, para las vistas globales del backoffice (HU #14579,
/// BE-1; SP-4): la misma lista del selector (<c>GET /backoffice/empresas</c>). Identidad caída: 503 <c>IDENTIDAD_NO_DISPONIBLE</c> con
/// <c>Retry-After: 30</c>; sin Identidad configurada: 503 <c>IDENTIDAD_NO_CONFIGURADA</c>. Nunca «leer igual» ni una lista vacía que
/// parezca real. La usan E2 y, en BE-4, los reportes globales.
/// </summary>
internal static partial class EmpresasHabilitadasPlataforma
{
    public static async Task<IReadOnlyList<EmpresaPlataforma>> ListarAsync(IEmpresasPlataforma plataforma, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plataforma);
        IReadOnlyList<EmpresaPlataforma>? empresas;
        try
        {
            empresas = await plataforma.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (EmpresasBackofficeEndpoints.EsFallaDeIdentidad(ex) && !ct.IsCancellationRequested)
        {
            LogNoDisponible(logger, ex is RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name);
            throw new ReglaException(EmpresasBackofficeEndpoints.CodigoNoDisponible,
                "No se pudo leer la lista de compañías de Identidad. Intenta de nuevo en unos segundos.", StatusCodes.Status503ServiceUnavailable)
            { RetryAfterSegundos = 30 };
        }

        return empresas ?? throw new ReglaException(EmpresasBackofficeEndpoints.CodigoNoConfigurada,
            "La lista de compañías no está disponible: el servicio no tiene configurada la conexión con Identidad.", StatusCodes.Status503ServiceUnavailable);
    }

    // Solo el motivo técnico (SP-9): ni nombres ni NIT de la lista.
    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer de Identidad la lista de compañías habilitadas ({Motivo}): la vista global responde 503.")]
    private static partial void LogNoDisponible(ILogger logger, string motivo);
}
