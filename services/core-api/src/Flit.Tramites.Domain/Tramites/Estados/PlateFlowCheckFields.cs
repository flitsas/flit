namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Field keys opcionales que el gestor marca en sub-estado <see cref="PlateFlowStatus.Asignado"/>
/// antes de pasar a <see cref="PlateFlowStatus.Terminado"/>. Valores: <c>true</c>/<c>false</c>.
/// </summary>
public static class PlateFlowCheckFields
{
    public const string SoatPagado = "soat_pagado";
    public const string ImpuestoDepartamentalPagado = "impuesto_departamental_pagado";
}
