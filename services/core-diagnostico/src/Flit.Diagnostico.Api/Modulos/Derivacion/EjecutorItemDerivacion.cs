using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>Lo que hay que hacer con un ítem después de llamar a Trámites.</summary>
public abstract record PasoDerivacion
{
    /// <summary>Cerrar el ítem con <see cref="CierreItemDerivacion"/> (CREADO, OMITIDO o FALLIDO) tras <paramref name="Intentos"/> llamadas.</summary>
    public sealed record Cerrar(ResultadoBorrador Resultado, short Intentos) : PasoDerivacion;

    /// <summary>Transitorio con intentos disponibles: reintentar desde <paramref name="DisponibleDesde"/> conservando el crédito.</summary>
    public sealed record Reintentar(short Intentos, DateTimeOffset DisponibleDesde) : PasoDerivacion;
}

/// <summary>
/// La llamada a Trámites de un ítem del masivo (Punto 4, incremento 3, diseño §5.3 puntos 3, 5 y 6), sin base de datos: espera turno
/// en el <see cref="FrenoTramites"/>, llama al rpc y decide:
/// <list type="bullet">
/// <item>Creado (también <c>ya_existia</c>) o rechazo de negocio: Trámites respondió → el freno cuenta un éxito y se cierra.</item>
/// <item>Transitorio: el freno cuenta un fallo; con intentos disponibles (<see cref="ReintentoDerivacion.Siguiente"/>) se reintenta;
/// agotado <see cref="OpcionesDerivacion.MaxIntentos"/>, se cierra FALLIDO.</item>
/// <item>No transitorio (configuración: PERMISSION_DENIED, UNIMPLEMENTED…): se cierra FALLIDO sin reintentos y sin tocar el freno.</item>
/// </list>
/// La usan el consumidor y la prueba de concurrencia y equidad.
/// </summary>
public sealed class EjecutorItemDerivacion(IClienteDerivacionTramites cliente, FrenoTramites freno, IOptions<OpcionesDerivacion> opciones, TimeProvider reloj)
{
    public async Task<PasoDerivacion> EjecutarAsync(Guid empresa, CrearBorradorDesdeDiagnosticoRequest solicitud, short intentosPrevios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await freno.EsperarTurnoAsync(ct).ConfigureAwait(false);
        var resultado = await cliente.CrearAsync(empresa, solicitud, ct).ConfigureAwait(false);
        var intentos = (short)(intentosPrevios + 1);
        switch (resultado)
        {
            case ResultadoBorrador.Creado or ResultadoBorrador.Rechazado:
                freno.RegistrarExito();
                return new PasoDerivacion.Cerrar(resultado, intentos);
            case ResultadoBorrador.NoDisponible { Transitorio: true }:
                freno.RegistrarFalloTransitorio();
                return ReintentoDerivacion.Siguiente(intentos, opciones.Value.MaxIntentos, reloj.GetUtcNow()) is { } desde
                    ? new PasoDerivacion.Reintentar(intentos, desde)
                    : new PasoDerivacion.Cerrar(resultado, intentos);
            default:
                return new PasoDerivacion.Cerrar(resultado, intentos);
        }
    }
}
