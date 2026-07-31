namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Fila de política de bloqueo de un tenant para un Organismo de Tránsito puntual (FEATURE 05).
/// La tabla es dispersa: solo existen filas para los pares (tenant, OT, criterio) que el admin
/// tocó explícitamente; ausencia de fila = default del criterio (definido en Trámites, por
/// criterio, para preservar el comportamiento previo).
/// </summary>
public sealed record OtBlockingPolicyItem(
    Guid TransitOfficeId,
    string Criterion,
    bool Blocks);
