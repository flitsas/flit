namespace Flit.Ict.Domain.Abstractions;

/// <summary>Un ítem del histórico de estados (forma v1, compatibilidad de clientes antiguos).</summary>
public sealed record StatusProcessV1Item(
    string Status,
    string StatusDescription,
    string Observation,
    string UserMailRegistered,
    string UserRoleRegistered,
    string DateRegistered,
    string CompanyRegistered);

/// <summary>
/// Respuesta del endpoint de estado en la forma EXACTA de v1 (para no romper clientes existentes):
/// mismos nombres de campo, códigos numéricos de estado y arreglo <c>statusProcess</c> con el histórico.
/// </summary>
public sealed record StatusProcessV1Response(
    string TransactionFlit,
    string StatusValidation,
    string StatusDescription,
    string MessageValidation,
    string StatusObservation,
    string Status,
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
