using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Precondición registral del levantamiento de prenda: el RUNT no reporta el gravamen que se
/// pretende levantar.
///
/// <para><b>Por qué NO bloquea (HU #12131/#12129 — corrección de un bloqueo duro previo,
/// ADR-0050).</b> La lectura original era que, sin gravamen, el FUR saldría "mudo" (sin acreedor que
/// nombrar) y el organismo devolvería el expediente — pero el criterio de negocio es explícito: esta
/// validación NUNCA debe cortar la radicación, solo informar y dejar que el gestor capture el
/// acreedor/entidad manualmente (mismo mecanismo que ya existe para el caso <c>unknown</c>, ver
/// abajo). Por eso <see cref="Evaluar"/> es un detector puro (<c>bool</c>), no algo que un endpoint
/// pueda traducir a un 422 — a diferencia de <see cref="VehicleBodyTypePolicy"/> (que sí sigue siendo
/// un bloqueo duro legítimo: sin carrocería previa no hay OTRO tipo de trámite al que redirigir al
/// gestor). Esta regla es de negocio, no de ambiente: no se resuelve con
/// <c>TramiteValidationPolicy</c>/<c>.env</c> — el código nunca ofrece la opción de bloquear.</para>
///
/// <para><b>Qué SÍ hace</b>: cuando el RUNT confirma la ausencia (<c>ok</c>), se agrega un check
/// informativo (<c>warn</c>) al semáforo del preflight, y el paso de captura de prenda del asistente
/// (<c>PrendaForm</c>) avisa con un modal no bloqueante y habilita la captura manual.</para>
///
/// <para><b>Tampoco avisa</b> cuando el RUNT no trae información de gravámenes (<c>unknown</c>): ahí
/// no se sabe si el vehículo tiene prenda o no, y no hay nada nuevo que informar sobre esa
/// incertidumbre aquí (la ausencia de dato ya es visible en el propio check <c>gravamenes</c>).</para>
/// </summary>
public static class VehiclePrendaPolicy
{
    /// <summary>Clave del check del semáforo que reporta gravámenes y prendas.</summary>
    public const string GravamenCheckKey = "gravamenes";

    /// <summary>
    /// ¿El RUNT afirma que el vehículo NO tiene gravamen? Solo <c>ok</c> lo afirma: es el estado que
    /// el proveedor emite cuando respondió y ni gravámenes ni prendas están en «SI».
    /// <c>warn</c>/<c>fail</c> reportan gravamen, y <c>unknown</c> (o ausencia del check) significa
    /// que no hay dato — ninguno de los tres afirma la ausencia.
    /// </summary>
    public static bool RuntAfirmaSinGravamen(string? gravamenCheckStatus) =>
        string.Equals(gravamenCheckStatus?.Trim(), "ok", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿El tipo de trámite exige gravamen previo (levantamiento) Y el RUNT confirma que no lo hay?
    /// <c>true</c> significa "avisa" (check <c>warn</c> + modal informativo), nunca "bloquea": no
    /// existe una variante de esta función que devuelva un código de error.
    /// </summary>
    /// <param name="procedureTypeCode">Código del tipo de trámite de la instancia.</param>
    /// <param name="gravamenCheckStatus">
    /// Estado del check <c>gravamenes</c> del semáforo; <c>null</c> si el check no llegó a emitirse.
    /// </param>
    public static bool Evaluar(string? procedureTypeCode, string? gravamenCheckStatus) =>
        ProcedureTypeLayers.ExigePrendaPreviaEnRunt(procedureTypeCode)
        && RuntAfirmaSinGravamen(gravamenCheckStatus);
}
