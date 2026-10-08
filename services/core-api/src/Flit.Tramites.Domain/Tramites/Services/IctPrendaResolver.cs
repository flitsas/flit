using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Bug #13445 (D1/D3) — decide qué hacer con la prenda de un borrador que llega de ICT, cruzando lo que
/// declara el cuerpo ICT (<c>ict_prenda_*</c>, desde <c>master.Limitations*</c>) con lo que reporta la
/// consulta RUNT del vehículo (<c>runt_*</c>, ver <see cref="RuntGravamenSignal"/>). Servicio puro: sin
/// EF ni IO; el orquestador lee los <c>field_values</c> y aplica el resultado.
///
/// <para><b>Matriz conservadora (D3).</b> Solo cuatro casos se aplican solos: traspaso que levanta con
/// prenda en el RUNT (acreedor = el del RUNT), inscripción con RUNT sin prenda (acreedor del cuerpo),
/// levantar con RUNT sin prenda (<c>sin_prenda</c> + aviso) y matrícula que omite con prenda en el RUNT.
/// Todo lo demás —incluido un RUNT que no se pudo leer— queda para el gestor con un motivo estable.</para>
///
/// <para>Uso de ejemplo: <c>IctPrendaResolver.Resolver(fieldValues, ProcedureFamily.Traspaso)</c> →
/// <c>Auto("levantar", …)</c> | <c>PendienteGestor("traspaso_sin_accion_con_prenda_runt")</c> | <c>Nada</c>.</para>
/// </summary>
public static class IctPrendaResolver
{
    /// <summary>Operación de prenda del cuerpo ICT: <c>1</c> levantar · <c>2</c> registrar/inscribir · <c>3</c> omitir.</summary>
    public const string OperacionKey = "ict_prenda_operacion";

    /// <summary>Nombre del acreedor declarado por ICT (PII — nunca a logs).</summary>
    public const string AcreedorNombreKey = "ict_prenda_acreedor_nombre";

    /// <summary>Documento del acreedor declarado por ICT (PII — nunca a logs).</summary>
    public const string AcreedorDocumentoKey = "ict_prenda_acreedor_documento";

    public const string OperacionLevantar = "1";
    public const string OperacionInscribir = "2";
    public const string OperacionOmitir = "3";

    // Motivos estables (contrato con la trazabilidad ICT: prenda_pendiente_gestor:<motivo> /
    // prenda_discrepancia_runt:<motivo>). Sin PII.
    public const string MotivoRuntDesconocido = "runt_desconocido";
    public const string MotivoGravamenNoPrendario = "gravamen_no_prendario";
    public const string MotivoVariasGarantias = "varias_garantias_runt";
    public const string MotivoMatriculaLevantarAtipico = "matricula_levantar_atipico";
    public const string MotivoTraspasoInscribirConPrendaRunt = "traspaso_inscribir_con_prenda_runt";
    public const string MotivoTraspasoSinAccionConPrendaRunt = "traspaso_sin_accion_con_prenda_runt";
    public const string MotivoAcreedorDistinto = "acreedor_distinto";
    public const string MotivoAcreedorSinDocumento = "acreedor_sin_documento";
    public const string MotivoLevantarSinPrendaRunt = "levantar_sin_prenda_runt";

    /// <summary>
    /// Resuelve la prenda del borrador ICT.
    /// </summary>
    /// <param name="fieldValues">Field values de la instancia, YA con la señal <c>runt_*</c> del preflight.</param>
    /// <param name="familia">Familia del tipo. Solo Traspaso y Matrículas tienen prenda complementaria.</param>
    public static IctPrendaResolucion Resolver(
        IEnumerable<ProcedureInstanceFieldValue> fieldValues, ProcedureFamily familia)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        // ADR-0050 — en OTROS la prenda no es una capa que se añada: ni se decide sola ni se avisa.
        if (familia is not (ProcedureFamily.Traspaso or ProcedureFamily.Matriculas))
            return IctPrendaResolucion.Nada();

        var lista = fieldValues as IReadOnlyCollection<ProcedureInstanceFieldValue> ?? [.. fieldValues];
        var cuerpo = LeerCuerpo(lista);
        var (prendas, gravamenes, detalle) = RuntGravamenSignal.Leer(lista);
        var garantias = RuntGravamenSignal.Garantias(detalle);
        var senal = Senal(prendas, gravamenes, garantias.Count);
        var traspaso = familia == ProcedureFamily.Traspaso;

        if (senal == SenalRunt.Desconocida)
        {
            return cuerpo.TieneDato
                ? IctPrendaResolucion.Pendiente(MotivoRuntDesconocido)
                : IctPrendaResolucion.Nada();
        }

        if (senal == SenalRunt.Negativa)
        {
            return cuerpo.Operacion switch
            {
                OperacionLevantar => IctPrendaResolucion.Auto(
                    PrendaDecision.SinPrenda, null, null, MotivoLevantarSinPrendaRunt),
                OperacionInscribir => IctPrendaResolucion.Auto(
                    PrendaDecision.Registrar, cuerpo.AcreedorNombre, cuerpo.AcreedorDocumento),
                _ => IctPrendaResolucion.Nada(),
            };
        }

