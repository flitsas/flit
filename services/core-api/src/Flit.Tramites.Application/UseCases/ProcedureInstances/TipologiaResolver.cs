using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Punto único de cableado modalidad/tipología para runtime (Slice 4b).
///
/// <para><b>Derivación al crear instancia</b> (<see cref="FromFamily"/>): mapea
/// <see cref="ProcedureType.Family"/> → <c>(modalidad_entrada, tipologia_codigo)</c>.
/// Familia <c>TRASPASO</c> → traspaso/<c>traspaso_standard</c>; cualquier otra familia
/// (MATRICULAS, OTROS, desconocida) → matrícula inicial/<c>matricula_inicial</c>.
/// Criterio: solo TRASPASO tiene modalidad placa-first diferenciada en el MVP; el resto
/// converge en matrícula inicial (modalidad por defecto histórica) hasta que se configuren
/// las tipologías diferidas (sucesión, remate, importación, flota).</para>
///
/// <para><b>Default defensivo del resolver</b> (<see cref="ResolveCodigo"/>): para instancias
/// que no tienen <c>tipologia_codigo</c> persistido (creadas antes de Slice 4b), resuelve el
/// código de tipología MVP a partir de <c>modalidad_entrada</c>. Necesario porque la modalidad
/// <c>traspaso</c> NO es un código de tipología válido (la tipología es <c>traspaso_standard</c>),
/// así que el fallback ingenuo <c>tipologia_codigo ?? modalidad_entrada</c> dejaba el gating de
/// traspaso inerte. Usado por wizard, GetChecklist y AutoMark para mantener consistencia.</para>
/// </summary>
public static class TipologiaResolver
{
    /// <summary>
    /// Deriva <c>(modalidad_entrada, tipologia_codigo)</c> desde la familia del procedure_type.
    /// Case-insensitive sobre <see cref="ProcedureFamily"/>.
    /// </summary>
    public static (string ModalidadEntrada, string TipologiaCodigo) FromFamily(string? family)
    {
        if (string.Equals(family, ProcedureFamily.Traspaso, StringComparison.OrdinalIgnoreCase))
        {
            return (TramiteModalidadEntradaCodes.Traspaso, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        }

        // MATRICULAS, OTROS y cualquier familia desconocida → matrícula inicial (default MVP).
        return (TramiteModalidadEntradaCodes.MatriculaInicial, TramiteTipologiaCatalog.CodigoMatriculaInicial);
    }

    /// <summary>
    /// Resuelve el código de tipología efectivo para checklist/gating.
    /// Prioriza <c>tipologia_codigo</c> persistido; si es null/vacío, deriva el código MVP desde
    /// <c>modalidad_entrada</c> (matrícula→<c>matricula_inicial</c>, traspaso→<c>traspaso_standard</c>).
    /// Devuelve <c>null</c> solo si ninguno resuelve a una tipología del catálogo.
    /// </summary>
    public static string? ResolveCodigo(string? tipologiaCodigo, string? modalidadEntrada)
    {
        if (!string.IsNullOrEmpty(tipologiaCodigo) && TramiteTipologiaCatalog.IsValid(tipologiaCodigo))
        {
            return tipologiaCodigo;
        }

        var modalidad = TramiteModalidadEntradaCodes.FromCode(modalidadEntrada);
        return modalidad switch
        {
            TramiteModalidadEntrada.Traspaso => TramiteTipologiaCatalog.CodigoTraspasoStandard,
            TramiteModalidadEntrada.MatriculaInicial => TramiteTipologiaCatalog.CodigoMatriculaInicial,
            // Modalidad no canónica: último intento por si el código persistido coincide con el catálogo.
            _ => TramiteTipologiaCatalog.IsValid(modalidadEntrada) ? modalidadEntrada : null,
        };
    }
}
