using System.Text;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Integrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13084 (Feature #13065) — esquema y mapeo de los clientes de integración externos.
/// <para>
/// Verifica que el DDL y el modelo de EF <i>digan</i> lo que deben; el comportamiento contra el motor
/// (AC1 a AC3) lo cubre <c>ExternalClientRepositoryTests</c> en Flit.Integration.Tests, contra Postgres 16.
/// </para>
/// </summary>
public sealed class ExternalClientsSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.125-HU13084-integrations-external-clients.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));
    }

    [Fact]
    public void LaTablaViveEnElSchemaIntegrationsSinCompaniaNiRls()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CREATE SCHEMA IF NOT EXISTS integrations;");
        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS integrations.external_clients (");
        ddl.Should().NotContain("tenant_id uuid", "no hay columna de compañía");
        ddl.Should().NotContain("ROW LEVEL SECURITY");
    }

    [Fact]
    public void ElIdentificadorEsUnicoYConFormato()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CONSTRAINT uq_external_clients_client_id UNIQUE (client_id)");
        ddl.Should().Contain("CONSTRAINT ck_external_clients_client_id_formato CHECK (client_id ~ '^[a-z0-9][a-z0-9-]{2,63}$')");
    }

    [Fact]
    public void LosHashesEstanMarcadosComoPiiAltaYNoHayColumnaParaElSecretoEnClaro()
    {
        var sql = LoadDdlConComentarios();

        sql.Should().Contain("'Hash Argon2id del secreto. El secreto en claro no se guarda en ningún sitio. @pii:high'");
        sql.Should().Contain("ventana de gracia de una rotación. @pii:high'");
        LoadDdl().Should().NotContain("client_secret").And.NotContain("secret text");
    }

    [Fact]
    public void LlevaLosTriggersEstandarDeRowVersionYAuditoria()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CREATE TRIGGER tr_external_clients_row_version BEFORE UPDATE ON integrations.external_clients");
        ddl.Should().Contain("CREATE TRIGGER tr_external_clients_audit AFTER INSERT OR UPDATE OR DELETE ON integrations.external_clients");
    }

    [Fact]
    public void ElModeloDeEfMapeaLaTablaSinFiltroDeCompania()
    {
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

        var entidad = db.Model.FindEntityType(typeof(ExternalClient))!;

        entidad.GetSchema().Should().Be("integrations");
        entidad.GetTableName().Should().Be("external_clients");
        entidad.GetDeclaredQueryFilters().Should().BeEmpty("es una entidad de plataforma, sin compañía");
        entidad.FindProperty(nameof(ExternalClient.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Theory]
    [InlineData("external.tramites.read", true)]
    [InlineData("external.tramites.pii.read", true)]
    [InlineData("external.tramites.attachments.write", true)]
    [InlineData("ict.transactions.write", false)]
    [InlineData(null, false)]
    public void LosPermisosValidosSonLosDelContrato(string? scope, bool valido) =>
        ExternalScopes.EsValido(scope).Should().Be(valido);

    private static string LoadDdlConComentarios()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
