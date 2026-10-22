using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — DDL 129 y lanzador de la validación propia del mandatario contra PostgreSQL
/// real: columna <c>mandate_signer_id</c> con FK y CHECKs de coherencia, ancla ampliada, unicidad en vuelo POR MANDATARIO
/// (dos reenvíos simultáneos dejan una sola activa) y unicidad por documento que ya no cuenta las filas del mandatario.
/// <para>Uso: <c>launcher.LaunchAsync(request)</c> deja la fila en el tenant de la compañía y cierra la anterior en vuelo.</para>
/// </summary>
public sealed class MandatarioValidacionPropiaPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Office = new("0199c100-0000-7000-8000-000000000001");
    private static readonly Guid OtTenant = new("b1000000-0000-7000-8000-0000000000c1");
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000000c2");
    private static readonly Guid Signer = new("0199c100-0000-7000-8000-000000000101");
    private static readonly Guid OtherSigner = new("0199c100-0000-7000-8000-000000000102");
    private const string Documento = "7700123456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-M1", false, null));
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-M1", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT-OT-M1', 'OT integración M1', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o);
            """,
            ("o", Office), ("ott", OtTenant));
        foreach (var (id, n) in new[] { (Signer, 1), (OtherSigner, 2) })
        {
            await ExecAsync(cn,
                """
                INSERT INTO admin.mandate_signers
                  (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
                   created_at, signer_model, signature_method, is_active, email)
                VALUES (@id, @ot, 'Mandatario de prueba', 'CC', @doc, 'h', now(), now(), 'natural', 'biometria', true, 'm@flit.test')
                """,
                ("id", id), ("ot", Office), ("doc", $"{Documento}{n}"));
        }

        return cn;
    }

    private static Task InsertValidationAsync(
        NpgsqlConnection cn, string status, string? partyRole, Guid? signer, string doc = Documento) =>
        ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, mandate_signer_id, name, document_type, document_number, email, status,
               token_hash, expires_at, created_at)
            VALUES (uuidv7(), @t, @r, @s, 'Persona', 'CC', @d, 'p@flit.test', @st, @h, now() + interval '1 hour', now())
            """,
            ("t", Company), ("r", partyRole), ("s", signer), ("d", doc), ("st", status), ("h", Guid.NewGuid().ToString("N")));

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

    [PostgresFact]
    public async Task Ddl129_CheckDeCoherencia_RolMandatarioExigeFicha_YFichaExigeRolMandatario()
    {
        await using var cn = await SeedAsync();

        (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, "enviado", "mandatario", null)))
            .Should().NotBeNull("party_role mandatario sin ficha viola el CHECK (y el ancla)");
        (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, "enviado", "comprador", Signer)))
            .Should().Be("ck_biometric_validations_mandatario_ref");
        (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, "aprobado", "mandatario", Signer)))
            .Should().BeNull("rol mandatario + ficha, sin persona ni trámite, cumple el ancla ampliada");
    }

    [PostgresFact]
    public async Task Ddl129_FkRestrict_NoSePuedeBorrarUnaFichaConValidaciones()
    {
        await using var cn = await SeedAsync();
        await InsertValidationAsync(cn, "aprobado", "mandatario", Signer);

        var violada = await ConstraintOfViolationAsync(
            () => ExecAsync(cn, "DELETE FROM admin.mandate_signers WHERE id = @id", ("id", Signer)));

        violada.Should().Be("fk_procedure_instance_biometric_validations_mandate_signers");
    }

    [PostgresFact]
    public async Task Ddl129_UnaSolaValidacionEnVueloPorMandatario_YAprobadasRechazadasYOtrasFichasNoCuentan()
    {
        await using var cn = await SeedAsync();
        await InsertValidationAsync(cn, "aprobado", "mandatario", Signer);
        await InsertValidationAsync(cn, "rechazado", "mandatario", Signer);
        await InsertValidationAsync(cn, "en_proceso", "mandatario", Signer);
        await InsertValidationAsync(cn, "en_proceso", "mandatario", OtherSigner); // otra ficha, mismo documento base

        var violada = await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, "enviado", "mandatario", Signer));

        violada.Should().Be("uq_biometric_validations_inflight_mandate_signer");
    }

    [PostgresFact]
    public async Task Ddl129_LaUnicidadEnVueloPorDocumento_NoMezclaLasFilasDelMandatarioConLasDelTramite()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.persons (id, tenant_id, document_type, document_number, full_name, email, person_type, created_at)
            VALUES ('0199c100-0000-7000-8000-0000000009aa', @t, 'CC', @d, 'Persona', 'p@flit.test', 'natural', now())
            """,
            ("t", Company), ("d", $"{Documento}1"));
        // Comprador en vuelo con el MISMO documento de la ficha: convive con la validación en vuelo del mandatario.
        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, name, document_type, document_number, email, status, token_hash, expires_at, created_at, person_id)
            VALUES (uuidv7(), @t, 'comprador', 'Persona', 'CC', @d, 'p@flit.test', 'en_proceso', @h, now() + interval '1 hour', now(),
                    '0199c100-0000-7000-8000-0000000009aa')
            """,
            ("t", Company), ("d", $"{Documento}1"), ("h", Guid.NewGuid().ToString("N")));
        await InsertValidationAsync(cn, "en_proceso", "mandatario", Signer, doc: $"{Documento}1");

        // Pero dos del trámite/prevalidación con el mismo documento siguen chocando (regla de HU #11266 intacta).
        var violada = await ConstraintOfViolationAsync(() => ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, name, document_type, document_number, email, status, token_hash, expires_at, created_at, person_id)
            VALUES (uuidv7(), @t, 'vendedor', 'Persona', 'CC', @d, 'p@flit.test', 'enviado', @h, now() + interval '1 hour', now(),
                    '0199c100-0000-7000-8000-0000000009aa')
            """,
            ("t", Company), ("d", $"{Documento}1"), ("h", Guid.NewGuid().ToString("N"))));

        violada.Should().Be("uq_biometric_validations_inflight_doc_norm");
    }

    private static MandateSignerIdentityLauncher NewLauncher(FlitDbContext ctx, IKyverumVerifyClient? kyverum = null)
    {
        var handler = new IniciarPrevalidacionHandler(
            Substitute.For<IPersonRepository>(),
            new ProcedureInstanceRepository(ctx),
            kyverum ?? Substitute.For<IKyverumVerifyClient>(),
            new BiometricsProviderOptions { Provider = kyverum is null ? BiometricProviders.Mock : BiometricProviders.Kyverum },
            new PassThroughSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>());
        return new MandateSignerIdentityLauncher(ctx, handler, NullLogger<MandateSignerIdentityLauncher>.Instance);
    }

    private sealed class PassThroughSecretProtector : IWebhookSecretProtector
    {
        public string Protect(string plaintextSecret) => "protected:" + plaintextSecret;

        public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
    }

    private static MandateSignerIdentityLaunchRequest Request() =>
        new(Signer, Company, "CC", $"{Documento}1", "Mandatario de prueba", "m@flit.test");

    [PostgresFact]
    public async Task Launcher_CreaLaFilaEnElTenantDeLaCompania_ConRolYFicha_YElReenvioCierraLaAnterior()
    {
        await using var cn = await SeedAsync();

        await using (var ctx = NewContext())
        {
            var primero = await NewLauncher(ctx).LaunchAsync(Request(), Ct);
            primero.Outcome.Should().Be(MandateSignerIdentityLaunchOutcome.Sent);
        }

        await using (var ctx = NewContext())
        {
            var segundo = await NewLauncher(ctx).LaunchAsync(Request(), Ct);
            segundo.Outcome.Should().Be(MandateSignerIdentityLaunchOutcome.Sent);
        }

        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations WHERE mandate_signer_id = @s AND tenant_id = @t AND party_role = 'mandatario'",
            ("s", Signer), ("t", Company))).Should().Be(2);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations WHERE mandate_signer_id = @s AND status IN ('pendiente_envio','enviado','en_proceso')",
            ("s", Signer))).Should().Be(1, "la anterior en vuelo deja de contar");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations WHERE mandate_signer_id = @s AND status = 'expirado'",
            ("s", Signer))).Should().Be(1);
    }

    [PostgresFact]
    public async Task Launcher_DosReenviosSimultaneos_DejanUnaSolaValidacionActiva()
    {
        await using var cn = await SeedAsync();

        async Task<MandateSignerIdentityLaunchResult> Una()
        {
            await using var ctx = NewContext();
            return await NewLauncher(ctx).LaunchAsync(Request(), Ct);
        }

        var resultados = await Task.WhenAll(Una(), Una(), Una());

        resultados.Should().OnlyContain(r => r.Outcome != MandateSignerIdentityLaunchOutcome.Failed);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations WHERE mandate_signer_id = @s AND status IN ('pendiente_envio','enviado','en_proceso')",
            ("s", Signer))).Should().Be(1);
    }

    [PostgresFact]
    public async Task Ddl129_EsReversible_DownQuitaLaColumnaYRestauraLaUnicidadPorDocumento_UpLaVuelveAAplicar()
    {
        const string migrationId = "20261001211736_HU13246_MandatarioValidacionPropia";
        string previous;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            var idx = all.IndexOf(migrationId);
            idx.Should().BeGreaterThan(0, "la migración HU13246 debe estar descubierta por EF");
            previous = all[idx - 1];
        }

        try
        {
            await using (var ctx = NewContext())
            {
                await ctx.GetService<IMigrator>().MigrateAsync(previous, Ct);
            }

            await using var cn = await Fixture.OpenConnectionAsync();
            (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'tramites' AND table_name = 'procedure_instance_biometric_validations' AND column_name = 'mandate_signer_id'"))
                .Should().Be(0);
            (await ScalarAsync<string>(cn,
                "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_biometric_validations_inflight_doc_norm'"))
                .Should().NotContain("mandate_signer_id");
        }
        finally
        {
            await using var ctx = NewContext();
            await ctx.GetService<IMigrator>().MigrateAsync(null, Ct);
        }

        await using var cn2 = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<long>(cn2,
            "SELECT count(*) FROM pg_indexes WHERE indexname IN ('uq_biometric_validations_inflight_mandate_signer', 'ix_biometric_validations_mandate_signer')"))
            .Should().Be(2);
    }
}
