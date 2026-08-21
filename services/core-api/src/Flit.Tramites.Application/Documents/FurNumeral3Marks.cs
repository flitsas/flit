using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Unión de casillas del numeral 3 del FUR (tablas 1+2+3).
/// Fuente: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>.
/// </summary>
public static class FurNumeral3Marks
{
    /// <summary>Ids <c>requested_process_N</c> que el mapper puede emitir (sin 6 ni 14).</summary>
    public static readonly int[] Emittable = [1, 2, 3, 4, 5, 7, 8, 10, 11, 12, 13, 15, 16, 17, 18];

    public static IReadOnlySet<int> Resolve(FurDocumentData data) =>
        Resolve(data.TipologiaCodigo, data.Modalidad, data.PrendaMarking, data.Transformaciones);

    public static HashSet<int> Resolve(
        string? tipologiaCodigo,
        string? modalidad,
        FurPrendaMarking prenda,
        FurTransformacionesDeclaradas transformaciones)
    {
        var code = Norm(tipologiaCodigo);
        var marks = new HashSet<int>(BaseBoxes(code, modalidad));

        if (!IsPrendaBase(code))
        {
            if (prenda is FurPrendaMarking.Constitucion or FurPrendaMarking.Ambos)
                marks.Add(11);
            if (prenda is FurPrendaMarking.Levantamiento or FurPrendaMarking.Ambos)
                marks.Add(12);
        }

        if (!IsColorBase(code) && transformaciones.Color)
            marks.Add(5);
        if (!IsCarroceriaBase(code) && transformaciones.Carroceria)
            marks.Add(17);
        if (!IsCombustibleBase(code) && transformaciones.Combustible)
            marks.Add(18);

        return marks;
    }

    public static string FieldId(int n) => $"requested_process_{n}";

    private static IReadOnlyList<int> BaseBoxes(string code, string? modalidad)
    {
        if (code is "CANCELACION_MATRICULA")
            return [13];
        if (code is "REMATRICULA")
            return [16];
        if (code is "MATRICULA_NUEVA" or "MATRICULA_LEASING" or "MATRICULA_INICIAL")
            return [1];
        if (code.Contains("TRASPASO", StringComparison.Ordinal))
            return [2];
        if (code is "TRASLADO_CUENTA")
            return [3];
        if (code is "RADICADO_CUENTA")
            return [4];
        if (code is "CAMBIO_COLOR")
            return [5];
        if (code is "REGRABAR_MOTOR_CHASIS")
            return [7, 8];
        if (code is "DUPLICADO_TARJETA")
            return [10];
        if (code is "PRENDA_INSCRIPCION")
            return [11];
        if (code is "LEVANTAMIENTO_PRENDA")
            return [12];
        if (code is "LEVANTAR_INSCRIBIR_PRENDA")
            return [11, 12];
        if (code is "DUPLICADO_PLACA")
            return [15];
        if (code is "CAMBIO_CARROCERIA")
            return [17];
        if (code is "CONVERSION_COMBUSTIBLE" or "CAMBIO_LOCATARIO" or "CAMBIO_ACREEDOR")
            return [18];
        if (code is "BLINDAJE")
            return [];

        var familia = Norm(modalidad);
        if (familia.Contains("TRASPASO", StringComparison.Ordinal) || familia == ProcedureFamily.Traspaso)
            return [2];
        if (code.Contains("MATRICULA", StringComparison.Ordinal)
            || familia.Contains("MATRICULA", StringComparison.Ordinal)
            || familia == ProcedureFamily.Matriculas)
            return [1];

        return [];
    }

    private static bool IsPrendaBase(string code) =>
        code is "PRENDA_INSCRIPCION" or "LEVANTAMIENTO_PRENDA" or "LEVANTAR_INSCRIBIR_PRENDA";

    private static bool IsColorBase(string code) => code is "CAMBIO_COLOR";

    private static bool IsCarroceriaBase(string code) => code is "CAMBIO_CARROCERIA";

    private static bool IsCombustibleBase(string code) => code is "CONVERSION_COMBUSTIBLE";

    private static string Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToUpperInvariant();
}
