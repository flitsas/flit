namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Trámite existente mínimo para el bloqueo CF-01 de duplicidad ACTIVA por PLACA (familia Traspaso,
/// HU #10876). Paridad estructural con <see cref="VinTramiteExistente"/> (invariante VIN de HU
/// #10538), pero deliberadamente SEPARADO: este solo alimenta
/// <see cref="Services.DuplicateActiveProcedurePolicy"/> (bloqueo duro 409), no el check informativo
/// de matrícula ya registrada.
/// </summary>
public sealed record PlacaTramiteExistente(
    Guid Id,
    string Estado,
    string Placa,
    string? Vin = null,
    DateTimeOffset? FechaRegistro = null);
