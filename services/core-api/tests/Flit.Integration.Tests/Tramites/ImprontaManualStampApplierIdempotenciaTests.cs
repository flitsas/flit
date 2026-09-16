using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #12594 (D1), de punta a punta en la capa de aplicación contra Postgres real: el mismo PDF
/// base (mismo hash) firmado en DOS trámites distintos debe persistir el adjunto sellado y la fila
/// de auditoría en AMBOS, sin que el registro del segundo trámite se descarte por el hash ya visto
/// en el primero (comportamiento del índice único global, hoy reemplazado por
/// <c>(procedure_instance_id, document_hash)</c> — migración 115). Usa los repositorios EF reales
/// (<see cref="ProcedureInstanceRepository"/> internal vía InternalsVisibleTo, igual que
/// <see cref="VehicleSignatureImprintIdempotenciaPorTramiteTests"/>) y un storage/stamper fake
/// deterministas — el arnés no expone un <see cref="IAttachmentStorage"/> real contra el bucket.
/// </summary>
public sealed class ImprontaManualStampApplierIdempotenciaTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid InstanceA = new("a12594b1-0000-4000-8000-000000000001");
    private static readonly Guid InstanceB = new("a12594b1-0000-4000-8000-000000000002");
    private static readonly byte[] SharedPdfBytes = "%PDF-1.4 mismo contenido base compartido"u8.ToArray();

    [PostgresFact]
    public async Task D1_MismoPdfBase_EnDosTramitesDistintos_PersisteAdjuntoYAuditoriaEnAmbos()
    {
        var attachmentIdA = Guid.NewGuid();
        var attachmentIdB = Guid.NewGuid();
        var (tenantId, procedureType) = await SeedTenantAndTraspasoTypeAsync(attachmentIdA, attachmentIdB);

        await using var ctx = NewContext();
        var procRepo = new ProcedureInstanceRepository(ctx);
        var auditRepo = new VehicleSignatureImprintRepository(ctx);
        var storage = new FakeAttachmentStorage();
        var stamper = new DeterministicStamper();

        var instanceA = BuildInstance(InstanceA, tenantId, procedureType);
        var instanceB = BuildInstance(InstanceB, tenantId, procedureType);
        var attachmentA = ImprontaAttachment(attachmentIdA, tenantId, instanceA.Id);
        var attachmentB = ImprontaAttachment(attachmentIdB, tenantId, instanceB.Id);

        var resultA = await ImprontaManualStampApplier.MaybeStampAsync(
            SharedPdfBytes, attachmentA, instanceA, storage, stamper, TestContext.Current.CancellationToken,
            repo: procRepo, auditRepo: auditRepo);
        var resultB = await ImprontaManualStampApplier.MaybeStampAsync(
            SharedPdfBytes, attachmentB, instanceB, storage, stamper, TestContext.Current.CancellationToken,
            repo: procRepo, auditRepo: auditRepo);

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Ambos adjuntos quedaron actualizados con el path/sha del PDF sellado (best-effort en memoria,
        // no requiere que el adjunto esté trackeado por EF: la persistencia del adjunto la hace el
        // llamador real al guardar la instancia completa, fuera del alcance de este caso).
        resultA.Should().NotBeEquivalentTo(SharedPdfBytes, "el stamper determinista sella el PDF");
        resultB.Should().NotBeEquivalentTo(SharedPdfBytes);
        attachmentA.StoragePath.Should().NotBe("path-old");
        attachmentB.StoragePath.Should().NotBe("path-old");
        storage.SavedPaths.Should().HaveCount(2, "cada trámite debe guardar SU propio original sellado");

        // Dos filas de auditoría, una por trámite, con el MISMO document_hash (mismo PDF base).
        var rows = await ctx.VehicleSignatureImprints
            .IgnoreQueryFilters()
            .Where(x => x.ProcedureInstanceId == InstanceA || x.ProcedureInstanceId == InstanceB)
            .ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().HaveCount(2, "el hash es del contenido del PDF; el mismo archivo puede pertenecer a trámites diferentes");
        rows.Select(r => r.DocumentHash).Distinct().Should().ContainSingle(
            "ambas auditorías comparten el mismo hash: es el mismo PDF base");
        rows.Select(r => r.ProcedureInstanceId).Should().BeEquivalentTo([InstanceA, InstanceB]);
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task<(Guid TenantId, ProcedureType Type)> SeedTenantAndTraspasoTypeAsync(
        Guid attachmentIdA, Guid attachmentIdB)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        var type = await ctx.ProcedureTypes.AsNoTracking()
            .SingleAsync(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard);

        var user = new User
        {
            Id = new("a12594b1-0000-4000-8000-0000000000aa"),
            Email = "it-b12594-applier@flit.test",
            DisplayName = "Impronta Applier B12594",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstances.AddRange(
            SeedRow(InstanceA, type.Id, user.Id), SeedRow(InstanceB, type.Id, user.Id));
        await ctx.SaveChangesAsync();

        // El adjunto de impronta debe existir en BD: vehicle_signature_imprints.attachment_id lleva FK
        // a procedure_instance_attachments.id (in-place: mismo adjunto, no uno nuevo).
        ctx.ProcedureInstanceAttachments.AddRange(
            ImprontaAttachment(attachmentIdA, TenantSeed.LoneId, InstanceA),
            ImprontaAttachment(attachmentIdB, TenantSeed.LoneId, InstanceB));
        await ctx.SaveChangesAsync();

        return (TenantSeed.LoneId, type);
    }

    private static ProcedureInstance SeedRow(Guid id, Guid typeId, Guid userId) => new()
    {
        Id = id,
        TenantId = TenantSeed.LoneId,
        ProcedureTypeId = typeId,
        CreatedByUserId = userId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Objeto de dominio en memoria (no trackeado) con Actors/BiometricValidations propios, igual que
    /// <c>ImprontaManualStampApplierTests</c> (Application.Tests) — solo el Id/TenantId/ProcedureTypeId
    /// deben coincidir con la fila sembrada arriba para respetar la FK al insertar la auditoría.
    /// </summary>
    private static ProcedureInstance BuildInstance(Guid id, Guid tenantId, ProcedureType type)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = type.Id,
            ReferenceNumber = $"FLIT-IT-{id.ToString("N")[..8]}",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = type,
            Attachments = [],
        };

        // Se añaden ambas partes (comprador y vendedor) con identidad aprobada+vigente: el gate de
        // ImprontaManualStampReadiness exige una u otra según el gate_profile real sembrado para
        // TRASPASO_STANDARD (RequiresSeller puede variar entre migraciones de seed); cubrir ambas
        // evita acoplar este test de aplicación a ese detalle de configuración.
        foreach (var rol in new[] { "comprador", "vendedor" })
        {
            var documento = $"IT-{rol[..3]}-{id.ToString("N")[..6]}";
            instance.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                ProcedureEntityId = Guid.NewGuid(),
                ActorType = rol,
                DocumentType = "CC",
                DocumentNumber = documento,
                FullName = $"{rol} Integración",
                Ordinal = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = rol,
                DocumentType = "CC",
                DocumentNumber = documento,
                Name = $"{rol} Integración",
                Status = BiometricEstados.Aprobado,
                CreatedAt = DateTimeOffset.UtcNow,
                ValidatedAt = DateTimeOffset.UtcNow,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(10),
            });
        }

        return instance;
    }

    private static ProcedureInstanceAttachment ImprontaAttachment(Guid id, Guid tenantId, Guid instanceId) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureInstanceId = instanceId,
        Tipo = "impronta",
        Filename = "impronta.pdf",
        Mimetype = "application/pdf",
        SizeBytes = SharedPdfBytes.Length,
        Sha256 = "shared-original-sha",
        StoragePath = "path-old",
        Source = "user",
        UploadedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Storage en memoria: cada SaveAsync produce un path único, sin tocar disco/S3.</summary>
    private sealed class FakeAttachmentStorage : IAttachmentStorage
    {
        public List<string> SavedPaths { get; } = [];

        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default)
        {
            var path = $"it/{procedureInstanceId}/{Guid.NewGuid()}.pdf";
            SavedPaths.Add(path);
            return Task.FromResult(new StoredFile(path, $"sha-{SavedPaths.Count}", content.Length));
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    /// <summary>Stamper determinista: el hash es el SHA-256 del PDF de entrada (mismos bytes ⇒ mismo hash).</summary>
    private sealed class DeterministicStamper : IImprontaManualStamper
    {
        public bool AlreadyStamped(byte[] pdf) => false;

        public ImprontaManualStampResult Stamp(byte[] pdf, ImprontaManualStampContext context)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf)).ToLowerInvariant();
            var stamped = pdf.Concat("-sellado"u8.ToArray()).ToArray();
            return new ImprontaManualStampResult(
                stamped,
                Applied: true,
                DocumentHash: hash,
                SignatureBase64: "c2ln",
                PrivateKeyPem: "-----BEGIN PRIVATE KEY----- it -----END PRIVATE KEY-----",
                PublicKeyPem: "-----BEGIN PUBLIC KEY----- it -----END PUBLIC KEY-----",
                SignedAt: DateTimeOffset.UtcNow);
        }
    }
}
