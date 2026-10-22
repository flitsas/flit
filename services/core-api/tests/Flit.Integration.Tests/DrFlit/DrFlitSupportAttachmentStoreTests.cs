using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.DrFlit;

/// <summary>
/// HU #12924 — adjuntos previos de los casos de soporte contra PostgreSQL real (DDL 120) con un
/// almacenamiento en memoria. Cubre el guardado con vencimiento, la purga, el filtrado por dueño y la
/// vinculación al caso; y que la tabla de casos respeta sus CHECK.
/// Uso de ejemplo:
/// <code>
/// var store = new DrFlitSupportAttachmentStore(ctx, storage);
/// var a = await store.SaveAsync(tenant, user, "captura.png", "image/png", stream, expiresAt, ct);
/// </code>
/// </summary>
public sealed class DrFlitSupportAttachmentStoreTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = TenantSeed.LoneId;
    private static readonly Guid TenantB = TenantSeed.ParentId;
    private static readonly Guid UserA = Guid.Parse("0199a000-0000-7000-8000-00000000d101");
    private static readonly Guid UserB = Guid.Parse("0199a000-0000-7000-8000-00000000d102");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStorage _storage = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Users.AddRange(NewUser(UserA, "a"), NewUser(UserB, "b"));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static User NewUser(Guid id, string suffix) => new()
    {
        Id = id,
        Email = $"soporte.{suffix}@example.test",
        DisplayName = $"Usuario {suffix}",
        Status = "active",
        HomeTenantId = TenantA,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private DrFlitSupportAttachmentStore Store() => new(NewContext(), _storage);

    private Task<DrFlitStoredAttachment> Save(Guid tenant, Guid user, string name, DateTimeOffset expiresAt) =>
        Store().SaveAsync(tenant, user, name, "image/png", new MemoryStream([1, 2, 3]), expiresAt, TestContext.Current.CancellationToken);

    // ── AC1 — se guarda con vencimiento y en el almacenamiento etiquetado ───────────────

    [PostgresFact]
    public async Task AC1_Guarda_FilaSinCasoConVencimientoYBinarioEtiquetado()
    {
        var saved = await Save(TenantA, UserA, "captura.png", Now.AddHours(24));

        saved.FileName.Should().Be("captura.png");
        saved.SizeBytes.Should().Be(3);
        _storage.Saved.Should().ContainSingle().Which.Tag.Should().Be(DrFlitSupportAttachmentStore.StorageTag).And.Be("dr-flit-support");

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT support_case_id IS NULL, expires_at, content_type, size_bytes, length(sha256) FROM dr_flit.support_case_attachments WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("id", saved.Id);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await r.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        r.GetBoolean(0).Should().BeTrue();
        r.GetFieldValue<DateTimeOffset>(1).Should().Be(Now.AddHours(24));
        r.GetString(2).Should().Be("image/png");
        r.GetInt64(3).Should().Be(3);
        r.GetInt32(4).Should().Be(64);
    }

    [PostgresFact]
    public async Task AC1_PurgaSoloLosVencidosSinCasoDelUsuario()
    {
        var vencido = await Save(TenantA, UserA, "vencido.png", Now.AddMinutes(-1));
        var vigente = await Save(TenantA, UserA, "vigente.png", Now.AddHours(1));
        var otroUsuario = await Save(TenantA, UserB, "otro.png", Now.AddMinutes(-1));

        var purged = await Store().PurgeExpiredAsync(TenantA, UserA, Now, TestContext.Current.CancellationToken);

        purged.Should().Be(1);
        _storage.Deleted.Should().Equal(_storage.PathOf(vencido.Id));
        (await CountAsync()).Should().Be(2);
        (await Store().GetPendingAsync(TenantA, UserA, [vigente.Id, vencido.Id], Now, TestContext.Current.CancellationToken))
            .Select(p => p.Id).Should().Equal(vigente.Id);
        (await Store().GetPendingAsync(TenantA, UserB, [otroUsuario.Id], Now.AddMinutes(-2), TestContext.Current.CancellationToken))
            .Should().ContainSingle();
    }

    // ── Base de la HU #12925 — solo los adjuntos propios, vigentes y sin caso ──────────

    [PostgresFact]
    public async Task GetPending_NoDevuelveAdjuntosDeOtroUsuarioOTenantNiVencidos()
    {
        var propio = await Save(TenantA, UserA, "propio.png", Now.AddHours(1));
        var deOtroUsuario = await Save(TenantA, UserB, "ajeno.png", Now.AddHours(1));
        var deOtroTenant = await Save(TenantB, UserA, "otro-tenant.png", Now.AddHours(1));
        var vencido = await Save(TenantA, UserA, "vencido.png", Now.AddMinutes(-5));

        var pending = await Store().GetPendingAsync(
            TenantA, UserA, [propio.Id, deOtroUsuario.Id, deOtroTenant.Id, vencido.Id, Guid.NewGuid()], Now, TestContext.Current.CancellationToken);

        pending.Should().ContainSingle().Which.Should().Be(
            new DrFlitPendingAttachment(propio.Id, "propio.png", "image/png", _storage.PathOf(propio.Id)));
    }

    [PostgresFact]
    public async Task Link_VinculaAlCaso_YYaNoEsPendienteNiSePurga()
    {
        var a = await Save(TenantA, UserA, "a.png", Now.AddMinutes(1));
        var caseId = await InsertCaseAsync("created", adoWorkItemId: 13001);

        await Store().LinkToCaseAsync([a.Id], caseId, TestContext.Current.CancellationToken);

        (await Store().GetPendingAsync(TenantA, UserA, [a.Id], Now, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await Store().PurgeExpiredAsync(TenantA, UserA, Now.AddDays(2), TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [PostgresFact]
    public async Task OpenAsync_DevuelveElBinarioGuardado()
    {
        var a = await Save(TenantA, UserA, "a.png", Now.AddHours(1));

        await using var stream = await Store().OpenAsync(_storage.PathOf(a.Id), TestContext.Current.CancellationToken);

        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms, TestContext.Current.CancellationToken);
        ms.ToArray().Should().Equal(1, 2, 3);
    }

    // ── DDL de support_cases (usado por la HU #12925) ───────────────────────────────────

    [PostgresFact]
    public async Task SupportCases_RespetaLosCheckDeEstado()
    {
        await InsertCaseAsync("pending");
        await InsertCaseAsync("created", adoWorkItemId: 1);
        await InsertCaseAsync("failed", lastError: "http_502");

        var sinWorkItem = () => InsertCaseAsync("created");
        var sinError = () => InsertCaseAsync("failed");
        var estadoInvalido = () => InsertCaseAsync("closed");

        (await sinWorkItem.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_support_cases_created_requires_work_item");
        (await sinError.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_support_cases_failed_requires_error");
        (await estadoInvalido.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_support_cases_status");
    }

    private async Task<Guid> InsertCaseAsync(string status, int? adoWorkItemId = null, string? lastError = null)
    {
        var id = Guid.CreateVersion7();
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO dr_flit.support_cases
                (id, tenant_id, created_by_user_id, status, ado_project, ado_work_item_id, title, environment, priority,
                 incidence, requester_name, requester_email, problem_detail, expected_result, last_error)
            VALUES (@id, @tenant, @user, @status, 'FLIT - SOPORTE', @wi, 'Título', 'DEV', 'Alta',
                    'siempre', 'Nombre', 'correo@example.test', 'Detalle', 'Esperado', @err)
            """, cn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", TenantA);
        cmd.Parameters.AddWithValue("user", UserA);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("wi", (object?)adoWorkItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("err", (object?)lastError ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<long> CountAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM dr_flit.support_case_attachments", cn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class InMemoryStorage : IAttachmentStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];
        private readonly Dictionary<Guid, string> _paths = [];

        public List<(Guid Id, string Tag)> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        public string PathOf(Guid id) => _paths[id];

        public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = "fm-" + procedureInstanceId.ToString("N");
            _files[path] = ms.ToArray();
            _paths[procedureInstanceId] = path;
            Saved.Add((procedureInstanceId, tipo));
            return new StoredFile(path, new string('a', 64), ms.Length);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(_files.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public void Delete(string storagePath)
        {
            Deleted.Add(storagePath);
            _files.Remove(storagePath);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
