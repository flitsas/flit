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
        result.Errors.Should().Contain(e => e.Field == "fullName" && e.Code == "requerido");
        // HU #10930: nitEmpresa ya NO es obligatorio — no debe reportarse como requerido.
        result.Errors.Should().NotContain(e => e.Field == "nitEmpresa");
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

    [Fact]
    public async Task Create_WithoutNit_Succeeds()
    {
        // HU #10930: la firma es de la persona + tenant; el NIT dejó de ser obligatorio.
        await using var ctx = NewContext();
        var (create, list, _, _) = Handlers(ctx, out _);

        var result = await create.HandleAsync(NewCreate(nit: null), Ct);

        result.IsValid.Should().BeTrue();
        result.SignatureVaultId.Should().NotBeNull();

        var rows = await list.HandleAsync(new ListSignatureVaultQuery { TenantId = Tenant }, Ct);
        rows.Should().ContainSingle();
        rows[0].NitEmpresa.Should().BeNull();
        rows[0].Estado.Should().Be("activa");
    }

    [Fact]
    public async Task Create_PersistsAndPropagatesCodigoHash()
    {
        // HU #10930: codigo_hash es el código alfanumérico que digita el usuario (distinto del SHA-256).
        await using var ctx = NewContext();
        var (create, _, get, _) = Handlers(ctx, out _);

        var created = await create.HandleAsync(NewCreate(codigoHash: "ABC-123-XYZ"), Ct);
        created.IsValid.Should().BeTrue();

        var detail = await get.HandleAsync(
            new GetSignatureVaultByIdQuery { TenantId = Tenant, Id = created.SignatureVaultId!.Value }, Ct);

        detail.Should().NotBeNull();
        detail!.CodigoHash.Should().Be("ABC-123-XYZ");
        detail.SignatureHash.Should().NotBe("ABC-123-XYZ"); // signature_hash es el SHA-256 del artefacto.
    }

    [Fact]
    public async Task FindActiveByDocument_FindsByPerson_IgnoringNit()
    {
        // HU #10930: el consumo por persona resuelve la firma activa por (tenant, tipo + documento).
        await using var ctx = NewContext();
        var (create, _, _, _) = Handlers(ctx, out _);
        var reader = new DbSignatureVaultReader(ctx);

        await create.HandleAsync(NewCreate(nit: null, documentNumber: "555000111"), Ct);

        var found = await reader.FindActiveByDocumentAsync(Tenant, "CC", "555000111", Ct);
        found.Should().NotBeNull();
        found!.DocumentNumber.Should().Be("555000111");
        found.Estado.Should().Be(SignatureVaultEstado.Activa);

        var missing = await reader.FindActiveByDocumentAsync(Tenant, "CC", "000000000", Ct);
        missing.Should().BeNull();
    }

    // ---------- Helpers ----------

    // ───────── HU #11193 — capturar la firma sin salir del formulario del representante ─────────

    [Fact]
    public async Task HU11193_AC4_FirmaActivaVencida_SeRevocaYSeCreaLaNueva()
    {
        // El índice único parcial solo mira el estado, no la vigencia: una firma 'activa' pero vencida
        // bloqueaba el alta y dejaba al usuario sin salida dentro del formulario del representante.
        await using var ctx = NewContext();
        var (create, list, _, _) = Handlers(ctx, out _);

        var vencida = await create.HandleAsync(
            NewCreate(desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 12, 31)), Ct);
        vencida.IsValid.Should().BeTrue();

        // El repositorio real de InMemory no enforce el índice: se emula el conflicto con el repo
        // que lanza el 23505 traducido, y se le da al handler el lector real para que resuelva la activa.
        var conRevocacion = new CreateSignatureVaultHandler(
            new FakeArtifactStorage(),
            new ConflictOnFirstCallRepository(new SignatureVaultRepository(ctx)),
            new DbSignatureVaultReader(ctx));

        var nueva = await conRevocacion.HandleAsync(
            NewCreate(desde: new DateOnly(2026, 1, 1), hasta: new DateOnly(2026, 12, 31)), Ct);

        nueva.IsValid.Should().BeTrue("la vencida se revoca y la nueva ocupa su lugar");
        var rows = await list.HandleAsync(new ListSignatureVaultQuery { TenantId = Tenant }, Ct);
        rows.Single(r => r.Id == vencida.SignatureVaultId!.Value).Estado.Should().Be("revocada");
        rows.Single(r => r.Id == nueva.SignatureVaultId!.Value).Estado.Should().Be("activa");
    }

    [Fact]
    public async Task HU11193_AC4_FirmaActivaVIGENTE_TambienSeSustituye()
    {
        // D7 — la última firma capturada manda: también sustituye a una vigente. La anterior no se
        // borra, queda 'revocada', así que lo ya firmado con ella sigue siendo trazable.
        await using var ctx = NewContext();
        var (create, list, _, _) = Handlers(ctx, out _);

        var vigente = await create.HandleAsync(
            NewCreate(desde: new DateOnly(2026, 1, 1), hasta: new DateOnly(2030, 12, 31)), Ct);
        vigente.IsValid.Should().BeTrue();

        var conRevocacion = new CreateSignatureVaultHandler(
            new FakeArtifactStorage(),
            new ConflictOnFirstCallRepository(new SignatureVaultRepository(ctx)),
            new DbSignatureVaultReader(ctx));

        var nueva = await conRevocacion.HandleAsync(NewCreate(), Ct);

        nueva.IsValid.Should().BeTrue();
        var rows = await list.HandleAsync(new ListSignatureVaultQuery { TenantId = Tenant }, Ct);
        rows.Single(r => r.Id == vigente.SignatureVaultId!.Value).Estado.Should().Be("revocada");
        rows.Single(r => r.Id == nueva.SignatureVaultId!.Value).Estado.Should().Be("activa");
    }

    [Fact]
    public async Task HU11193_SinLector_ConservaElComportamientoAnterior()
    {
        // Sin lector inyectado no hay forma de saber si la que bloquea está vencida: 422, como antes.
        var create = new CreateSignatureVaultHandler(new FakeArtifactStorage(), new ConflictingRepository());

        var result = await create.HandleAsync(NewCreate(), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("firma_activa_existente");
    }

    private static CreateSignatureVaultCommand NewCreate(
        string? artifact = PngBase64,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        string? nit = "900000000-1",
        string? codigoHash = null,
        string? documentNumber = "123456789") =>
        new()
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = documentNumber,
            NitEmpresa = nit,
            FullName = "Apoderada Renting S.A.S.",
            VigenciaDesde = desde ?? new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta ?? new DateOnly(2026, 12, 31),
            ArtefactoFirmaBase64 = artifact,
            CodigoHash = codigoHash,
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
            new ListSignatureVaultHandler(reader, TimeProvider.System),
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

    /// <summary>
    /// HU #11193 — emula el índice único parcial que InMemory no aplica: el PRIMER intento de alta
    /// choca (como en PostgreSQL con una firma activa presente) y el reintento posterior a la
    /// revocación se delega al repositorio real, que es el que debe persistir la nueva firma.
    /// </summary>
    private sealed class ConflictOnFirstCallRepository(ISignatureVaultRepository inner) : ISignatureVaultRepository
    {
        private bool _yaChoco;

        public Task<Guid> CreateAsync(CreateSignatureVaultData data, CancellationToken cancellationToken = default)
        {
            if (!_yaChoco)
            {
                _yaChoco = true;
                throw new SignatureVaultActiveConflictException();
            }

            return inner.CreateAsync(data, cancellationToken);
        }

        public Task<bool> RevokeAsync(RevokeSignatureVaultData data, CancellationToken cancellationToken = default) =>
            inner.RevokeAsync(data, cancellationToken);
    }
}
