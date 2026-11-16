using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — DDL 169 contra PostgreSQL real con TODAS las migraciones: las cinco columnas
/// <c>letter_*</c> existen, el modelo EF las lee y las escribe, la carta se guarda sobre una validación SIN trámite (la carta es
/// de la validación) y una validación de Kyverum las deja en NULL sin tocar <c>certificate_hash</c>.
/// </summary>
public sealed class CartaIdentidadPersistenciaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000000e2");
    private static readonly Guid Person = new("0199c200-0000-7000-8000-0000000000e3");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid Manual, Guid Kyverum)> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-C1", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var manual = Guid.NewGuid();
        var kyverum = Guid.NewGuid();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.persons (id, tenant_id, document_type, document_number, full_name, email, created_at)
            VALUES (@p, @t, 'CC', '1000000014', 'Persona de prueba', 'p@flit.test', now());
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, person_id, name, document_type, document_number, email, status, provider, approval_origin,
               token_hash, expires_at, created_at, validated_at, valid_until, certificate_hash)
            VALUES (@m, @t, @p, 'Persona', 'CC', '1000000014', 'p@flit.test', 'aprobado', 'manual', 'manual',
                    @h1, now() + interval '1 hour', now(), now(), now() + interval '30 days', NULL),
                   (@k, @t, @p, 'Persona', 'CC', '1000000014', 'p@flit.test', 'aprobado', 'kyverum', 'automatica',
                    @h2, now() + interval '1 hour', now(), now(), now() + interval '30 days', 'KV-SERIE');
            """,
            ("p", Person), ("t", Company), ("m", manual), ("k", kyverum),
            ("h1", Guid.NewGuid().ToString("N")), ("h2", Guid.NewGuid().ToString("N")));
        return (manual, kyverum);
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    [PostgresFact]
    public async Task LaCartaSeGuardaYSeLeeSobreUnaValidacionSinTramiteYLaDeKyverumQuedaIntacta()
    {
        var (manualId, kyverumId) = await SeedAsync();
        var now = new DateTimeOffset(2026, 11, 5, 15, 30, 0, TimeSpan.Zero);

        await using (var ctx = NewContext())
        {
            var v = await ctx.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().SingleAsync(x => x.Id == manualId, Ct);
            v.ProcedureInstanceId.Should().BeNull("la carta pertenece a la validación, también sin trámite");
            v.RegistrarCartaIdentidad("FL-3C9A71BE-04D5F2", new string('a', 64), new string('b', 64), "carta/ruta-opaca", now)
                .Should().BeTrue();
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var m = await ctx.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == manualId, Ct);
            m.CartaSerie.Should().Be("FL-3C9A71BE-04D5F2");
            m.CartaSha256.Should().Be(new string('a', 64));
            m.CartaPdfSha256.Should().Be(new string('b', 64));
            m.CartaPath.Should().Be("carta/ruta-opaca");
            m.CartaGeneratedAt.Should().Be(now);
            m.CertificateHash.Should().BeNull("la carta no usa la serie de Kyverum");

            var k = await ctx.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == kyverumId, Ct);
            k.TieneCartaIdentidad.Should().BeFalse();
            k.CartaSerie.Should().BeNull();
            k.CertificateHash.Should().Be("KV-SERIE");
        }
    }
}
