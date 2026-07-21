using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.GetSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.SignatureVault;

/// <summary>
/// Tests del CRUD del baúl de firmas (HU #10643, ADR-0025) ejercitando los handlers reales sobre
/// <see cref="DbSignatureVaultReader"/> + <see cref="SignatureVaultRepository"/> (InMemory) y un
/// storage de artefacto en memoria. Cubren: alta feliz (sube artefacto + persiste solo path/hash),
/// validaciones 422, conflicto de firma activa (23505 → 422 vía stub) y revocación idempotente.
/// El material de firma NUNCA se persiste en BD ni se expone en las respuestas.
/// </summary>
public sealed class SignatureVaultHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-000000000001");
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUg=="; // bytes arbitrarios base64-válidos.

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_HappyPath_PersistsPathAndHash_NoMaterial()
    {
        await using var ctx = NewContext();
        var (create, list, _, _) = Handlers(ctx, out var storage);

        var result = await create.HandleAsync(NewCreate(), Ct);

        result.IsValid.Should().BeTrue();
        result.SignatureVaultId.Should().NotBeNull();
        storage.LastArtifact.Should().NotBeNullOrEmpty(); // el artefacto se subió a storage.

        var rows = await list.HandleAsync(new ListSignatureVaultQuery { TenantId = Tenant }, Ct);
        rows.Should().ContainSingle();
        rows[0].NitEmpresa.Should().Be("900000000-1");
        rows[0].Estado.Should().Be("activa");
        rows[0].StoragePath.Should().Be("fm-file-abc");   // solo referencia al artefacto.
        rows[0].StorageSha256.Should().Be("sha-256-hex"); // integridad, no material.
        rows[0].SignatureHash.Should().Be("sha-256-hex"); // reutiliza el SHA-256 del storage.
    }

    [Fact]
    public async Task Create_InvalidBase64_Returns422()
    {
        await using var ctx = NewContext();
        var (create, _, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(NewCreate(artifact: "no-es-base64-@@@"), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "artefactoFirmaBase64" && e.Code == "artefacto_invalido");
    }

    [Fact]
    public async Task Create_MissingRequiredFields_Returns422()
    {
        await using var ctx = NewContext();
        var (create, _, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(new CreateSignatureVaultCommand
        {
            TenantId = Tenant,
            DocumentType = "",
            DocumentNumber = "",
            NitEmpresa = "",
            FullName = "",
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = new DateOnly(2026, 12, 31),
            ArtefactoFirmaBase64 = PngBase64,
        }, Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "documentNumber" && e.Code == "requerido");
        result.Errors.Should().Contain(e => e.Field == "nitEmpresa" && e.Code == "requerido");
    }

    [Fact]
    public async Task Create_VigenciaInvertida_Returns422()
    {
        await using var ctx = NewContext();
        var (create, _, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(
            NewCreate(desde: new DateOnly(2026, 12, 31), hasta: new DateOnly(2026, 1, 1)), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "vigenciaHasta" && e.Code == "vigencia_invalida");
    }

    [Fact]
    public async Task Create_DuplicateActive_TranslatesTo422()
    {
        // InMemory no enforce el índice único parcial: se usa un repo que emula el 23505 → dominio.
        var create = new CreateSignatureVaultHandler(new FakeArtifactStorage(), new ConflictingRepository());

        var result = await create.HandleAsync(NewCreate(), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("firma_activa_existente");
    }

    [Fact]
    public async Task Revoke_IsIdempotent_AndFlipsEstado()
    {
        await using var ctx = NewContext();
        var (create, list, _, revoke) = Handlers(ctx, out _);

        var created = await create.HandleAsync(NewCreate(), Ct);
        var id = created.SignatureVaultId!.Value;

        var first = await revoke.HandleAsync(NewRevoke(id), Ct);
        first.Should().Be(RevokeSignatureVaultOutcome.Revoked);

        // Revocar de nuevo sigue devolviendo Revoked (idempotente).
        var second = await revoke.HandleAsync(NewRevoke(id), Ct);
        second.Should().Be(RevokeSignatureVaultOutcome.Revoked);

        var rows = await list.HandleAsync(new ListSignatureVaultQuery { TenantId = Tenant }, Ct);
        rows.Single(r => r.Id == id).Estado.Should().Be("revocada");
    }

    [Fact]
    public async Task Revoke_UnknownId_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var (_, _, _, revoke) = Handlers(ctx, out _);

        var outcome = await revoke.HandleAsync(NewRevoke(Guid.NewGuid()), Ct);

        outcome.Should().Be(RevokeSignatureVaultOutcome.NotFound);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenAbsent()
    {
        await using var ctx = NewContext();
        var (_, _, get, _) = Handlers(ctx, out _);

        var result = await get.HandleAsync(
            new GetSignatureVaultByIdQuery { TenantId = Tenant, Id = Guid.NewGuid() }, Ct);

        result.Should().BeNull();
    }

    // ---------- Helpers ----------

    private static CreateSignatureVaultCommand NewCreate(
        string? artifact = PngBase64,
        DateOnly? desde = null,
        DateOnly? hasta = null) =>
        new()
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            NitEmpresa = "900000000-1",
            FullName = "Apoderada Renting S.A.S.",
            VigenciaDesde = desde ?? new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta ?? new DateOnly(2026, 12, 31),
            ArtefactoFirmaBase64 = artifact,
        };

    private static RevokeSignatureVaultCommand NewRevoke(Guid id) =>
        new() { TenantId = Tenant, Id = id };

    private static (
        CreateSignatureVaultHandler Create,
        ListSignatureVaultHandler List,
        GetSignatureVaultByIdHandler Get,
        RevokeSignatureVaultHandler Revoke) Handlers(FlitDbContext ctx, out FakeArtifactStorage storage)
    {
        var reader = new DbSignatureVaultReader(ctx);
        var repo = new SignatureVaultRepository(ctx);
        storage = new FakeArtifactStorage();
        return (
            new CreateSignatureVaultHandler(storage, repo),
            new ListSignatureVaultHandler(reader),
            new GetSignatureVaultByIdHandler(reader),
            new RevokeSignatureVaultHandler(reader, repo));
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-signature-vault-{Guid.NewGuid()}")
            .Options);

    /// <summary>Storage de artefacto en memoria: no toca red, devuelve path/hash fijos.</summary>
    private sealed class FakeArtifactStorage : ISignatureVaultArtifactStorage
    {
        public byte[]? LastArtifact { get; private set; }

        public Task<StoredSignatureArtifact> SaveAsync(
            Guid tenantId, byte[] artifact, CancellationToken cancellationToken = default)
        {
            LastArtifact = artifact;
            return Task.FromResult(new StoredSignatureArtifact("fm-file-abc", "sha-256-hex"));
        }
    }

    /// <summary>Repositorio que emula el conflicto de firma activa (23505 traducido a dominio).</summary>
    private sealed class ConflictingRepository : ISignatureVaultRepository
    {
        public Task<Guid> CreateAsync(CreateSignatureVaultData data, CancellationToken cancellationToken = default) =>
            throw new SignatureVaultActiveConflictException();

        public Task<bool> RevokeAsync(RevokeSignatureVaultData data, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
