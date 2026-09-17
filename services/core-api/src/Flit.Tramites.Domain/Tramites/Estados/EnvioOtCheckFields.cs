namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Field keys opcionales que el gestor marca en <see cref="TramiteEstado.Asignado"/> antes de
/// «Enviar al OT» (<see cref="TramiteEstado.Entregado"/>). Valores: <c>true</c>/<c>false</c>.
/// El trigger de inmutabilidad de <c>field_values</c> solo los admite en ese estado.
/// </summary>
public static class EnvioOtCheckFields
{
    public const string SoatPagado = "soat_pagado";
    public const string ImpuestoDepartamentalPagado = "impuesto_departamental_pagado";
}
