using FluentAssertions;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14032 (Feature #14022, Épica #14021) — el esquema del portal queda completo tras sus migraciones: tablas,
/// llaves foráneas con índice, auditoría de tiempos y CHECK de estados y de vehículo. Usa una base efímera propia en el
/// Postgres de <c>ConnectionStrings__Core</c> (sin motor: se omite en local y falla en CI).
/// </summary>
public sealed class EsquemaDelPortalTests : IAsyncLifetime
{
    private static readonly string[] TablasDelPortal =
    [
        "procedure_request", "requester_person", "attachment", "procedure_session", "query_quota",
        "consent", "status_history", "pii_access_log", "resume_attempt", "retention_config", "identity_audit", "outbox", "inbox",
    ];

    private string _database = string.Empty;
    private string? _skip;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pn_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        await using var db = NuevaDb();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_skip is not null)
            return;
        NpgsqlConnection.ClearAllPools();
        await using var db = NuevaDb();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task LasMigraciones_CreanTodasLasTablasDelPortal_ConSuPropioEsquema()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var tablas = await ConsultarAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @s", ("s", PersonaNaturalDb.Schema));

        tablas.Should().Contain(TablasDelPortal);
        (await ConsultarAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_name IN ('procedure_instances', 'transit_offices')"))
            .Should().BeEmpty("el servicio solo es dueño de su esquema y nunca crea ni toca tablas de FLIT 2");
    }

    [Fact]
    public async Task CadaLlaveForanea_TieneSuIndice()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var sinIndice = await ConsultarAsync<string>(
            """
            SELECT c.conrelid::regclass::text || '.' || c.conname
            FROM pg_constraint c
            WHERE c.contype = 'f' AND c.connamespace = @s::regnamespace
              AND NOT EXISTS (
                SELECT 1 FROM pg_index i
                WHERE i.indrelid = c.conrelid AND (i.indkey::int2[])[0:cardinality(c.conkey) - 1] = c.conkey)
            """,
            ("s", PersonaNaturalDb.Schema));

        sinIndice.Should().BeEmpty("toda llave foránea lleva índice (checklist de datos)");
    }

    [Theory]
    [InlineData("borrador", true)]
    [InlineData("pendiente_validacion", true)]
    [InlineData("en_cola", true)]
    [InlineData("creada", true)]
    [InlineData("rechazada_creacion", true)]
    [InlineData("cancelada", true)]
    [InlineData("cerrada", false)]
    [InlineData("", false)]
    public async Task ElCheckDeEstado_SoloAceptaLosEstadosDefinidos(string estado, bool valido)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await using var db = NuevaDb();
        db.Add(Solicitud(estado, placa: "ABC123"));

        var act = () => db.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (valido)
            await act.Should().NotThrowAsync();
        else
            (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
                .Which.ConstraintName.Should().Be("ck_procedure_request_status");
    }

    [Fact]
    public async Task ElCheckDeVehiculo_ExigePlacaOVin()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await using var sinNada = NuevaDb();
        sinNada.Add(Solicitud("borrador"));
        (await ((Func<Task>)(() => sinNada.SaveChangesAsync(TestContext.Current.CancellationToken))).Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("ck_procedure_request_vehicle");

        await using var soloVin = NuevaDb();
        soloVin.Add(Solicitud("borrador", vin: "9BWZZZ377VT004251"));
        await ((Func<Task>)(() => soloVin.SaveChangesAsync(TestContext.Current.CancellationToken))).Should().NotThrowAsync(
            "los tipos que entran por VIN, como la matrícula nueva, no tienen placa");
    }

    [Fact]
    public async Task LaLlaveDeIdempotencia_NoSeRepiteDentroDeLaEmpresa_PeroVariasSinLlaveConviven()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var empresa = Guid.NewGuid();
        await using var db = NuevaDb();
        var a = Solicitud("borrador", placa: "AAA111"); a.TenantId = empresa; a.IdempotencyKey = "k-1";
        var b = Solicitud("borrador", placa: "BBB222"); b.TenantId = empresa; b.IdempotencyKey = "k-1";
        db.Add(a);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var db2 = NuevaDb();
        db2.Add(b);
        (await ((Func<Task>)(() => db2.SaveChangesAsync(TestContext.Current.CancellationToken))).Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("uq_procedure_request_idempotency");

        await using var db3 = NuevaDb();
        var c = Solicitud("borrador", placa: "CCC333"); c.TenantId = empresa;
        var d = Solicitud("borrador", placa: "DDD444"); d.TenantId = empresa;
        db3.AddRange(c, d);
        await ((Func<Task>)(() => db3.SaveChangesAsync(TestContext.Current.CancellationToken))).Should().NotThrowAsync();
    }

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private static SolicitudTramite Solicitud(string estado, string? placa = null, string? vin = null) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.NewGuid(),
        CodigoPublico = $"PN-{Guid.NewGuid():N}"[..24],
        TipoTramiteCodigo = "TRASPASO_STANDARD",
        DefinicionVersion = 1,
        OrganismoId = Guid.NewGuid(),
        OrganismoNombre = "Organismo de prueba",
        Placa = placa,
        Vin = vin,
        Estado = estado,
        CedulaSolicitanteHash = [1, 2, 3],
        CorrelationId = Guid.NewGuid(),
    };

    private async Task<List<T>> ConsultarAsync<T>(string sql, params (string Name, object Value)[] parametros)
    {
        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parametros)
            command.Parameters.AddWithValue(name, value);
        var filas = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            filas.Add(reader.GetFieldValue<T>(0));
        return filas;
    }
}
