using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Catálogo de fuentes disponibles (HU #13733, F03-C8, F03-C9, F03-C15): una fuente está disponible si tiene adaptador
/// (<see cref="IRegistroAdaptadores"/>), si ese adaptador no es el no conectado (HU #13791, F05-C2: sin simulación ni
/// <c>Diagnostico:Consultas:Address</c> las fuentes de core-consultas no se ofrecen; RUNT conserva su regla) y, para <see cref="Fuente.ImpuestoDepartamental"/>, además si
/// <c>Diagnostico:Fuentes:ImpuestoDisponible=true</c> (<see cref="DisponibilidadFuentes"/>; ausente equivale a false) y si la
/// réplica de departamentos con impuesto tiene al menos uno soportado (<see cref="Catalogos.EstadoCapacidadImpuesto.HaySoportados"/>;
/// HU #13863, Q5=a: bandera ∧ adaptador conectado ∧ réplica). Sin C3 la réplica está vacía y el adaptador es el no conectado
/// (o responde UNIMPLEMENTED), así que el impuesto nunca está disponible en ambientes. Lo consultan el contrato del backoffice
/// (<see cref="ServicioConfiguracion.AplicarContrato"/>), la creación y edición de la cabecera (<c>ServicioCabecera</c>):
/// habilitar o pedir una fuente no disponible responde 422 <c>FUENTE_NO_DISPONIBLE</c>, y <see cref="Todas"/> alimenta
/// <c>fuentes_disponibles</c> de <c>GET/PUT /configuracion</c> y del contrato del backoffice con la misma regla. Así ningún lote
/// queda con celdas sin trabajador.
/// </summary>
/// <example>
/// <code>
/// if (!fuentes.Disponible(Fuente.SimitVehiculo)) throw new ReglaException("FUENTE_NO_DISPONIBLE", "...");
/// var disponibles = fuentes.Todas(); // [RUNT, ..., AVALUO] sin C3
/// </code>
/// </example>
public sealed class FuentesDisponibles(IRegistroAdaptadores registro, DisponibilidadFuentes bandera, Catalogos.EstadoCapacidadImpuesto capacidad)
{
    public bool Disponible(Fuente fuente) =>
        registro.TieneAdaptador(fuente) && !registro.EsNoConectada(fuente) && bandera.Disponible(fuente)
        && (fuente != Fuente.ImpuestoDepartamental || capacidad.HaySoportados);

    /// <summary>Las fuentes disponibles hoy, en el orden del enum (HU #13863, F09-C8). No depende de la compañía.</summary>
    public IReadOnlyList<Fuente> Todas() => Enum.GetValues<Fuente>().Where(Disponible).ToList();
}
