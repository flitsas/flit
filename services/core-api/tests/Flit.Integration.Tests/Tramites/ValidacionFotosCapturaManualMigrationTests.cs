using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14310 (Feature #14307, Épica #14271) — DDL 171 contra PostgreSQL real con TODAS las migraciones: las 6 columnas existen y
/// son NULL, el CHECK <c>ck_biometric_validations_photo_checks</c> rechaza un motivo con estado verificada o un valor fuera de
/// lista, y <c>TryPersistManualCaptureAsync</c> (UPDATE atómico fuera del change tracker) guarda de verdad las 6 columnas y
/// sobrescribe el resultado anterior.
/// </summary>
public sealed class ValidacionFotosCapturaManualMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Office = new("0199c200-0000-7000-8000-000000000a01");
    private static readonly Guid OtTenant = new("b1000000-0000-7000-8000-0000000a10d1");
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000a10d2");
    private static readonly Guid Signer = new("0199c200-0000-7000-8000-000000000a11");
    private static readonly string Sha = new('a', 64);

    private static readonly string[] Columnas =
    [
        "face_check_status", "face_check_reason", "id_front_check_status", "id_front_check_reason",
        "id_back_check_status", "id_back_check_reason",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<string?> ConstraintOfViolationAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.ConstraintName ?? ex.SqlState;
        }
    }

    private async Task<(NpgsqlConnection Cn, Guid ValidationId)> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-143", false, null));
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-143", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT-O-143', 'OT integración 14310', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o);
            INSERT INTO admin.mandate_signers
              (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
               created_at, signer_model, signature_method, is_active, email)
            VALUES (@s, @o, 'Mandatario de prueba', 'CC', '7700999143', 'h', now(), now(), 'natural', 'biometria', true, 'm@flit.test');
            """,
            ("o", Office), ("ott", OtTenant), ("s", Signer));

        var id = Guid.NewGuid();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, mandate_signer_id, name, document_type, document_number, email, status, provider,
               token_hash, expires_at, created_at, manual_activated_at, consent_at, consent_text_version)
            VALUES (@id, @t, 'mandatario', @s, 'Persona', 'CC', '7700999143', 'p@flit.test', 'manual_activo', 'manual',
                    @h, now() + interval '1 hour', now(), now() - interval '2 hour', now() - interval '1 hour', 'v1')
            """,
            ("id", id), ("t", Company), ("s", Signer), ("h", Guid.NewGuid().ToString("N")));
        return (cn, id);
    }

    private static Task SetAsync(NpgsqlConnection cn, Guid id, string set) =>
        ExecAsync(cn, $"UPDATE tramites.procedure_instance_biometric_validations SET {set} WHERE id = @id", ("id", id));

    [PostgresFact]
    public async Task LaMigracionCreaLas6ColumnasNullables()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, is_nullable FROM information_schema.columns
            WHERE table_schema = 'tramites' AND table_name = 'procedure_instance_biometric_validations'
              AND column_name = ANY(@c)
            """, cn);
        cmd.Parameters.AddWithValue("c", Columnas);
        var found = new Dictionary<string, string>();
        await using (var r = await cmd.ExecuteReaderAsync(Ct))
        {
            while (await r.ReadAsync(Ct))
                found[r.GetString(0)] = r.GetString(1);
        }

        found.Keys.Should().BeEquivalentTo(Columnas);
        found.Values.Should().OnlyContain(v => v == "YES");
    }

    [PostgresFact]
    public async Task ElCheckRechazaMotivoConEstadoVerificadaYValoresFueraDeLista()
    {
        var (cn, id) = await SeedAsync();
        await using var _ = cn;
        const string check = "ck_biometric_validations_photo_checks";

        (await ConstraintOfViolationAsync(() => SetAsync(cn, id, "face_check_status = 'verificada', face_check_reason = 'sin_rostro'")))
            .Should().Be(check, "el motivo solo existe en una foto no verificada");
        (await ConstraintOfViolationAsync(() => SetAsync(cn, id, "id_front_check_status = 'aprobada'"))).Should().Be(check);
        (await ConstraintOfViolationAsync(() => SetAsync(cn, id, "id_back_check_status = 'no_verificada', id_back_check_reason = 'texto libre'")))
            .Should().Be(check);
        (await ConstraintOfViolationAsync(() => SetAsync(cn, id, "face_check_reason = 'sin_rostro'")))
            .Should().Be(check, "motivo sin estado");

        (await ConstraintOfViolationAsync(() => SetAsync(cn, id,
            "face_check_status = 'verificada', id_front_check_status = 'no_verificada', id_front_check_reason = 'lado_incorrecto'")))
            .Should().BeNull("las combinaciones válidas se aceptan");
    }

    [PostgresFact]
    public async Task TryPersistManualCapture_guarda_las_6_columnas_y_la_captura_nueva_sobrescribe()
    {
        var (cn, id) = await SeedAsync();
        await using var _ = cn;

        // 1) Primera captura con resultado mixto.
        await using (var ctx = NewContext())
        {
            var v = await ctx.ProcedureInstanceBiometricValidations.SingleAsync(x => x.Id == id, Ct);
            v.RegistrarCapturaManual("r", "a", "b", "f", Sha, DateTimeOffset.UtcNow, new ManualPhotoChecks(
                ManualPhotoCheck.Verified, ManualPhotoCheck.NotVerified("lado_incorrecto"), ManualPhotoCheck.NotVerified("sin_soporte")));
            (await new ProcedureInstanceRepository(ctx).TryPersistManualCaptureAsync(v, Ct)).Should().BeTrue();
        }

        await using (var ctx = NewContext())
        {
            var v = await ctx.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
            v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
            (v.FaceCheckStatus, v.FaceCheckReason).Should().Be(("verificada", null));
            (v.IdFrontCheckStatus, v.IdFrontCheckReason).Should().Be(("no_verificada", "lado_incorrecto"));
            (v.IdBackCheckStatus, v.IdBackCheckReason).Should().Be(("no_verificada", "sin_soporte"));
        }

        // 2) Rechazo y captura nueva SIN el campo: sobrescribe con «sin_resultado» (la fila vuelve a esperar captura).
        await SetAsync(cn, id,
            "status = 'rechazado', rejection_reason_code = 'imagen_borrosa', consent_at = now(), manual_activated_at = now() - interval '1 minute'");
        await using (var ctx = NewContext())
        {
            var v = await ctx.ProcedureInstanceBiometricValidations.SingleAsync(x => x.Id == id, Ct);
            v.RegistrarCapturaManual("r2", "a2", "b2", "f2", Sha, DateTimeOffset.UtcNow);
            (await new ProcedureInstanceRepository(ctx).TryPersistManualCaptureAsync(v, Ct)).Should().BeTrue();
        }

        await using (var ctx = NewContext())
        {
            var v = await ctx.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
            v.PhotoChecks.Should().Be(ManualPhotoChecks.AllUnknown);
            v.FaceCheckReason.Should().Be("sin_resultado", "sobrescribió el resultado anterior");
        }
    }

    [PostgresFact]
    public async Task Una_fila_anterior_sin_resultado_se_lee_con_PhotoChecks_null()
    {
        var (cn, id) = await SeedAsync();
        await using var _ = cn;

        await using var ctx = NewContext();
        var v = await ctx.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.PhotoChecks.Should().BeNull();
    }
}
