using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13084 (Feature #13065, Épica #12737) contra Postgres real con TODAS las migraciones: alta de un
/// cliente de integración con el secreto solo como hash Argon2id (AC1), identificador único (AC2) y el
/// secreto en claro ausente de la tabla y de la auditoría (AC3).
/// </summary>
public sealed class ExternalClientRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    // Secreto de prueba generado para esta prueba; no es una credencial real.
    private const string Secreto = "it13084-secreto-de-prueba-9f2c7a"; // gitleaks:allow — valor de prueba
    private static readonly Argon2PasswordHasher Hasher = new();

    private static NewExternalClient Nuevo(string clientId, string? hash = null) => new(
        clientId,
        "Flito (pruebas)",
        "Sincronización de trámites para procesos Flito",
        hash ?? Hasher.Hash(Secreto),
        [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead],
        CreatedBy: null);

    [PostgresFact]
    public async Task AC1_ElAltaGuardaElClienteActivoSinCompaniaYConElSecretoSoloComoHash()
    {
        await using var ctx = NewContext();
        var repo = new ExternalClientRepository(ctx);

        var creado = await repo.CreateAsync(Nuevo("flito-dev"));

        creado.ClientId.Should().Be("flito-dev");
        creado.IsActive.Should().BeTrue();
        creado.Scopes.Should().Equal(ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead);

        var credenciales = await new ExternalClientRepository(NewContext()).GetCredentialsByClientIdAsync("flito-dev");
        credenciales.Should().NotBeNull();
        credenciales!.SecretHash.Should().StartWith("argon2id|").And.NotContain(Secreto);
        Hasher.Verify(Secreto, credenciales.SecretHash).Should().BeTrue();

        (await EscalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'integrations' AND table_name = 'external_clients' AND column_name = 'tenant_id'"))
            .Should().Be(0, "el cliente no pertenece a ninguna compañía");
    }

    [PostgresFact]
    public async Task AC2_ElMismoIdentificadorSeRechaza()
    {
        await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-qa"));

        var repetir = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-qa"));

        (await repetir.Should().ThrowAsync<ExternalClientAlreadyExistsException>()).Which.ClientId.Should().Be("flito-qa");
    }

    [PostgresFact]
    public async Task AC2_UnIdentificadorDadoDeBajaNoSeReutiliza()
    {
        var creado = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-pdn"));
        await EjecutarAsync($"UPDATE integrations.external_clients SET deleted_at = now() WHERE id = '{creado.Id}'");

        var repetir = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-pdn"));

        await repetir.Should().ThrowAsync<ExternalClientAlreadyExistsException>();
        (await new ExternalClientRepository(NewContext()).ListAsync()).Should().NotContain(c => c.ClientId == "flito-pdn");
        (await new ExternalClientRepository(NewContext()).GetCredentialsByClientIdAsync("flito-pdn")).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC3_ElSecretoEnClaroNoApareceEnLaTablaNiEnLaAuditoria()
    {
        var creado = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-audit"));

        var fila = await EscalarAsync<string>(
            $"SELECT to_jsonb(c)::text FROM integrations.external_clients c WHERE id = '{creado.Id}'");
        var auditoria = await EscalarAsync<string>(
            $"SELECT coalesce(string_agg(coalesce(new_data::text, '') || coalesce(old_data::text, ''), ''), '') FROM audit.audit_logs WHERE schema_name = 'integrations' AND record_id = '{creado.Id}'");

        fila.Should().NotContain(Secreto);
        auditoria.Should().NotBeEmpty("el alta queda auditada").And.NotContain(Secreto);
    }

    [PostgresFact]
    public async Task ElIdentificadorDebeTenerElFormatoAcordado()
    {
        var invalido = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("Flito Dev!"));

        await invalido.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [PostgresFact]
    public async Task ReaplicarElUp_NoProduceError()
    {
        var up = new HU13084_ExternalClients().UpOperations.OfType<SqlOperation>().Single().Sql;
        await EjecutarAsync(up);
        await EjecutarAsync(up);
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
