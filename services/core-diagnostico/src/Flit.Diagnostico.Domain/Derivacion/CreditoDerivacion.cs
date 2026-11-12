namespace Flit.Diagnostico.Domain.Derivacion;

/// <summary>
/// Crédito por compañía del masivo (Punto 4, incremento 3, diseño §5.3 punto 2, ADR-0084): cada compañía tiene como mucho
/// <c>CreditoPorCompania</c> ítems ENVIADO a la vez. Al encolar y al cerrar cada ítem se liberan los que caben; así la cola del broker
/// nunca tiene más de crédito × compañías activas mensajes y el FIFO se comporta como un turno rotativo entre compañías.
/// </summary>
public static class CreditoDerivacion
{
    /// <summary>Cuántos ítems PENDIENTE se pueden liberar con <paramref name="enVuelo"/> ya ENVIADO (nunca negativo).</summary>
    public static int Libres(int credito, int enVuelo) => Math.Max(0, credito - Math.Max(0, enVuelo));
}
