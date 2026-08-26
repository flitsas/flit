using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Detalle del bloqueo «no hay prenda que levantar»: viaja del preflight al endpoint, que lo traduce
/// a las extensions RFC7807 del 422 <see cref="VehiclePrendaPolicy.ErrorCode"/>.
/// </summary>
public sealed record VehiclePrendaBlock(string ProcedureType);

/// <summary>
/// Precondición registral del levantamiento de prenda: no se puede levantar un gravamen que el RUNT
/// no reporta.
///
/// <para><b>Por qué es un bloqueo.</b> El trámite consiste en extinguir un gravamen existente, y el
/// FUR lo declara como tal: marca la casilla 12 y escribe en el numeral 20 «A FAVOR DE» el acreedor
/// del gravamen que se levanta. Sobre un vehículo sin prenda no hay acreedor que nombrar ni acto que
/// soportar: el formulario saldría marcado y mudo, y el organismo devuelve el expediente. Además el
/// acreedor lo PRECARGA el propio RUNT desde el gravamen reportado — sin gravamen, el gestor tendría
/// que inventarlo.</para>
///
/// <para><b>Lo que NO bloquea.</b> Que el RUNT no traiga información de gravámenes
/// (<c>unknown</c>). Ahí no se sabe si el vehículo tiene prenda o no, y convertir un dato ausente en
/// un trámite imposible de radicar sería castigar al gestor por una falla ajena. Mismo criterio
/// deliberado que <see cref="VehicleBodyTypePolicy"/>.</para>
/// </summary>
public static class VehiclePrendaPolicy
{
    /// <summary>Código de error 422: el vehículo no tiene prenda que levantar.</summary>
    public const string ErrorCode = "VEHICLE_PRENDA_MISSING";

    /// <summary><c>procedureType</c> del detalle RFC7807.</summary>
    public const string ProcedureTypeLevantamiento = "levantamiento_prenda";

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
    /// Resuelve el bloqueo, o <c>null</c> si el trámite puede seguir.
    /// </summary>
    /// <param name="procedureTypeCode">Código del tipo de trámite de la instancia.</param>
    /// <param name="gravamenCheckStatus">
    /// Estado del check <c>gravamenes</c> del semáforo; <c>null</c> si el check no llegó a emitirse.
    /// </param>
    public static VehiclePrendaBlock? Evaluar(string? procedureTypeCode, string? gravamenCheckStatus)
    {
        if (!ProcedureTypeLayers.ExigePrendaPreviaEnRunt(procedureTypeCode))
            return null;
        if (!RuntAfirmaSinGravamen(gravamenCheckStatus))
            return null;

        return new VehiclePrendaBlock(ProcedureTypeLevantamiento);
    }
}
