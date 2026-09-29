using Flit.Admin.Application.Integrations.Auth;
using Flit.Admin.Application.Integrations.Clients;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13088 (Feature #13065, Épica #12737) contra Postgres real: el ciclo de administración con los
/// handlers, el repositorio y el Argon2id reales. El alta devuelve un secreto que sirve para pedir el pase
/// (AC1); desactivar, cambiar permisos, forzar la rotación, regenerar y desbloquear se aplican y quedan en
/// <c>audit.audit_logs</c> con el autor en <c>updated_by</c> y sin el secreto en claro (AC2).
/// </summary>
public sealed class ExternalClientAdminRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Finalidad = "Sincronización de trámites para procesos Flito";
    private static readonly Guid Admin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Argon2PasswordHasher Argon = new();

    [PostgresFact]
    public async Task AC1_ElSecretoDelAltaSirveParaPedirElPaseYNoSeGuarda()
    {
        var alta = await AltaAsync("it88-alta");

        (await Pase("it88-alta", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued);
        (await EscalarAsync<long>(
            $"SELECT count(*) FROM integrations.external_clients WHERE row_to_json(external_clients)::text LIKE '%{alta.ClientSecret}%'"))
            .Should().Be(0);
        (await EscalarAsync<Guid>($"SELECT created_by FROM integrations.external_clients WHERE id = '{alta.Client.Id}'"))
            .Should().Be(Admin);
    }

    [PostgresFact]
    public async Task AC2_DesactivarCambiarPermisosYForzarRotacion_SeAplicanYQuedanAuditados()
    {
        var alta = await AltaAsync("it88-editar");

        var (vista, error) = await new UpdateExternalClientHandler(Repo(), TimeProvider.System).HandleAsync(
            new(alta.Client.Id, null, null, [ExternalScopes.TramitesRead], IsActive: false, MustRotate: true, Admin));

        error.Should().BeNull();
        vista!.IsActive.Should().BeFalse();
        vista.MustRotate.Should().BeTrue();
        vista.Scopes.Should().Equal(ExternalScopes.TramitesRead);
        (await Pase("it88-editar", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.InvalidClient, "desactivado");

        (await EscalarAsync<string>($"""
            SELECT new_data->>'updated_by' FROM audit.audit_logs
             WHERE table_name = 'external_clients' AND record_id = '{alta.Client.Id}' AND action = 'U'
             ORDER BY 1 DESC LIMIT 1
            """)).Should().Be(Admin.ToString());
    }

    [PostgresFact]
    public async Task AC2_RegenerarMantieneElAnteriorEnLaVentanaYConRevocarLoAnula()
    {
        var alta = await AltaAsync("it88-regenerar");
        var regenerar = new RegenerateExternalClientSecretHandler(Repo(), Hasher(), TimeProvider.System);

        var planificada = (await regenerar.HandleAsync(alta.Client.Id, revokePrevious: false, Admin)).Result!;

        (await Pase("it88-regenerar", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued, "ventana de gracia");
        (await Pase("it88-regenerar", planificada.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued);

        var filtracion = (await new RegenerateExternalClientSecretHandler(Repo(), Hasher(), TimeProvider.System)
            .HandleAsync(alta.Client.Id, revokePrevious: true, Admin)).Result!;

        (await Pase("it88-regenerar", planificada.ClientSecret)).Status.Should().Be(ExternalTokenStatus.InvalidClient, "revocado al instante");
        (await Pase("it88-regenerar", filtracion.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued);
    }

    [PostgresFact]
    public async Task AC2_RegenerarLimpiaLaRotacionObligatoria()
    {
        var alta = await AltaAsync("it88-rotar");
        await new UpdateExternalClientHandler(Repo(), TimeProvider.System)
            .HandleAsync(new(alta.Client.Id, null, null, null, null, MustRotate: true, Admin));
        (await Pase("it88-rotar", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.RotationRequired);

        var nuevo = (await new RegenerateExternalClientSecretHandler(Repo(), Hasher(), TimeProvider.System)
            .HandleAsync(alta.Client.Id, revokePrevious: false, Admin)).Result!;

        nuevo.Client.MustRotate.Should().BeFalse();
        (await Pase("it88-rotar", nuevo.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued);
    }

    [PostgresFact]
    public async Task AC2_DesbloquearPermiteEntrarAntesDeLos15Minutos()
    {
        var alta = await AltaAsync("it88-desbloquear");
        for (var i = 0; i < 5; i++)
        {
            await Pase("it88-desbloquear", "equivocado");
        }

        (await Pase("it88-desbloquear", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Locked);

        var (vista, _) = await new UnlockExternalClientHandler(Repo(), TimeProvider.System).HandleAsync(alta.Client.Id, Admin);

        vista!.LockedUntil.Should().BeNull();
        (await Pase("it88-desbloquear", alta.ClientSecret)).Status.Should().Be(ExternalTokenStatus.Issued);
    }

    [PostgresFact]
    public async Task AC2_NingunSecretoEnClaroLlegaALaAuditoria()
    {
        var alta = await AltaAsync("it88-auditoria");
        var nuevo = (await new RegenerateExternalClientSecretHandler(Repo(), Hasher(), TimeProvider.System)
            .HandleAsync(alta.Client.Id, revokePrevious: false, Admin)).Result!;

        foreach (var secreto in new[] { alta.ClientSecret, nuevo.ClientSecret })
        {
            (await EscalarAsync<long>(
                $"SELECT count(*) FROM audit.audit_logs WHERE record_id = '{alta.Client.Id}' AND (coalesce(old_data::text, '') || coalesce(new_data::text, '')) LIKE '%{secreto}%'"))
                .Should().Be(0);
        }

        (await EscalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_logs WHERE table_name = 'external_clients' AND record_id = '{alta.Client.Id}'"))
            .Should().BeGreaterThanOrEqualTo(2, "alta y regeneración");
    }

    [PostgresFact]
    public async Task AC2_ElClienteInexistenteDevuelveNull()
    {
        var id = Guid.NewGuid();

        (await Repo().UpdateAsync(id, new ExternalClientChanges(IsActive: false), Admin, DateTimeOffset.UtcNow)).Should().BeNull();
        (await Repo().ReplaceSecretAsync(id, Argon.Hash("x"), false, Admin, DateTimeOffset.UtcNow)).Should().BeNull();
        (await Repo().UnlockAsync(id, Admin, DateTimeOffset.UtcNow)).Should().BeNull();
    }

    private ExternalClientRepository Repo() => new(NewContext());

    private static ExternalClientSecretHasher Hasher() => new(Argon);

    private async Task<ExternalClientSecretResult> AltaAsync(string clientId)
    {
        var (result, error) = await new CreateExternalClientHandler(Repo(), Hasher()).HandleAsync(
            new(clientId, "Flito (pruebas)", Finalidad, ExternalScopes.Todos, Admin));
        error.Should().BeNull();
        return result!;
    }

    private Task<IssueExternalClientTokenResult> Pase(string clientId, string secret) =>
        new IssueExternalClientTokenHandler(Repo(), Hasher(), new StubIssuer(), new ExternalClientAuthSettings(), TimeProvider.System)
            .HandleAsync(new(clientId, secret));

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class StubIssuer : IExternalClientTokenIssuer
    {
        public bool IsAvailable => true;

        public ExternalAccessToken Issue(string clientId, IReadOnlyList<string> scopes) => new("jwt", 1800);
    }
}
