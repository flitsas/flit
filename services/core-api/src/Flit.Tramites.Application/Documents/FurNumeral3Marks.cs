using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;
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

        // ADR-0050 — las tablas 2 y 3 son ACUMULACIÓN (art. 5.1.8), y acumular es privilegio de las
        // familias que radican varios trámites en un FUR. La familia OTROS no: ahí el gravamen o el
        // cambio ES el trámite, así que solo entra la capa que le pertenece al tipo. `modalidad`
        // trae la familia del expediente desde ADR-0050 (FurCommand la puebla con FamilyCode).
        var acumula = ProcedureTypeLayers.FamiliaAcumulaComplementarios(modalidad);

        // La prenda de un tipo prendario NO es complementaria: es el trámite. Por eso CAMBIO_ACREEDOR
        // —cuya casilla base es la 18— sigue marcando 11/12 desde su propia decisión de gravamen.
        if (!IsPrendaBase(code) && (acumula || ProcedureTypeLayers.EsTipoPrendaBase(code)))
        {
            if (prenda is FurPrendaMarking.Constitucion or FurPrendaMarking.Ambos)
                marks.Add(11);
            if (prenda is FurPrendaMarking.Levantamiento or FurPrendaMarking.Ambos)
                marks.Add(12);
        }

        // En OTROS la casilla del cambio ya la puso BaseBoxes (5 / 17 / 18, o ninguna en blindaje):
        // la tabla 3 solo podría añadir la de OTRO cambio, que es justo lo que no puede acumularse.
        if (acumula)
        {
            if (!IsColorBase(code) && transformaciones.Color)
                marks.Add(5);
            if (!IsCarroceriaBase(code) && transformaciones.Carroceria)
                marks.Add(17);
            if (!IsCombustibleBase(code) && transformaciones.Combustible)
                marks.Add(18);
        }

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
        // El traslado marca ADEMÁS la 18, igual que el radicado: la 3 dice que la matrícula se
        // traslada, pero el formulario no tiene casilla para «a qué organismo», que es el dato del
        // trámite. La 18 (Otros) acompaña y el destino se nombra en el párrafo 23.
        if (code is "TRASLADO_CUENTA")
            return [3, 18];
        // El radicado marca ADEMÁS la 18: la casilla 4 dice que se radica una cuenta, pero el
        // formulario no tiene casilla para «a otro organismo», que es lo que el trámite hace. La 18
        // (Otros) lo acompaña y el destino se nombra en el párrafo 23.
        if (code is "RADICADO_CUENTA")
            return [4, 18];
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

        // Fallback por FAMILIA (ADR-0050) para un código que el catálogo de casillas todavía no
        // contempla. Antes miraba también substrings del código y de la modalidad, así que cualquier
        // cosa que contuviera "MATRICULA" acababa marcando la casilla 1.
        //
        // La familia OTROS no cae en ninguna casilla a propósito: un blindaje o un duplicado no son
        // ni matrícula ni traspaso, y marcar una casilla equivocada en el formulario oficial es peor
        // que no marcar ninguna — el organismo devuelve el trámite y el error es imputable a FLIT.
        return ProcedureFamilyCodes.FromCode(familia) switch
        {
            ProcedureFamily.Traspaso => [2],
            ProcedureFamily.Matriculas => [1],
            _ => [],
        };
    }

    private static bool IsPrendaBase(string code) =>
        code is "PRENDA_INSCRIPCION" or "LEVANTAMIENTO_PRENDA" or "LEVANTAR_INSCRIBIR_PRENDA";

    private static bool IsColorBase(string code) => code is "CAMBIO_COLOR";

    private static bool IsCarroceriaBase(string code) => code is "CAMBIO_CARROCERIA";

    private static bool IsCombustibleBase(string code) => code is "CONVERSION_COMBUSTIBLE";

    private static string Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToUpperInvariant();
}
