using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Reutilización del dato del mismo día (HU #13739, F03-H7, F03-C13, D-10 «b con corte a medianoche»): ANTES de llamar a la
/// fuente, el procesador busca una celda COMPLETADO de origen API de la MISMA compañía, con la misma placa y la misma fuente, y
/// con <c>dato_obtenido_en</c> dentro del día de hoy en hora Colombia (<see cref="VigenciaDelDia"/>). Si existe, la celda copia
/// su resultado normalizado y su <c>dato_obtenido_en</c>, apunta a ella con <c>reutilizada_de_id</c> y se cierra por el camino
/// normal (<see cref="EscritorCeldas"/>: sube la versión del tablero, libera las demás celdas si es RUNT y recalcula el estado
/// del lote), con un intento <c>desde_cache=true</c> y sin llamar a la fuente ni consumir. En RUNT copia además los datos del
/// vehículo y los documentos cifrados (<see cref="DocumentosVehiculo.Copiar"/>, sin descifrar).
/// <para>
/// Reglas: la búsqueda filtra por <c>tenant_id</c> de forma EXPLÍCITA, además del filtro global del contexto de la celda (el
/// procesador corre fuera de una petición), y la FK compuesta <c>fk_consultas_consultas_reutilizada_de</c> impide apuntar a una
/// celda de otra empresa. La clave es la placa: con identificador VIN no hay reutilización de la celda RUNT; las demás fuentes
/// usan la placa definitiva que dejó el RUNT (sin ella, no hay reutilización). Solo se copia de una celda original (con
/// <c>reutilizada_de_id</c> nulo), así que la referencia siempre apunta al dato que sí se consultó. Las fuentes del locatario
/// solo se reutilizan si el locatario de ambos vehículos es el del proveedor: un locatario declarado puede ser distinto aunque
/// la placa sea la misma. Lo vencido no se borra: queda como historial. Usa índices existentes (<c>ix_consultas_reutilizacion</c>
/// e <c>ix_vehiculos_tenant_placa</c>).
/// </para>
/// </summary>
/// <example>
/// <code>
/// var resultado = await ReutilizacionMismoDia.BuscarAsync(contexto, reloj.GetUtcNow(), ct)
///     ?? await ejecutor.EjecutarAsync(contexto, ct);
/// </code>
/// </example>
internal static class ReutilizacionMismoDia
{
    /// <summary>
    /// El resultado copiado de la celda vigente de hoy, listo para cerrar la celda; null si no hay ninguna (entonces se llama a
    /// la fuente).
    /// </summary>
    public static async Task<ResultadoCelda?> BuscarAsync(ContextoCelda contexto, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var db = contexto.Db;
        var empresa = celda.TenantId;

        var vehiculo = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == empresa && v.Id == celda.VehiculoId, ct).ConfigureAwait(false);
        if (vehiculo is null || Placa(celda.Fuente, vehiculo) is not { } placa)
            return null;
        var porLocatario = celda.Fuente is Fuente.RuntLocatario or Fuente.SimitLocatario;
        if (porLocatario && vehiculo.LocatarioDocOrigen != OrigenDocumento.Proveedor)
            return null;

        // Npgsql solo escribe timestamptz en UTC.
        var desde = VigenciaDelDia.InicioDelDia(ahora).ToUniversalTime();
        var hasta = VigenciaDelDia.FinDelDia(ahora).ToUniversalTime();
        var origen = await (
                from c in db.Set<CeldaConsulta>().AsNoTracking()
                join v in db.Set<VehiculoDiagnostico>().AsNoTracking() on c.VehiculoId equals v.Id
                where c.TenantId == empresa && v.TenantId == empresa
                    && c.Fuente == celda.Fuente && c.Estado == EstadoCelda.Completado && c.Origen == OrigenCelda.Api
                    && c.ReutilizadaDeId == null && c.Id != celda.Id && c.Payload != null
                    && c.DatoObtenidoEn >= desde && c.DatoObtenidoEn < hasta
                    && v.Placa == placa
                    && (!porLocatario || v.LocatarioDocOrigen == OrigenDocumento.Proveedor)
                orderby c.DatoObtenidoEn descending, c.Id descending
                select new { Celda = c, Vehiculo = v })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (origen is null)
            return null;

        var original = origen.Celda;
        var deVehiculo = origen.Vehiculo;
        var efectos = celda.Fuente == Fuente.Runt
            ? new EfectosDeCierre(async (dbCierre, _, token) =>
            {
                var destino = await dbCierre.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == celda.VehiculoId, token).ConfigureAwait(false);
                CopiarDatosRunt(destino, deVehiculo, original.DatoObtenidoEn);
            })
            : null;
        return new ResultadoCelda(EstadoCelda.Completado, original.Payload, null, OrigenCelda.Api, original.DatoObtenidoEn, null, efectos)
        {
            ReutilizadaDeId = original.Id,
            Intento = new DatosIntento(EsPorPersona(celda.Fuente) ? null : placa, null, null, null),
        };
    }

    /// <summary>
    /// La placa con que se busca: en RUNT, la del identificador (nula si es VIN, D-10); en las demás fuentes, la placa definitiva
    /// que dejó el RUNT.
    /// </summary>
    internal static string? Placa(Fuente fuente, VehiculoDiagnostico vehiculo)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        if (fuente == Fuente.Runt)
            return vehiculo.TipoIdentificador == TipoIdentificador.Vin ? null : vehiculo.Placa ?? vehiculo.IdentificadorNorm;
        return string.IsNullOrWhiteSpace(vehiculo.Placa) ? null : vehiculo.Placa;
    }

    private static bool EsPorPersona(Fuente fuente) =>
        fuente is Fuente.RuntPropietario or Fuente.RuntLocatario or Fuente.SimitPropietario or Fuente.SimitLocatario;

    /// <summary>
    /// Los datos que dejó el RUNT en el vehículo original, en el vehículo de la celda reutilizada: placa, VIN, servicio,
    /// organismo, <c>runt_consultado_en</c> (el instante del dato original), la clave <c>vehiculo</c> de
    /// <c>datos_consolidados</c> (conserva las demás) y los documentos cifrados con la precedencia del locatario.
    /// </summary>
    private static void CopiarDatosRunt(VehiculoDiagnostico destino, VehiculoDiagnostico origen, DateTimeOffset? obtenidoEn)
    {
        destino.Placa = origen.Placa;
        destino.Vin = origen.Vin;
        destino.Servicio = origen.Servicio;
        destino.OrganismoNombre = origen.OrganismoNombre;
        destino.RuntConsultadoEn = obtenidoEn ?? origen.RuntConsultadoEn;
        destino.DatosConsolidados = ConsolidarVehiculo(destino.DatosConsolidados, origen.DatosConsolidados);
        DocumentosVehiculo.Copiar(destino, origen);
    }

    private static string ConsolidarVehiculo(string actual, string deOrigen)
    {
        var raiz = (string.IsNullOrWhiteSpace(actual) ? null : JsonNode.Parse(actual) as JsonObject) ?? [];
        var origen = string.IsNullOrWhiteSpace(deOrigen) ? null : JsonNode.Parse(deOrigen) as JsonObject;
        if (origen?["vehiculo"] is { } vehiculo)
            raiz["vehiculo"] = vehiculo.DeepClone();
        return raiz.ToJsonString();
    }
}
