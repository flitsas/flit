namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Épica #13216 (HU #13390) — referencia ligera de un trámite de la bandeja del OT: lo justo para congelar
/// un ítem del lote de consolidados. <see cref="ClientTenantId"/> es la compañía CLIENTE dueña del trámite,
/// nunca el tenant del OT.
/// </summary>
public sealed record OtClientProcedureRef(Guid Id, Guid ClientTenantId, string ReferenceNumber, string? Plate);
