using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Derivacion;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// ÚNICO punto que cierra un ítem de derivación (Punto 4, diseño §5.3 punto 4, ADR-0084): con <see cref="ResultadoBorrador.Creado"/>
/// el ítem pasa a CREADO y el vehículo guarda el trámite (solo si no tenía uno: se fija una vez, R-16); con
/// <see cref="ResultadoBorrador.Rechazado"/>, OMITIDO con el código; con <see cref="ResultadoBorrador.NoDisponible"/>, FALLIDO con
/// TRAMITES_NO_DISPONIBLE. Suma al contador de la operación y la pasa a TERMINADA al completar su total. Solo modifica las entidades
/// seguidas; el llamador guarda en la misma transacción (en el incremento 3, junto con la liberación del crédito).
/// </summary>
public static class CierreItemDerivacion
{
    /// <summary>
    /// <see cref="Cerrar"/> con la versión de fila del tablero (HU #14563, ADR-0086 D-1): primero sube la cabecera y la fila
    /// (<see cref="EscritorCeldas.SubirVersionFilaAsync"/>; orden de bloqueo cabecera → vehículo → ítem) y después cierra el ítem en
    /// memoria. Debe ir en la transacción del cierre; el llamador guarda. Devuelve la versión de fila.
    /// </summary>
    internal static async Task<long> CerrarAsync(
        DiagnosticoDb db, OperacionDerivacion operacion, ItemDerivacion item, VehiculoDiagnostico vehiculo, ResultadoBorrador resultado, DateTimeOffset ahora,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        ArgumentNullException.ThrowIfNull(vehiculo);
        var version = await EscritorCeldas.SubirVersionFilaAsync(db, operacion.DiagnosticoId, vehiculo.Id, ct).ConfigureAwait(false);
        Cerrar(operacion, item, vehiculo, resultado, ahora);
        return version;
    }

    public static void Cerrar(OperacionDerivacion operacion, ItemDerivacion item, VehiculoDiagnostico vehiculo, ResultadoBorrador resultado, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(vehiculo);
        ArgumentNullException.ThrowIfNull(resultado);
        if (item.Estado is not (EstadoItemDerivacion.Enviado or EstadoItemDerivacion.Pendiente))
            throw new InvalidOperationException($"El ítem {item.Id} ya está cerrado ({item.Estado}).");
        if (item.OperacionId != operacion.Id || item.VehiculoId != vehiculo.Id)
            throw new InvalidOperationException($"El ítem {item.Id} no es de la operación o del vehículo dados.");

        switch (resultado)
        {
            case ResultadoBorrador.Creado creado:
                item.Estado = EstadoItemDerivacion.Creado;
                item.MotivoCodigo = null;
                item.TramiteId = creado.TramiteId;
                item.TramiteNumero = creado.Numero;
                if (vehiculo.TramiteId is null)
                {
                    vehiculo.TramiteId = creado.TramiteId;
                    vehiculo.TramiteNumero = creado.Numero;
                    vehiculo.TramiteCreadoEn = ahora;
                }

                operacion.Creadas++;
                break;
            case ResultadoBorrador.Rechazado rechazado:
                item.Estado = EstadoItemDerivacion.Omitido;
                item.MotivoCodigo = rechazado.Codigo;
                operacion.Omitidas++;
                break;
            case ResultadoBorrador.NoDisponible:
                item.Estado = EstadoItemDerivacion.Fallido;
                item.MotivoCodigo = MotivoDerivacion.TramitesNoDisponible;
                operacion.Fallidas++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resultado), resultado.GetType().Name, "Resultado de derivación desconocido.");
        }

        item.ActualizadoEn = ahora;
        if (operacion.Creadas + operacion.Omitidas + operacion.Fallidas >= operacion.Total)
        {
            operacion.Estado = EstadoOperacionDerivacion.Terminada;
            operacion.TerminadaEn = ahora;
        }
    }
}
