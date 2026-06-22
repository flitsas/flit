namespace Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;

/// <summary>
/// Payload unificado de alta de un override de orden documental (HU #10196, AC1/AC2).
/// El <c>scope</c> viaja como query (no en el body) y determina qué referencia se usa:
/// <c>transitOfficeId</c> (scope OT) o <c>clienteId</c> (scope CLIENTE).
/// </summary>
/// <param name="ProcedureTypeId">Tipo de trámite (debe existir).</param>
/// <param name="DocumentTypeId">Tipo de documento (debe existir y estar asociado al trámite).</param>
/// <param name="TransitOfficeId">Organismo de tránsito (obligatorio si scope=OT).</param>
/// <param name="ClienteId">Cliente/tenant (obligatorio si scope=CLIENTE).</param>
/// <param name="Orden">Orden personalizado (smallint ≥ 0).</param>
public sealed record CreateDocumentOrderOverrideRequest(
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    Guid? TransitOfficeId,
    Guid? ClienteId,
    int? Orden);
