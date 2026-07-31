using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11141 — <b>única decisión sobre si la firma del baúl cubre a una parte del trámite.</b>
///
/// <para><b>Por qué existe.</b> La regla estaba copiada en varios sitios y una de las copias se quedó
/// atrás: al añadir la elección explícita de mecanismo (HU #11061) se actualizó el generador de
/// documentos, pero no la consulta que alimenta la interfaz. Resultado: los documentos se firmaban con
/// la validación de identidad —lo correcto— mientras el paso FUR y las pestañas de comprador/vendedor
/// del expediente rotulaban a la parte como «firmado desde el baúl».</para>
///
/// <para>Con la decisión en un solo predicado, la vista y el documento no pueden volver a discrepar:
/// lo que se muestra es, por construcción, lo que se va a plasmar.</para>
///
/// <para><b>Qué NO decide.</b> Si existe una firma vigente en el baúl para esa persona — eso lo
/// resuelve el repositorio del baúl. Aquí solo se responde si <i>procede</i> siquiera buscarla.</para>
/// </summary>
public static class FirmaBaulCobertura
{
    /// <summary>
    /// ¿Procede consumir la firma del baúl para este actor?
    ///
    /// <para>Dos condiciones, y las dos importan:</para>
    /// <list type="number">
    ///   <item><b>El actor es una persona jurídica.</b> El baúl solo se consume por el representante
    ///   legal de una compañía; una persona natural firma con su validación de identidad.</item>
    ///   <item><b>El gestor no eligió explícitamente el sello de identidad.</b> Sin elección se
    ///   mantiene la precedencia del baúl (HU #11031).</item>
    /// </list>
    /// </summary>
    public static bool Aplica(ProcedureInstanceActor? actor)
    {
        if (actor is null || !EsJuridico(actor.DocumentType))
            return false;

        var (_, _, representanteLegal, _) = PutActorsHandler.ParseMetadata(actor.Metadata);
        return MecanismoFirma.ConsumeBaul(representanteLegal?.MecanismoFirma);
    }

    /// <summary>
    /// Persona jurídica por su tipo de documento. <c>N</c> es el código del RUNT para NIT y llega así
    /// desde algunos proveedores de consulta.
    /// </summary>
    public static bool EsJuridico(string? documentType)
    {
        var t = documentType?.Trim();
        return string.Equals(t, "NIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "N", StringComparison.OrdinalIgnoreCase);
    }
}
