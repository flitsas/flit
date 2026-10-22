using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.DrFlit;

/// <summary>
/// HU #12925 — <c>dr_flit.support_cases</c> contra PostgreSQL real: el intento se escribe en pending con
/// todo lo que se envía al proveedor y pasa a created o failed.
/// Uso de ejemplo:
/// <code>
/// var id = await repo.InsertPendingAsync(record, ct);
/// await repo.MarkCreatedAsync(id, workItemId, attachmentCount: 2, attachmentFailures: 0, ct);
/// </code>
/// </summary>
public sealed class DrFlitSupportCaseRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = TenantSeed.LoneId;
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-00000000d201");

    private static readonly DrFlitSupportTicket Ticket = new(
        "No puedo subir la factura", "Usuario Prueba", "usuario.prueba@example.test", null, "Empresa Demo S.A.S",
        "Detalle del error", "Resultado esperado", DrFlitCaseFrequency.AVeces, DrFlitCasePriority.Media,
        DrFlitDeployEnvironment.QA, "/tramites", new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero));

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(new User
        {
            Id = User, Email = "caso.soporte@example.test", DisplayName = "Caso", Status = "active",
            HomeTenantId = Tenant, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private DrFlitSupportCaseRepository Repo() => new(NewContext());

    private Task<Guid> InsertAsync() =>
        Repo().InsertPendingAsync(new DrFlitSupportCaseRecord(Tenant, User, "FLIT - SOPORTE", Ticket, "Otros Tramites"), TestContext.Current.CancellationToken);

    private async Task<Dictionary<string, object?>> RowAsync(Guid id)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM dr_flit.support_cases WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await r.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        return Enumerable.Range(0, r.FieldCount).ToDictionary(r.GetName, i => r.IsDBNull(i) ? null : r.GetValue(i));
    }

    [PostgresFact]
    public async Task AC1_InsertaPendingConLoQueSeEnviaAlProveedor()
    {
        var id = await InsertAsync();

        var row = await RowAsync(id);
        row["status"].Should().Be("pending");
        row["tenant_id"].Should().Be(Tenant);
        row["created_by_user_id"].Should().Be(User);
        row["ado_project"].Should().Be("FLIT - SOPORTE");
        row["environment"].Should().Be("QA");
        row["priority"].Should().Be("Media");
        row["incidence"].Should().Be("a_veces");
        row["affected_module"].Should().Be("Otros Tramites");
        row["requester_email"].Should().Be("usuario.prueba@example.test");
        row["requester_phone"].Should().BeNull();
        row["ado_work_item_id"].Should().BeNull();
    }

    [PostgresFact]
    public async Task AC1_MarkCreated_GuardaWorkItemYConteos()
    {
        var id = await InsertAsync();

        await Repo().MarkCreatedAsync(id, 13001, 3, 1, TestContext.Current.CancellationToken);

        var row = await RowAsync(id);
        row["status"].Should().Be("created");
        row["ado_work_item_id"].Should().Be(13001);
        row["attachment_count"].Should().Be(3);
        row["attachment_upload_failures"].Should().Be(1);
        row["last_error"].Should().BeNull();
        ((long)row["row_version"]!).Should().BeGreaterThan(0, "trg_row_version incrementa en cada UPDATE");
    }

    [PostgresFact]
    public async Task AC2_MarkFailed_GuardaElCodigoSinPii()
    {
        var id = await InsertAsync();

        await Repo().MarkFailedAsync(id, "http_503", 1, 0, TestContext.Current.CancellationToken);

        var row = await RowAsync(id);
        row["status"].Should().Be("failed");
        row["last_error"].Should().Be("http_503");
        row["ado_work_item_id"].Should().BeNull();
    }

    [PostgresFact]
    public async Task ElIntentoQuedaAuditado()
    {
        var id = await InsertAsync();

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_logs WHERE table_name = 'support_cases' AND record_id = @id", cn);
        cmd.Parameters.AddWithValue("id", id);
        ((long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().BeGreaterThan(0);
    }
}
