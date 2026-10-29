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
/// HU #14067 (Feature H, Épica #14021) contra Postgres real con TODAS las migraciones: la integración ligada a una
/// compañía (AC1), una sola ACTIVA por (compañía, tipo) con el índice parcial (AC2), bloqueo indefinido y desactivación
/// auditados (AC4), y que las filas históricas quedan <c>tipo='externa'</c> sin compañía.
/// </summary>
public sealed class ExternalIntegrationRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Compania = Guid.Parse("14067000-0000-4000-8000-000000000001");
    private static readonly Guid OtraCompania = Guid.Parse("14067000-0000-4000-8000-000000000002");
    private static readonly Argon2PasswordHasher Hasher = new();

    // Secreto de prueba generado para esta prueba; no es una credencial real.
    private const string Secreto = "it14067-secreto-de-prueba-3b8d1c"; // gitleaks:allow — valor de prueba

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
        {
            return;
        }

        await EjecutarAsync(
            $"""
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('{Compania}', 'Persona Natural (prueba)', 'PN-14067-1', 'RENTING', true, now()),
                   ('{OtraCompania}', 'Otra compañía (prueba)', 'PN-14067-2', 'RENTING', true, now())
            ON CONFLICT (id) DO NOTHING
            """);
    }

    private static NewExternalClient Nuevo(string clientId, Guid? compania, string tipo = "persona_natural") => new(
        clientId, "Portal Persona Natural", "Finalidad de prueba", Hasher.Hash(Secreto),
        tipo == "persona_natural" ? ["platform.tramites.persona_natural"] : [ExternalScopes.TramitesRead],
        CreatedBy: null, Tipo: tipo, CompanyId: compania);

    [PostgresFact]
    public async Task AC1_ElAltaQuedaLigadaALaCompaniaYSoloGuardaElHash()
    {
        var repo = new ExternalClientRepository(NewContext());

        var creado = await repo.CreateAsync(Nuevo("pn-it14067-alta", Compania));

        creado.CompanyId.Should().Be(Compania);
        creado.Tipo.Should().Be("persona_natural");
        creado.Scopes.Should().Equal("platform.tramites.persona_natural");
        (await new ExternalClientRepository(NewContext()).CompanyExistsAsync(Compania)).Should().BeTrue();
        (await new ExternalClientRepository(NewContext()).CompanyExistsAsync(Guid.NewGuid())).Should().BeFalse();

        var credenciales = await new ExternalClientRepository(NewContext()).GetCredentialsByClientIdAsync("pn-it14067-alta");
        credenciales!.Tipo.Should().Be("persona_natural");
        credenciales.SecretHash.Should().StartWith("argon2id|").And.NotContain(Secreto);
        (await EscalarAsync<string>($"SELECT to_jsonb(c)::text FROM integrations.external_clients c WHERE id = '{creado.Id}'"))
            .Should().NotContain(Secreto);
    }

    [PostgresFact]
    public async Task AC2_UnaSegundaActivaDelMismoTipoEnLaCompaniaSeRechaza_YEnOtraCompaniaNo()
    {
        await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-a", Compania));

        var repetir = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-b", Compania));

        (await repetir.Should().ThrowAsync<ExternalIntegrationAlreadyActiveException>()).Which.CompanyId.Should().Be(Compania);
        var enOtra = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-c", OtraCompania));
        enOtra.CompanyId.Should().Be(OtraCompania);
    }

    [PostgresFact]
    public async Task AC2_DesactivarLiberaElCupoYElBloqueoNo()
    {
        var primera = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-d", Compania));

        await new ExternalClientRepository(NewContext()).BlockAsync(primera.Id, null, DateTimeOffset.UtcNow);
        var conBloqueada = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-e", Compania));
        await conBloqueada.Should().ThrowAsync<ExternalIntegrationAlreadyActiveException>("bloquear no libera el cupo");

        await new ExternalClientRepository(NewContext()).UpdateAsync(
            primera.Id, new ExternalClientChanges(IsActive: false), null, DateTimeOffset.UtcNow);
        var segunda = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-e", Compania));

        segunda.IsActive.Should().BeTrue();
        (await new ExternalClientRepository(NewContext()).ListByCompanyAsync(Compania)).Should().HaveCount(2);
    }

    [PostgresFact]
    public async Task AC4_BloquearYDesactivarQuedanAuditadosConElAutor()
    {
        var actor = Guid.Parse("14067000-0000-4000-8000-0000000000aa");
        var creado = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-f", Compania));

        var bloqueada = await new ExternalClientRepository(NewContext()).BlockAsync(creado.Id, actor, DateTimeOffset.UtcNow);
        ExternalIntegrationRules.EstaBloqueadaIndefinidamente(bloqueada!).Should().BeTrue();
        await new ExternalClientRepository(NewContext()).UpdateAsync(
            creado.Id, new ExternalClientChanges(IsActive: false), actor, DateTimeOffset.UtcNow);

        var cambios = await EscalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_logs WHERE schema_name = 'integrations' AND record_id = '{creado.Id}' AND new_data::text LIKE '%{actor}%'");
        cambios.Should().BeGreaterThanOrEqualTo(2, "el bloqueo y la desactivación quedan auditados con updated_by");
    }

    [PostgresFact]
    public async Task AC3_RotarConserva24HorasAlAnteriorSalvoRevocacion()
    {
        var creado = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-g", Compania));
        var primero = Hasher.Hash("nuevo-1-it14067");

        await new ExternalClientRepository(NewContext()).ReplaceSecretAsync(creado.Id, primero, false, null, DateTimeOffset.UtcNow);
        var tras = await new ExternalClientRepository(NewContext()).GetCredentialsByClientIdAsync("pn-it14067-g");
        tras!.PreviousSecretHash.Should().NotBeNull("el anterior queda en ventana de gracia");
        Hasher.Verify(Secreto, tras.PreviousSecretHash!).Should().BeTrue();

        await new ExternalClientRepository(NewContext()).ReplaceSecretAsync(creado.Id, Hasher.Hash("nuevo-2-it14067"), true, null, DateTimeOffset.UtcNow);
        (await new ExternalClientRepository(NewContext()).GetCredentialsByClientIdAsync("pn-it14067-g"))!
            .PreviousSecretHash.Should().BeNull("revocar anula el anterior al instante");
    }

    [PostgresFact]
    public async Task LasFilasHistoricasQuedanExternasSinCompania_YLaListaGenericaNoMuestraLasDeCompania()
    {
        var externa = await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("flito-it14067", null, "externa"));
        await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-h", Compania));

        externa.Tipo.Should().Be("externa");
        externa.CompanyId.Should().BeNull();
        var genericos = await new ExternalClientRepository(NewContext()).ListAsync();
        genericos.Should().Contain(c => c.ClientId == "flito-it14067").And.NotContain(c => c.ClientId == "pn-it14067-h");
    }

    [PostgresFact]
    public async Task UnaPersonaNaturalSinCompania_ElCheckLaRechaza()
    {
        var sinCompania = async () => await new ExternalClientRepository(NewContext()).CreateAsync(Nuevo("pn-it14067-i", null));

        await sinCompania.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [PostgresFact]
    public async Task ReaplicarElUp_NoProduceError()
    {
        var up = new HU14067_IntegracionExternaPorCompania().UpOperations.OfType<SqlOperation>().Single().Sql;
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
