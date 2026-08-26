using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>Atributo del vehículo que un tipo de trámite cambia POR SÍ MISMO (su capa base).</summary>
public enum TransformacionBase
{
    /// <summary>El tipo no cambia ningún atributo del vehículo.</summary>
    Ninguna,
    Color,
    Carroceria,
    Combustible,
    Blindaje,
}

/// <summary>
/// Qué capas del expediente le pertenecen al TIPO y cuáles son complementarias (art. 5.1.8).
///
/// <para><b>El problema que resuelve.</b> Hasta ADR-0050 solo existían dos recorridos, y los dos
/// admitían acumular trámites sobre el mismo vehículo: una matrícula o un traspaso podían llevar
/// encima una prenda y las transformaciones que hicieran falta. La familia OTROS no funciona así:
/// ahí el cambio ES el trámite. Un <c>CAMBIO_COLOR</c> con una prenda y un blindaje «por encima» no
/// es un cambio de color con extras — son tres trámites distintos que el organismo devuelve.</para>
///
/// <para>Este tipo es la fuente única de esa distinción, compartida por el asistente (qué se pinta),
/// el PATCH de <c>field_values</c> (qué se acepta), la decisión de prenda y el FUR/mandato (qué
/// capas se unen). Las dos preguntas que responde son distintas y no hay que confundirlas:</para>
/// <list type="bullet">
/// <item><see cref="EsTipoPrendaBase"/>: la prenda ES el trámite (no un gravamen añadido).</item>
/// <item><see cref="TransformacionDelTipo"/>: el atributo que el tipo cambia por definición.</item>
/// </list>
/// Fuente normativa de los códigos: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c> (tabla 1).
/// </summary>
public static class ProcedureTypeLayers
{
    /// <summary>
    /// La decisión de prenda es el trámite mismo, no un gravamen complementario.
    ///
    /// <para><c>CAMBIO_ACREEDOR</c> entra aquí aunque su casilla del numeral 3 sea la 18 y no la
    /// 11/12: la pregunta es de quién es la capa, no qué casilla marca. Sustituir un acreedor exige
    /// capturar el gravamen, así que el paso de prenda le pertenece al tipo.</para>
    /// </summary>
    public static bool EsTipoPrendaBase(string? code) => Norm(code) switch
    {
        "PRENDA_INSCRIPCION" or "LEVANTAMIENTO_PRENDA" or "LEVANTAR_INSCRIBIR_PRENDA" or "CAMBIO_ACREEDOR" => true,
        _ => false,
    };

    /// <summary>
    /// Tipos prendarios de UNA sola acción sobre el gravamen: inscribirlo o levantarlo.
    ///
    /// <para>Al ser una sola, la decisión la fija el tipo y el asistente no ofrece elegir: afirma lo
    /// que va a pasar y pide únicamente lo que hay que capturar. Eso permite además que el gate
    /// exija TODO lo que esa acción necesita para el formulario —incluido el acreedor de un
    /// levantamiento, que llena el numeral 20 «A FAVOR DE»—, cosa que no se puede hacer donde la
    /// decisión es una elección del gestor entre varias.</para>
    ///
    /// <para><b>Por qué no son los cuatro de <see cref="EsTipoPrendaBase"/>.</b>
    /// <c>LEVANTAR_INSCRIBIR_PRENDA</c> y <c>CAMBIO_ACREEDOR</c> ejecutan DOS acciones en el mismo
    /// trámite (se levanta un gravamen y se inscribe otro), y el expediente guarda una sola decisión
    /// de prenda: cómo se capturan esas dos caras está sin resolver. Ambos están inactivos en el
    /// catálogo, así que quedan fuera hasta que se decida, en vez de forzarlos a un modelo de una
    /// sola acción que no los describe.</para>
    /// </summary>
    public static bool EsPrendaDeAccionUnica(string? code) => Norm(code) switch
    {
        "PRENDA_INSCRIPCION" or "LEVANTAMIENTO_PRENDA" => true,
        _ => false,
    };

    /// <summary>
    /// El trámite actúa sobre un gravamen que YA existe, así que exige que el RUNT lo reporte: sin
    /// prenda inscrita no hay nada que levantar. <c>PRENDA_INSCRIPCION</c> no entra —constituye el
    /// gravamen, no lo presupone— y puede inscribirse aunque el vehículo ya tenga otro.
    /// </summary>
    public static bool ExigePrendaPreviaEnRunt(string? code) =>
        Norm(code) == "LEVANTAMIENTO_PRENDA";

    /// <summary>Atributo que el tipo cambia por definición; <see cref="TransformacionBase.Ninguna"/> si no cambia ninguno.</summary>
    public static TransformacionBase TransformacionDelTipo(string? code) => Norm(code) switch
    {
        "CAMBIO_COLOR" => TransformacionBase.Color,
        "CAMBIO_CARROCERIA" => TransformacionBase.Carroceria,
        "CONVERSION_COMBUSTIBLE" => TransformacionBase.Combustible,
        "BLINDAJE" => TransformacionBase.Blindaje,
        _ => TransformacionBase.Ninguna,
    };

    /// <summary>
    /// ¿Este trámite tiene que resolver una prenda? Fuente ÚNICA de la regla, para que el gate del
    /// servidor y las opciones que pinta el asistente no puedan discrepar.
    ///
    /// <para>Se exige decisión si el trámite <b>ES</b> de prenda —inscribir, levantar, cambiar de
    /// acreedor— <b>o</b> si el RUNT reportó un gravamen sobre el vehículo.</para>
    ///
    /// <para><b>Por qué no una marca del tipo.</b> Antes esto lo decía <c>gate_profile.hasPrendaGate</c>,
    /// y esa marca tenía tres defectos, los tres observados: preguntaba por una prenda inexistente en
    /// todo traspaso sin gravamen; había que acordarse de ponerla —quedó ausente en los ocho tipos de
    /// matrícula y traspaso, con R10 desactivada sin que nada fallara—; y al vivir en el tipo se
    /// CONGELA en el snapshot del expediente, así que corregirla no alcanzaba a los trámites ya
    /// abiertos. Los dos disparadores de aquí no se olvidan ni se congelan: uno es el código del tipo
    /// (lo que el trámite ES) y el otro es dato vivo de la instancia.</para>
    ///
    /// <para>La inscripción de prenda entra por el primer disparador y no por el segundo: crea un
    /// gravamen que todavía no existe, así que el RUNT no reporta nada.</para>
    /// </summary>
    public static bool ExigeDecisionDePrenda(string? code, bool runtReportaGravamen) =>
        EsTipoPrendaBase(code) || runtReportaGravamen;

    /// <summary><c>true</c> si la familia acumula trámites complementarios sobre el tipo base.</summary>
    /// <remarks>
    /// Una familia desconocida o ausente se trata como acumulable: es lo que hacían matrícula y
    /// traspaso antes de este cambio, y degradar a «no acumula» apagaría en silencio la prenda y las
    /// transformaciones de un expediente en curso cuyo tipo llegara sin clasificar.
    /// </remarks>
    public static bool FamiliaAcumulaComplementarios(string? familyCode) =>
        ProcedureFamilyCodes.FromCode(familyCode) != ProcedureFamily.Otros;

    private static string Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToUpperInvariant();
}
