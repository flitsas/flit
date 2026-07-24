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

            // Banderas de comportamiento del contrato v1.
            Priority = row.Priority,
            TransactionFlit = Clean(row.TransactionFlit),
            StartsProcedureInPaused = row.StartsProcedureInPaused,
            ObservationWhenPaused = Clean(row.ObservationWhenPaused),
            SendAutomaticTrafficSecretary = row.SendAutomaticTrafficSecretary,
            PlateAssignmentType = row.PlateAssignmentType,

            // Compañía relacionada (servicio público).
            RelatedCompanyDocument = Clean(row.RelatedCompanyDocument),
            RelatedCompanyName = Clean(row.RelatedCompanyName),

            // Limitación / garantía mobiliaria (prenda).
            LimitationsOperationType = row.LimitationsOperationType,
            LimitationsCreditor = Clean(row.LimitationsCreditor),
            LimitationsCreditorDocumentType = Clean(row.LimitationsCreditorDocumentType),
            LimitationsCreditorDocumentNumber = Clean(row.LimitationsCreditorDocumentNumber),
            LimitationsInscriptionDate = Clean(row.LimitationsInscriptionDate),

            // Otros trámites.
            ArmorLevelNumberId = row.ArmorLevelNumberId,
            NewVehicleFuelType = row.NewVehicleFuelType,

            ProcessStatusId = 1,
            BusinessValidation = 0,
            ExternalValidation = 0,
        };

        AddActors(master, tenantId, row.Seller, "seller");
        AddActors(master, tenantId, row.Buyer, "buyer");
        AddActors(master, tenantId, row.Lessee, "lessee");
        AddTransformations(master, tenantId, row.MoreTransactionTransactionType);
        return master;
    }

    /// <summary>Códigos RUNT de transformación del catálogo (12-ICT-catalogs-parity.sql).</summary>
    private static readonly int[] KnownTransformationCodes = [5, 9, 17];

    /// <summary>
    /// Aplana <c>more_transaction_transaction_type</c> al puente master↔transformación. Se ignoran
    /// los códigos fuera del catálogo (la FK los rechazaría y tumbaría el registro completo) y los
    /// repetidos (la PK es compuesta por master + código).
    /// TODO(ICT-TRANSFORM-VALIDATION): v1 validaba el código en el SP de negocio como NOVEDAD
    /// (business.sql reglas 85/86); aquí se descartan en silencio hasta portar esas reglas.
    /// </summary>
    private static void AddTransformations(
        ExternalIntegrationMaster master,
        Guid tenantId,
        IReadOnlyList<RegisterTransformationInput>? transformations)
    {
        if (transformations is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var t in transformations)
        {
            if (!KnownTransformationCodes.Contains(t.TransactionType) || !seen.Add(t.TransactionType))
            {
                continue;
            }

            master.Transformations.Add(new ExternalIntegrationMasterTransformation
            {
                TenantId = tenantId,
                IdTransformationType = t.TransactionType,
                Description = t.Description?.Trim() ?? string.Empty,
            });
        }
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
            var actor = new ExternalIntegrationActor
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
            };

            // Representante legal y mandante del actor (contrato v1): se aplanan en la misma fila,
            // que ya tiene las columnas legal_representative_* / principal_mandante_*.
            var rl = a.LegalRepresentative;
            if (rl is not null)
            {
                actor.LegalRepresentativeDocumentType = Clean(rl.DocumentType);
                actor.LegalRepresentativeDocumentNumber = Clean(rl.DocumentNumber);
                actor.LegalRepresentativeName = Clean(rl.Name);
                actor.LegalRepresentativeFirstLastName = Clean(rl.FirstLastName);
                actor.LegalRepresentativeSecondLastName = Clean(rl.SecondLastName);
                actor.LegalRepresentativePhone = Clean(rl.Phone);
                actor.LegalRepresentativeEmail = Clean(rl.Email);
                actor.LegalRepresentativeCity = Clean(rl.City);
                actor.LegalRepresentativeState = Clean(rl.State);
                actor.LegalRepresentativeAddress = Clean(rl.Address);
            }

            var pm = a.PrincipalMandante;
            if (pm is not null)
            {
                actor.PrincipalMandanteDocumentType = Clean(pm.DocumentType);
                actor.PrincipalMandanteDocumentNumber = Clean(pm.DocumentNumber);
                actor.PrincipalMandanteName = Clean(pm.Name);
                actor.PrincipalMandanteFirstLastName = Clean(pm.FirstLastName);
                actor.PrincipalMandanteSecondLastName = Clean(pm.SecondLastName);
                actor.PrincipalMandanteEmail = Clean(pm.Email);
            }

            master.Actors.Add(actor);
        }
    }

    /// <summary>Normaliza un opcional del payload: recorta y convierte vacío en null (no se persiste ruido).</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
