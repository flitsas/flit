namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Épica #13216 (HU #13370) — referencia ligera de un trámite: lo justo para congelar un ítem del lote de
/// consolidados (id, compañía dueña, radicado y placa para el nombre del PDF), sin cargar el grafo.
/// </summary>
public sealed record ProcedureInstanceRef(Guid Id, Guid TenantId, string ReferenceNumber, string? Plate);
