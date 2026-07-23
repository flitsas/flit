namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Señales estables que el guardado de un representante legal puede emitir hacia el FE (HU #10901).
/// Son distintas de un error 422: el guardado es exitoso, pero se avisa una condición que el FE debe
/// resolver (p. ej. ofrecer "enviar correo de validación" o "registrar firma en el baúl").
/// </summary>
public static class LegalRepresentativeSignals
{
    /// <summary>
    /// Al guardar no había firma del baúl ni validación de identidad vigente para el representante:
    /// el registro queda sin firma/identidad vinculada (ADR-0033 §5.1, D2).
    /// </summary>
    public const string SinFirmaNiIdentidad = "sin_firma_ni_identidad";
}
