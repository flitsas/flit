namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — elige el proveedor de comparendos a partir de la fuente configurada por la
/// compañía y del tipo de persona del actor.
///
/// Regla: <b>la fuente manda; el tipo de persona solo elige cuál proveedor EXTERNO</b>.
///
/// <code>
/// fuente     | persona natural | persona jurídica
/// -----------|-----------------|------------------
/// internal   | flit_fines      | flit_fines
/// external   | verifik_simit   | kyverum_fines
/// null/otro  | verifik_simit   | kyverum_fines     (Normalize ⇒ external)
/// </code>
///
/// Función pura y estática, no un servicio inyectable: es una tabla de verdad de cuatro celdas sin
/// dependencias. Inyectarla solo añadiría un mock a cada test del preflight sin comprar nada.
///
/// Se resuelve POR ACTOR, no por trámite: en un traspaso con comprador jurídico y vendedor
/// natural, cada uno va a un proveedor distinto.
/// </summary>
public static class FinesProviderResolver
{
    public const string FlitFines = "flit_fines";
    public const string VerifikSimit = "verifik_simit";
    public const string KyverumFines = "kyverum_fines";

    public static string Resolve(string? finesQuerySource, bool isNaturalPerson) =>
        FinesSourceCodes.Normalize(finesQuerySource) == FinesSourceCodes.Internal
            ? FlitFines
            : isNaturalPerson ? VerifikSimit : KyverumFines;
}
