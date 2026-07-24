namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Un ítem del histórico de estados (forma v1, compatibilidad de clientes antiguos). Los nombres de
/// campo y su casing (camelCase al serializar) calcan la respuesta real de v1: <c>status</c> numérico,
/// <c>userNameRegistered</c> incluido (v1 lo emitía desde status_process_userregistered).
/// </summary>
public sealed record StatusProcessV1Item(
    int Status,
    string StatusDescription,
    string Observation,
    string UserMailRegistered,
    string UserRoleRegistered,
    string UserNameRegistered,
    string DateRegistered,
    string CompanyRegistered);

/// <summary>
/// Respuesta del endpoint de estado en la forma EXACTA de v1 (para no romper clientes existentes):
/// mismos nombres de campo, códigos numéricos de estado (v1 emite <c>statusValidation</c>/<c>status</c>
/// como número, no como cadena) y arreglo <c>statusProcess</c> con el histórico.
/// </summary>
public sealed record StatusProcessV1Response(
    string TransactionFlit,
    int StatusValidation,
    string StatusDescription,
    string MessageValidation,
    string StatusObservation,
    int Status,
    string StatusMessage,
    IReadOnlyList<StatusProcessV1Item> StatusProcess);

/// <summary>Consulta del estado de un trámite en la forma v1, por su manager_id_transaction (TransactionFlit).</summary>
public interface IStatusProcessV1Query
{
    Task<StatusProcessV1Response?> GetByManagerIdTransactionAsync(
        string managerIdTransaction,
        Guid tenantId,
        CancellationToken ct = default);
}
