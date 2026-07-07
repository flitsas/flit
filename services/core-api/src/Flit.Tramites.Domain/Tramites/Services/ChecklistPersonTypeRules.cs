using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Reglas de checklist derivadas del tipo de persona del actor (HU #10542). Para persona
/// natural, el documento de identidad se incorpora desde la validación biométrica (certificado
/// PDF, <c>Source=system</c>), por lo que el ítem <c>cedulas</c> no se exige como carga manual;
/// persona jurídica lo conserva. Función pura, sin IO.
/// </summary>
public static class ChecklistPersonTypeRules
{
    /// <summary>Id del ítem de checklist de cédulas en el catálogo (traspaso estándar).</summary>
    public const string CedulasItemId = "cedulas";

    /// <summary>
    /// Construye el override de checklist según los tipos de persona de los actores. Oculta
    /// <c>cedulas</c> cuando hay al menos un actor persona natural y ninguno persona jurídica.
    /// Devuelve <c>null</c> (sin override) en cualquier otro caso, preservando el comportamiento
    /// actual para actores sin tipo de persona (legacy) o cuando interviene una persona jurídica.
    /// </summary>
    public static ChecklistOverride? BuildOverride(IReadOnlyCollection<string?> actorPersonTypes)
    {
        ArgumentNullException.ThrowIfNull(actorPersonTypes);

        var anyNatural = actorPersonTypes.Any(ActorPersonTypes.IsNatural);
        var anyJuridical = actorPersonTypes.Any(ActorPersonTypes.IsJuridical);

        return anyNatural && !anyJuridical
            ? new ChecklistOverride(Hide: new[] { CedulasItemId })
            : null;
    }
}