        // Positiva. Un gravamen que el RUNT reporta solo por la bandera de gravámenes —prendas «NO» y
        // ninguna garantía en el detalle— no es una prenda: embargo o medida cautelar. No se decide.
        if (EsGravamenNoPrendario(prendas, gravamenes, garantias.Count))
            return IctPrendaResolucion.Pendiente(MotivoGravamenNoPrendario);

        return (cuerpo.Operacion, traspaso) switch
        {
            (OperacionLevantar, true) => LevantarConPrendaRunt(cuerpo, garantias),
            (OperacionLevantar, false) => IctPrendaResolucion.Pendiente(MotivoMatriculaLevantarAtipico),
            (OperacionInscribir, true) => IctPrendaResolucion.Pendiente(MotivoTraspasoInscribirConPrendaRunt),
            (OperacionInscribir, false) => InscribirMatriculaConPrendaRunt(cuerpo, garantias),
            (_, true) => IctPrendaResolucion.Pendiente(MotivoTraspasoSinAccionConPrendaRunt),
            (_, false) => IctPrendaResolucion.Auto(PrendaDecision.Omitir, null, null),
        };
    }

    /// <summary>
    /// Traspaso que levanta un gravamen que el RUNT sí reporta: se levanta con el acreedor DEL RUNT (es el
    /// que consta en el registro). Si el cuerpo trae otro acreedor, sigue automático y se avisa. Con más
    /// de una garantía no se adivina cuál levantar; sin garantías (solo bandera) no hay acreedor RUNT y se
    /// usa el del cuerpo.
    /// </summary>
    private static IctPrendaResolucion LevantarConPrendaRunt(Cuerpo cuerpo, IReadOnlyList<RuntGarantia> garantias)
    {
        if (garantias.Count > 1)
            return IctPrendaResolucion.Pendiente(MotivoVariasGarantias);

        if (garantias.Count == 0)
            return IctPrendaResolucion.Auto(PrendaDecision.Levantar, cuerpo.AcreedorNombre, cuerpo.AcreedorDocumento);

        var runt = garantias[0];
        var distinto = cuerpo.AcreedorDocumento is not null
            && runt.AcreedorDocumento is not null
            && !MismoDocumento(cuerpo.AcreedorDocumento, runt.AcreedorDocumento);

        return IctPrendaResolucion.Auto(
            PrendaDecision.Levantar,
            runt.AcreedorNombre ?? cuerpo.AcreedorNombre,
            runt.AcreedorDocumento ?? cuerpo.AcreedorDocumento,
            distinto ? MotivoAcreedorDistinto : null);
    }

    /// <summary>
    /// Matrícula que inscribe con prenda ya visible en el RUNT: se registra con el acreedor del cuerpo solo
    /// si su documento coincide con el de alguna garantía del RUNT. Sin documento en el cuerpo no se puede
    /// comprobar, y con otro acreedor es una discrepancia: las dos quedan para el gestor.
    /// </summary>
    private static IctPrendaResolucion InscribirMatriculaConPrendaRunt(Cuerpo cuerpo, IReadOnlyList<RuntGarantia> garantias)
    {
        var documentosRunt = garantias
            .Select(g => g.AcreedorDocumento)
            .Where(d => d is not null)
            .ToList();

        // Solo bandera, sin detalle: no hay contra qué comparar; el RUNT no contradice al cuerpo.
        if (documentosRunt.Count == 0)
            return IctPrendaResolucion.Auto(PrendaDecision.Registrar, cuerpo.AcreedorNombre, cuerpo.AcreedorDocumento);

        if (cuerpo.AcreedorDocumento is null)
            return IctPrendaResolucion.Pendiente(MotivoAcreedorSinDocumento);

        return documentosRunt.Any(d => MismoDocumento(cuerpo.AcreedorDocumento, d!))
            ? IctPrendaResolucion.Auto(PrendaDecision.Registrar, cuerpo.AcreedorNombre, cuerpo.AcreedorDocumento)
            : IctPrendaResolucion.Pendiente(MotivoAcreedorDistinto, MotivoAcreedorDistinto);
    }

    private enum SenalRunt { Positiva, Negativa, Desconocida }

    /// <summary>
    /// Positiva = <see cref="RuntGravamenSignal.Reporta(string?, string?, string?)"/>. Negativa = al menos
    /// una bandera dice explícitamente «no», ninguna dice «sí» y no hay garantías. Lo demás —claves
    /// ausentes, vacías o con texto que no es ni sí ni no— es desconocida: no se inventa que no hay prenda.
    /// </summary>
    private static SenalRunt Senal(string? prendas, string? gravamenes, int garantias)
    {
        if (RuntGravamenSignal.EsAfirmativo(prendas) || RuntGravamenSignal.EsAfirmativo(gravamenes) || garantias > 0)
            return SenalRunt.Positiva;

        var algunaNegativa = RuntGravamenSignal.EsNegativo(prendas) || RuntGravamenSignal.EsNegativo(gravamenes);
        var algunaIlegible = (!string.IsNullOrWhiteSpace(prendas) && !RuntGravamenSignal.EsNegativo(prendas))
            || (!string.IsNullOrWhiteSpace(gravamenes) && !RuntGravamenSignal.EsNegativo(gravamenes));

        return algunaNegativa && !algunaIlegible ? SenalRunt.Negativa : SenalRunt.Desconocida;
    }

    private static bool EsGravamenNoPrendario(string? prendas, string? gravamenes, int garantias) =>
        garantias == 0
        && RuntGravamenSignal.EsAfirmativo(gravamenes)
        && RuntGravamenSignal.EsNegativo(prendas);

    /// <summary>
    /// Documentos iguales tras quitar todo lo que no sea letra o dígito y pasar a mayúscula. Un NIT con y
    /// sin dígito de verificación (<c>860034313</c> vs <c>860.034.313-7</c>) cuenta como el mismo.
    /// </summary>
    internal static bool MismoDocumento(string a, string b)
    {
        var x = Normalizar(a);
        var y = Normalizar(b);
        if (x.Length == 0 || y.Length == 0)
            return false;
        if (x == y)
            return true;

        var (corto, largo) = x.Length < y.Length ? (x, y) : (y, x);
        return largo.Length == corto.Length + 1
            && char.IsDigit(largo[^1])
            && largo.StartsWith(corto, StringComparison.Ordinal);
    }

    private static string Normalizar(string valor) =>
        new([.. valor.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private sealed record Cuerpo(string? Operacion, string? AcreedorNombre, string? AcreedorDocumento)
    {
        public bool TieneDato => Operacion is not null || AcreedorNombre is not null || AcreedorDocumento is not null;
    }

    private static Cuerpo LeerCuerpo(IEnumerable<ProcedureInstanceFieldValue> fieldValues)
    {
        string? operacion = null, nombre = null, documento = null;
        foreach (var f in fieldValues)
        {
            var valor = string.IsNullOrWhiteSpace(f.ValueText) ? null : f.ValueText.Trim();
            if (string.Equals(f.FieldKey, OperacionKey, StringComparison.OrdinalIgnoreCase))
                operacion = valor is OperacionLevantar or OperacionInscribir or OperacionOmitir ? valor : null;
            else if (string.Equals(f.FieldKey, AcreedorNombreKey, StringComparison.OrdinalIgnoreCase))
                nombre = valor;
            else if (string.Equals(f.FieldKey, AcreedorDocumentoKey, StringComparison.OrdinalIgnoreCase))
                documento = valor;
        }

        return new Cuerpo(operacion, nombre, documento);
    }
}

/// <summary>Resultado de <see cref="IctPrendaResolver"/>.</summary>
public enum IctPrendaResolucionTipo
{
    /// <summary>No hay nada que decidir ni que avisar.</summary>
    Nada,

    /// <summary>Se registra la decisión sola (<see cref="IctPrendaResolucion.Decision"/>).</summary>
    Auto,

    /// <summary>La decide el gestor; <see cref="IctPrendaResolucion.Motivo"/> dice por qué.</summary>
    PendienteGestor,
}

/// <summary>
/// Bug #13445 — decisión de prenda de un borrador ICT. <see cref="AcreedorNombre"/> y
/// <see cref="AcreedorDocumento"/> son PII: <see cref="ToString"/> no los expone.
/// </summary>
/// <param name="Tipo">Qué hacer.</param>
/// <param name="Decision">Valor de <see cref="PrendaDecision"/> cuando es <see cref="IctPrendaResolucionTipo.Auto"/>.</param>
/// <param name="AcreedorNombre">Acreedor con el que se registra (PII).</param>
/// <param name="AcreedorDocumento">Documento del acreedor (PII).</param>
/// <param name="Motivo">Motivo estable cuando es <see cref="IctPrendaResolucionTipo.PendienteGestor"/>.</param>
/// <param name="Discrepancia">Motivo estable de discrepancia con el RUNT que se avisa además, o null.</param>
public sealed record IctPrendaResolucion(
    IctPrendaResolucionTipo Tipo,
    string? Decision,
    string? AcreedorNombre,
    string? AcreedorDocumento,
    string? Motivo,
    string? Discrepancia)
{
    public static IctPrendaResolucion Nada() => new(IctPrendaResolucionTipo.Nada, null, null, null, null, null);

    public static IctPrendaResolucion Auto(
        string decision, string? acreedorNombre, string? acreedorDocumento, string? discrepancia = null) =>
        new(IctPrendaResolucionTipo.Auto, decision, acreedorNombre, acreedorDocumento, null, discrepancia);

    public static IctPrendaResolucion Pendiente(string motivo, string? discrepancia = null) =>
        new(IctPrendaResolucionTipo.PendienteGestor, null, null, null, motivo, discrepancia);

    public override string ToString() =>
        $"{nameof(IctPrendaResolucion)} {{ Tipo = {Tipo}, Decision = {Decision}, Motivo = {Motivo}, Discrepancia = {Discrepancia} }}";
}
