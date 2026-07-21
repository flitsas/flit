using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence.Entities.Admin;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Traducción entre el <see cref="SignatureVaultEstado"/> del dominio y su representación en BD
/// ('activa'|'revocada', ADR-0025 §2), y rehidratación del agregado desde su fila EF. Aislar el
/// mapeo aquí evita esparcir literales de estado por los repositorios.
/// </summary>
internal static class SignatureVaultEstadoMapping
{
    public const string Activa = "activa";
    public const string Revocada = "revocada";

    public static string ToDb(SignatureVaultEstado estado) => estado switch
    {
        SignatureVaultEstado.Activa => Activa,
        SignatureVaultEstado.Revocada => Revocada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado de baúl no soportado."),
    };

    public static SignatureVaultEstado FromDb(string estado) => estado switch
    {
        Activa => SignatureVaultEstado.Activa,
        Revocada => SignatureVaultEstado.Revocada,
        // Defensivo: cualquier valor no reconocido se trata como no consumible.
        _ => SignatureVaultEstado.Revocada,
    };

    public static SignatureVault Rehydrate(SignatureVaultEntity entity) =>
        SignatureVault.Rehydrate(
            entity.Id,
            entity.TenantId,
            entity.DocumentType,
            entity.DocumentNumber,
            entity.NitEmpresa,
            entity.FullName,
            entity.SignatureHash,
            entity.StoragePath,
            entity.StorageSha256,
            FromDb(entity.Estado),
            entity.VigenciaDesde,
            entity.VigenciaHasta,
            entity.MandateSignerId);

    public static SignatureVaultItem ToItem(SignatureVaultEntity entity) =>
        new()
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            DocumentType = entity.DocumentType,
            DocumentNumber = entity.DocumentNumber,
            NitEmpresa = entity.NitEmpresa,
            FullName = entity.FullName,
            SignatureHash = entity.SignatureHash,
            StoragePath = entity.StoragePath,
            StorageSha256 = entity.StorageSha256,
            Estado = FromDb(entity.Estado),
            VigenciaDesde = entity.VigenciaDesde,
            VigenciaHasta = entity.VigenciaHasta,
            MandateSignerId = entity.MandateSignerId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
}
