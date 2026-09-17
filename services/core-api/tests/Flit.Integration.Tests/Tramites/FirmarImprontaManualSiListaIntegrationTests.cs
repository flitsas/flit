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
/// HU #12116, de punta a punta contra Postgres real: un trámite <c>entregado</c> con impronta
/// manual (placa asignada + identidad vigente de la parte requerida) queda con una fila en
/// <c>vehicle_signature_imprints</c> tras <see cref="FirmarImprontaManualSiListaHandler"/>, SIN pasar
/// por <c>GenerarConsolidadoHandler</c> (el caso que reporta el usuario: nadie generó el consolidado).
/// </summary>
public sealed class FirmarImprontaManualSiListaIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid InstanceId = new("a12116b1-0000-4000-8000-000000000001");
    private static readonly byte[] RawPdfBytes = "%PDF-1.4 impronta original"u8.ToArray();

    [PostgresFact]
    public async Task HandleAsync_TramiteEntregadoConImprontaYPlaca_PersisteFilaDeAuditoria_SinConsolidado()
    {
        var attachmentId = Guid.NewGuid();
        var (tenantId, procedureType) = await SeedTenantAndTypeAsync(attachmentId);

        await using var ctx = NewContext();
        var procRepo = new ProcedureInstanceRepository(ctx);
        var auditRepo = new VehicleSignatureImprintRepository(ctx);
        var storage = new IdentityAttachmentStorage(RawPdfBytes);
        var stamper = new DeterministicStamper();
        var merger = new PassthroughMerger();

        var handler = new FirmarImprontaManualSiListaHandler(
            procRepo, merger, storage, stamper, auditRepo: auditRepo);

        var resultado = await handler.HandleAsync(
            InstanceId, tenantId, FirmaImprontaAutomaticaOrigen.Radicacion,
            TestContext.Current.CancellationToken);

        resultado.Estado.Should().Be(FirmaImprontaAutomaticaEstado.Firmada);

        var rows = await ctx.VehicleSignatureImprints
            .IgnoreQueryFilters()
            .Where(x => x.ProcedureInstanceId == InstanceId)
            .ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().ContainSingle();
        rows[0].AttachmentId.Should().NotBeNull();

        var consolidados = await ctx.ProcedureInstanceAttachments
            .Where(a => a.ProcedureInstanceId == InstanceId && a.Tipo == "consolidado")
            .ToListAsync(TestContext.Current.CancellationToken);
        consolidados.Should().BeEmpty("la firma automática no genera el consolidado");
    }

    [PostgresFact]
    public async Task ListImprontaManualBackfillCandidatesAsync_ExcluyeElTramiteYaFirmado()
    {
        var attachmentId = Guid.NewGuid();
        var (tenantId, _) = await SeedTenantAndTypeAsync(attachmentId);

        await using (var seedCtx = NewContext())
        {
            seedCtx.VehicleSignatureImprints.Add(new VehicleSignatureImprint
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = InstanceId,
                AttachmentId = attachmentId,
                ModuleCode = "traspaso",
                PrivateKey = "pk",
                PublicKey = "pub",
                DocumentHash = "hash-ya-firmado",
                Signature = "sig",
                SignedAt = DateTimeOffset.UtcNow,
                SignedStoragePath = "path-firmado",
                SignedSha256 = "sha",
                SignedSizeBytes = 10,
                SignedFilename = "impronta.pdf",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seedCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext();
        var procRepo = new ProcedureInstanceRepository(ctx);

        var candidatos = await procRepo.ListImprontaManualBackfillCandidatesAsync(
            200, TestContext.Current.CancellationToken);

        candidatos.Should().NotContain(c => c.InstanceId == InstanceId);
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task<(Guid TenantId, ProcedureType Type)> SeedTenantAndTypeAsync(Guid attachmentId)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        var type = await ctx.ProcedureTypes.AsNoTracking()
            .SingleAsync(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard);

        var user = new User
        {
            Id = new("a12116b1-0000-4000-8000-0000000000aa"),
            Email = "it-hu12116-firma@flit.test",
            DisplayName = "Firma Automática Impronta",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = type.Id,
            CreatedByUserId = user.Id,
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstanceAttachments.Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId,
            TenantId = TenantSeed.LoneId,
            ProcedureInstanceId = InstanceId,
            Tipo = "impronta",
            Filename = "impronta.pdf",
            Mimetype = "application/pdf",
            SizeBytes = RawPdfBytes.Length,
            Sha256 = "original-sha",
            StoragePath = "path-old",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        // Catálogo global (sin tenant_id, sembrado por 04-HU10151-seeds-minimos.sql): BUYER = lado
        // comprador, OWNER = lado vendedor/propietario actual.
        var buyerEntityId = await ctx.ProcedureEntities.AsNoTracking()
            .Where(e => e.Code == "BUYER").Select(e => e.Id).SingleAsync();
        var ownerEntityId = await ctx.ProcedureEntities.AsNoTracking()
            .Where(e => e.Code == "OWNER").Select(e => e.Id).SingleAsync();

        // Ambas partes (comprador y vendedor) con identidad aprobada+vigente: el gate_profile real
        // sembrado para TRASPASO_STANDARD puede variar entre migraciones (RequiresSeller), igual que
        // en ImprontaManualStampApplierIdempotenciaTests — cubrir ambas evita acoplar el test a ese
        // detalle de configuración.
        foreach (var rol in new[] { "comprador", "vendedor" })
        {
            var documento = $"IT-HU12116-{rol[..3]}";
            ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = TenantSeed.LoneId,
                ProcedureInstanceId = InstanceId,
                ProcedureEntityId = rol == "comprador" ? buyerEntityId : ownerEntityId,
                ActorType = rol,
                DocumentType = "CC",
                DocumentNumber = documento,
                FullName = $"{rol} Integración",
                Ordinal = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = TenantSeed.LoneId,
                ProcedureInstanceId = InstanceId,
                PartyRole = rol,
                DocumentType = "CC",
                DocumentNumber = documento,
                Name = $"{rol} Integración",
                Status = BiometricEstados.Aprobado,
                TokenHash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow,
                ValidatedAt = DateTimeOffset.UtcNow,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(10),
            });
        }
        await ctx.SaveChangesAsync();

        return (TenantSeed.LoneId, type);
    }

    /// <summary>Storage en memoria: siempre devuelve los mismos bytes originales y un path nuevo por save.</summary>
    private sealed class IdentityAttachmentStorage(byte[] bytes) : IAttachmentStorage
    {
        private int _saves;

        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default)
        {
            _saves++;
            return Task.FromResult(new StoredFile($"it/{procedureInstanceId}/{_saves}.pdf", $"sha-{_saves}", content.Length));
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(new MemoryStream(bytes));

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed class PassthroughMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(p => p).ToArray();

        public byte[] Compose(MergeRequest request) => request.Parts.SelectMany(p => p.Pdf).ToArray();
    }

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
                PrivateKeyPem: "pem-privada-de-prueba",
                PublicKeyPem: "pem-publica-de-prueba",
                SignedAt: DateTimeOffset.UtcNow);
        }
    }
}
