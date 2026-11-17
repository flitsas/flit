using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #14559 (ADR-0085) — el veredicto OCR del adjunto (<c>ocr_status</c>/<c>ocr_reason</c>, DDL 196) contra PostgreSQL
/// real: los dos CHECK cierran el vocabulario y el motivo solo acompaña a <c>rejected</c>/<c>skipped</c>; EF escribe y lee
/// las dos columnas; un adjunto sin veredicto queda en NULL (sin backfill).
/// </summary>
public sealed class AttachmentOcrStatusCheckTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string CkStatus = "ck_procedure_instance_attachments_ocr_status";
    private const string CkReason = "ck_procedure_instance_attachments_ocr_reason";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000014559");
    private static readonly Guid Tramite = new("5a5a5a5a-0002-4000-8000-000000014559");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000014559");

    [PostgresTheory]
    [InlineData(null, null)]
    [InlineData("verified", null)]
    [InlineData("rejected", null)]
    [InlineData("rejected", "vin_mismatch")]
    [InlineData("rejected", "tipo_invalido")]
    [InlineData("rejected", "no_confirmado")]
    [InlineData("rejected", "ilegible")]
    [InlineData("skipped", "excede_tamano")]
    [InlineData("skipped", "analisis_fallido")]
    public async Task Check_AceptaLasCombinacionesValidas(string? status, string? reason)
    {
        await SembrarAsync();
        await using var conn = await Fixture.OpenConnectionAsync();

        var id = await InsertarAsync(conn, status, reason);

        (await LeerAsync(conn, id)).Should().Be((status, reason));
    }

    [PostgresTheory]
    [InlineData("foo", null, CkStatus)]
    [InlineData("VERIFIED", null, CkStatus)]
    [InlineData("verified", "vin_mismatch", CkReason)]
    [InlineData(null, "vin_mismatch", CkReason)]
    [InlineData("rejected", "el VIN 9BW... no coincide", CkReason)]
    public async Task Check_RechazaValoresFueraDelVocabulario(string? status, string? reason, string constraint)
    {
        await SembrarAsync();
        await using var conn = await Fixture.OpenConnectionAsync();

        var insertar = async () => await InsertarAsync(conn, status, reason);

        var rechazo = (await insertar.Should().ThrowAsync<PostgresException>()).Which;
        rechazo.SqlState.Should().Be(CheckViolation);
        rechazo.ConstraintName.Should().Be(constraint);
    }

    [PostgresFact]
    public async Task Ef_IdaYVuelta_EscribeYLeeLasDosColumnas_YSinVeredictoQuedaNulo()
    {
        await SembrarAsync();
        var conVeredicto = Adjunto("factura", "rejected", "vin_mismatch");
        var sinVeredicto = Adjunto("soat", null, null);
        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceAttachments.AddRange(conVeredicto, sinVeredicto);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var lectura = NewContext();
        var filas = await lectura.ProcedureInstanceAttachments.AsNoTracking()
            .Where(a => a.ProcedureInstanceId == Tramite)
            .ToDictionaryAsync(a => a.Id, TestContext.Current.CancellationToken);

        filas[conVeredicto.Id].OcrStatus.Should().Be("rejected");
        filas[conVeredicto.Id].OcrReason.Should().Be("vin_mismatch");
        filas[sinVeredicto.Id].OcrStatus.Should().BeNull();
        filas[sinVeredicto.Id].OcrReason.Should().BeNull();
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────

    private static ProcedureInstanceAttachment Adjunto(string tipo, string? status, string? reason) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Compania,
        ProcedureInstanceId = Tramite,
        Tipo = tipo,
        Filename = $"{tipo}.pdf",
        Mimetype = "application/pdf",
        SizeBytes = 10,
        Sha256 = "aa",
        StoragePath = $"path-{tipo}",
        UploadedAt = DateTimeOffset.UtcNow,
        UploadedBy = Gestor,
        OcrStatus = status,
        OcrReason = reason,
    };

    private static async Task<Guid> InsertarAsync(NpgsqlConnection conn, string? status, string? reason)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO tramites.procedure_instance_attachments
                (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at,
                 ocr_status, ocr_reason)
            VALUES (@tenant, @id, 'factura', 'x.pdf', 'application/pdf', 10, 'aa', 'path', now(), @status, @reason)
            RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", Tramite);
        cmd.Parameters.Add(new NpgsqlParameter("status", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)status ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("reason", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)reason ?? DBNull.Value });
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<(string?, string?)> LeerAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT ocr_status, ocr_reason FROM tramites.procedure_instance_attachments WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-14559", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-14559@flit.test",
                DisplayName = "Gestor sintético 14559",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        foreach (var sql in new[]
                 {
                     """
                     INSERT INTO tramites.procedure_instances
                         (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                     VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                             'IT-14559', 'borrador', 'SINTVIN14559', @gestor, now())
                     """,
                     "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, 'preasignacion')",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Tramite);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("gestor", Gestor);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
