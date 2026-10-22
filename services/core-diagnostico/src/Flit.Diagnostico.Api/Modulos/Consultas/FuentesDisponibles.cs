using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Catálogo de fuentes disponibles (HU #13733, F03-C8, F03-C9, F03-C15): una fuente está disponible si tiene adaptador
/// (<see cref="IRegistroAdaptadores"/>), si ese adaptador no es el no conectado (HU #13791, F05-C2: sin simulación ni
/// <c>Diagnostico:Consultas:Address</c> las fuentes de core-consultas no se ofrecen; RUNT conserva su regla) y, para <see cref="Fuente.ImpuestoDepartamental"/>, además si
/// <c>Diagnostico:Fuentes:ImpuestoDisponible=true</c> (<see cref="DisponibilidadFuentes"/>; ausente equivale a false). Lo
/// consultan el contrato del backoffice (<see cref="ServicioConfiguracion.AplicarContrato"/>) y la creación y edición de la
/// cabecera (<c>ServicioCabecera</c>): habilitar o pedir una fuente no disponible responde 422 <c>FUENTE_NO_DISPONIBLE</c>.
/// Así ningún lote queda con celdas sin trabajador.
/// </summary>
/// <example>
/// <code>
/// if (!fuentes.Disponible(Fuente.SimitVehiculo)) throw new ReglaException("FUENTE_NO_DISPONIBLE", "...");
/// </code>
/// </example>
public sealed class FuentesDisponibles(IRegistroAdaptadores registro, DisponibilidadFuentes bandera)
{
    public bool Disponible(Fuente fuente) =>
        registro.TieneAdaptador(fuente) && !registro.EsNoConectada(fuente) && bandera.Disponible(fuente);
}
