namespace Flit.Tramites.Domain.ReadModels;

/// <summary>
/// Trámite del tenant vinculado a una identidad (tipo + número de documento) compartida entre
/// validaciones biométricas. Feature #11066.
/// </summary>
public sealed record LinkedProcedureSummary(
    Guid InstanceId,
    string ReferenceNumber,
    string Status);
