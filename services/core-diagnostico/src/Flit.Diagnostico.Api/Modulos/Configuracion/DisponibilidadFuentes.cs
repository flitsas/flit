using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// Qué fuentes puede contratar hoy una compañía (HU #13741, R-9). <see cref="Fuente.ImpuestoDepartamental"/> solo con la
/// consulta automatizada (C3): mientras <c>Diagnostico:Fuentes:ImpuestoDisponible</c> sea false (por defecto, en todos los
/// ambientes) el backoffice no puede habilitarla. Consultar la capacidad a core-consultas queda para D5. Se lee en cada
/// llamada, así que un cambio de configuración recargable aplica sin reiniciar.
/// </summary>
public sealed class DisponibilidadFuentes(IConfiguration configuracion)
{
    public const string ClaveImpuestoDisponible = "Diagnostico:Fuentes:ImpuestoDisponible";

    public bool Disponible(Fuente fuente) =>
        fuente != Fuente.ImpuestoDepartamental || configuracion.GetValue(ClaveImpuestoDisponible, false);
}
