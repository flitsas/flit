using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Normaliza una fila del payload v1 a las entidades del pre-trámite (master + actores),
/// aplanando seller/buyer/lessee a <c>external_integration_actors</c>. Lógica pura (testeable).
/// </summary>
public static class IctPayloadNormalizer
{
    public static ExternalIntegrationMaster ToMaster(RegisterRowInput row, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(row);

        var managerIdTransaction = string.IsNullOrWhiteSpace(row.ManagerIdTransaction)
            ? Guid.NewGuid().ToString("N")[..20]
            : row.ManagerIdTransaction.Trim();

        var master = new ExternalIntegrationMaster
        {
            TenantId = tenantId,
            CompanyManagerDocument = row.CompanyManagerDocument?.Trim() ?? string.Empty,
            ManagerUser = row.ManagerUser?.Trim() ?? string.Empty,
            ManagerMail = row.ManagerMail?.Trim() ?? string.Empty,
            DeliveryAddress = row.DeliveryAddress?.Trim() ?? string.Empty,
            ManagerIdTransaction = managerIdTransaction,
            TransactionOperation = row.TransactionOperation,
            TransactionType = row.TransactionType,
            Plate = row.Plate?.Trim().ToUpperInvariant() ?? string.Empty,
            Vin = row.Vin?.Trim().ToUpperInvariant(),
            TrafficSecretaryCode = row.TrafficSecretaryCode?.Trim() ?? string.Empty,
            SellingDate = row.SellingDate?.Trim() ?? string.Empty,
            SellingPrice = row.SellingPrice ?? 0m,
            UrlWebHook = row.UrlWebHook?.Trim() ?? string.Empty,
            ProcessWithoutAttachedDocuments = row.ProcessWithoutAttachedDocuments,
            ProcessStatusId = 1,
            BusinessValidation = 0,
            ExternalValidation = 0,
        };

        AddActors(master, tenantId, row.Seller, "seller");
        AddActors(master, tenantId, row.Buyer, "buyer");
        AddActors(master, tenantId, row.Lessee, "lessee");
        return master;
    }

    /// <summary>Clave de deduplicación intra-lote por tipo de trámite (v1 findDuplicatesInBatch, extendida a 5-16).</summary>
    public static string DedupKey(RegisterRowInput row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var plate = row.Plate?.Trim().ToUpperInvariant() ?? string.Empty;
        var vin = row.Vin?.Trim().ToUpperInvariant() ?? string.Empty;
        return row.TransactionType switch
        {
            1 or 2 => $"vin:{vin}",
            3 or 4 => $"plate:{plate}",
            _ => $"plate+type:{plate}:{row.TransactionType}",
        };
    }

    private static void AddActors(
        ExternalIntegrationMaster master,
        Guid tenantId,
        IReadOnlyList<RegisterActorInput>? actors,
        string actorType)
    {
        if (actors is null)
        {
            return;
        }

        foreach (var a in actors)
        {
            master.Actors.Add(new ExternalIntegrationActor
            {
                TenantId = tenantId,
                ActorType = actorType,
                DocumentType = a.DocumentType?.Trim() ?? string.Empty,
                DocumentNumber = a.DocumentNumber?.Trim() ?? string.Empty,
                Name = a.Name?.Trim() ?? string.Empty,
                FirstLastName = a.FirstLastName?.Trim() ?? string.Empty,
                SecondLastName = a.SecondLastName?.Trim(),
                Phone = a.Phone?.Trim() ?? string.Empty,
                Email = a.Email?.Trim() ?? string.Empty,
                City = a.City?.Trim(),
                State = a.State?.Trim(),
                Address = a.Address?.Trim(),
                ExpeditionDate = a.ExpeditionDate?.Trim(),
            });
        }
    }
}
