using System;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Efecto de una regla condicional sobre el checklist.</summary>
public enum ConditionalEffect
{
    /// <summary>Agrega el documento (si no existe) cuando la condición se cumple.</summary>
    Add,

    /// <summary>Fuerza obligatorio (o agrega como obligatorio) cuando la condición se cumple.</summary>
    Require,

    /// <summary>Oculta el documento cuando la condición se cumple.</summary>
    Hide,
}

/// <summary>
/// Regla condicional de obligatoriedad documental (HU #10521, RF30). Fuente de verdad en
/// código (versionada con el repo, igual que <c>TramiteTipologiaCatalog</c>). La condición
/// <see cref="When"/> se evalúa contra el <see cref="TramiteDocumentContext"/>; si se cumple,
/// se aplica <see cref="Effect"/> sobre <see cref="Item"/>. Aditiva: si ninguna regla aplica,
/// el checklist queda idéntico al base.
/// </summary>
/// <param name="Id">Identificador de la regla (trazabilidad).</param>
/// <param name="When">Predicado sobre el contexto del trámite.</param>
/// <param name="Effect">Efecto a aplicar cuando la condición se cumple.</param>
/// <param name="Item">Documento objetivo (para <c>Require</c>/<c>Hide</c> se usa su <c>Id</c>).</param>
public sealed record ConditionalRule(
    string Id,
    Func<TramiteDocumentContext, bool> When,
    ConditionalEffect Effect,
    ChecklistItem Item);
