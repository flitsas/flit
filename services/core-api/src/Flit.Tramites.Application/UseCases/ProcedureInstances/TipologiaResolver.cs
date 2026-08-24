using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Deriva la modalidad de entrada al crear la instancia (Slice 4b).
///
/// <para>ADR-0050 — pieza EN RETIRADA. <c>ResolveCodigo</c> se eliminó: la tipología es el
/// <c>code</c> del tipo (<c>ProcedureInstance.TypeCode</c>) y ya no hay que derivarla de la
/// modalidad. Solo queda <see cref="FromFamily"/>, que sobrevive mientras la columna
/// <c>modalidad_entrada</c> siga existiendo; desaparece con el DDL 80.</para>
///
/// <para><b>Derivación al crear instancia</b> (<see cref="FromFamily"/>): mapea
/// <see cref="ProcedureType.Family"/> → <c>(modalidad_entrada, tipologia_codigo)</c>.
/// Familia <c>TRASPASO</c> → traspaso/<c>traspaso_standard</c>; cualquier otra familia
/// (MATRICULAS, OTROS, desconocida) → matrícula inicial/<c>matricula_inicial</c>.
/// Criterio: solo TRASPASO tiene modalidad placa-first diferenciada en el MVP; el resto
/// converge en matrícula inicial (modalidad por defecto histórica) hasta que se configuren
/// las tipologías diferidas (sucesión, remate, importación, flota).</para>
/// </summary>
public static class TipologiaResolver
{
    /// <summary>
    /// Deriva <c>(modalidad_entrada, tipologia_codigo)</c> desde la familia del procedure_type.
    /// Case-insensitive sobre <see cref="ProcedureFamily"/>.
    /// </summary>
    public static (string ModalidadEntrada, string TipologiaCodigo) FromFamily(string? family)
    {
        if (ProcedureFamilyCodes.FromCode(family) == ProcedureFamily.Traspaso)
        {
            return (ProcedureFamilyCodes.Traspaso, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        }

        // MATRICULAS, OTROS y cualquier familia desconocida → matrícula inicial (default MVP).
        return (ProcedureFamilyCodes.Matriculas, TramiteTipologiaCatalog.CodigoMatriculaInicial);
    }

}
