using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// La MISMA regla con la que el paso 1 del wizard decide si una consulta de vehículo permite crear
/// el trámite (<c>TramiteWizard.tsx</c>, <c>hardBlocked</c>): un check en <c>error</c> es el
/// proveedor caído y el check <c>vehiculo</c> en <c>fail</c> es «no existe en el RUNT». En ambos
/// casos el wizard deshabilita «Continuar», y la carga masiva tiene que hacer lo propio: el PO fue
/// explícito en que un vehículo que falla NO crea trámite.
///
/// <para>Existe porque la primera versión solo miraba el código de error del handler, que es
/// <c>null</c> en estos dos casos —el semáforo en rojo viaja en los checks, no como error— y el lote
/// creaba trámites de vehículos inexistentes. Se vio probando contra el proveedor real.</para>
/// </summary>
public static class BulkTramitesVehicleGate
{
    public const string VehiculoNoEncontrado = "vehiculo_no_encontrado";
    public const string ConsultaVehiculoFallida = "consulta_vehiculo_fallida";

    private const string VehicleCheckKey = "vehiculo";
    private const string FailStatus = "fail";
    private const string ErrorStatus = "error";

    /// <summary>Código de fila que impide la creación, o <c>null</c> si la consulta sirve.</summary>
    public static string? Evaluate(IReadOnlyList<PreflightCheckDto>? checks)
    {
        if (checks is null || checks.Count == 0)
        {
            return null;
        }

        // El proveedor caído se evalúa PRIMERO: con la consulta caída, «no encontrado» no significa
        // nada y el usuario debe reintentar, no corregir la placa.
        if (checks.Any(c => string.Equals(c.Status, ErrorStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return ConsultaVehiculoFallida;
        }

        return checks.Any(c =>
            string.Equals(c.Key, VehicleCheckKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Status, FailStatus, StringComparison.OrdinalIgnoreCase))
            ? VehiculoNoEncontrado
            : null;
    }
}
