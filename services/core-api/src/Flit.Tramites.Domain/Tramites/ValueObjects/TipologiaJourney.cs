using System.Collections.Generic;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Contexto de un paso del wizard para una tipología (paridad <c>PasoTipologia</c> de Johan).</summary>
public sealed record PasoTipologia(
    int Paso,
    string Titulo,
    bool Aplica,
    string? Nota = null);

/// <summary>
/// Journey de una tipología: QUIÉN participa (partes) y CÓMO cambia cada paso del wizard
/// (paridad <c>TipologiaJourney</c> de Johan). Complementa el catálogo de checklist (QUÉ documentos).
/// </summary>
public sealed record TipologiaJourney(
    string Codigo,
    string Nombre,
    TramiteModalidadEntrada Modalidad,
    AdquirenteConfig Adquirente,
    bool VendedorRequerido,
    IReadOnlyList<ParteRequerida> Partes,
    IReadOnlyList<PasoTipologia> Pasos);
