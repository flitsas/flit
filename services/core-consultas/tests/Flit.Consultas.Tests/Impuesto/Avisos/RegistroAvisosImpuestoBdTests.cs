using Flit.Consultas.Api.Impuesto.Avisos;
using Flit.Consultas.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// HU #14711 — persistencia del receptor contra Postgres real (base efímera migrada): el <c>INSERT … ON CONFLICT DO NOTHING</c> de
/// <c>consultas.vehicle_tax_webhook_deliveries</c> deduplica por (empresa, entrega), el estado de la consulta se lee de
/// <c>vehicle_tax_jobs</c> solo para la empresa dueña, y la purga borra solo lo anterior al corte.
/// <para>Uso de ejemplo: <c>await registro.RegistrarEntregaAsync(empresa, "d-1", "ext-1", 1, ct)</c> → true la primera vez.</para>
/// </summary>
public sealed class RegistroAvisosImpuestoBdTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private string _database = string.Empty;
    private string? _skip;
    private ServiceProvider? _servicios;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync(Ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync(Ct);
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        _servicios = new ServiceCollection()
            .AddDbContext<ConsultasDb>(o => ConsultasDb.Configure(o, _database))
            .BuildServiceProvider();
        await using var scope = _servicios.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Database.MigrateAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_servicios is not null)
            await _servicios.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private RegistroAvisosImpuestoBd Registro(DateTimeOffset? ahora = null) =>
        new(_servicios!.GetRequiredService<IServiceScopeFactory>(), new RelojFalso(ahora ?? Ahora));

    private async Task SembrarTrabajo(Guid empresa, string idExterno, string status)
    {
        await using var scope = _servicios!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        db.TrabajosImpuesto.Add(new TrabajoImpuestoExterno
        {
            Id = Guid.CreateVersion7(), TrabajoId = Guid.NewGuid(), TenantId = empresa, DepartmentCode = "05", ExternalId = idExterno,
            Status = status, CreatedAt = Ahora, UpdatedAt = Ahora,
        });
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task OnConflictDoNothing_DeduplicaPorEmpresaYEntrega()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var registro = Registro();

        (await registro.RegistrarEntregaAsync(_empresa, "d-1", "ext-1", 1, Ct)).Should().BeTrue();
        (await registro.RegistrarEntregaAsync(_empresa, "d-1", "ext-1", 1, Ct)).Should().BeFalse();
        (await registro.RegistrarEntregaAsync(Guid.NewGuid(), "d-1", "ext-1", 1, Ct)).Should().BeTrue("otra empresa no colisiona");

        await using var scope = _servicios!.CreateAsyncScope();
        var filas = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().EntregasAvisoImpuesto.AsNoTracking()
            .Where(e => e.TenantId == _empresa).ToListAsync(Ct);
        filas.Should().ContainSingle().Which.Should().Match<EntregaAvisoImpuesto>(e =>
            e.DeliveryId == "d-1" && e.ExternalId == "ext-1" && e.Attempt == 1 && e.CreatedAt == Ahora);
    }

    [Fact]
    public async Task EstadoConsulta_SoloParaLaEmpresaDuena()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await SembrarTrabajo(_empresa, "ext-abierta", "queued");
        await SembrarTrabajo(_empresa, "ext-cerrada", "succeeded");
        var registro = Registro();

        (await registro.EstadoConsultaAsync(_empresa, "ext-abierta", Ct)).Should().Be(EstadoConsultaAviso.Abierta);
        (await registro.EstadoConsultaAsync(_empresa, "ext-cerrada", Ct)).Should().Be(EstadoConsultaAviso.Cerrada);
        (await registro.EstadoConsultaAsync(Guid.NewGuid(), "ext-abierta", Ct)).Should().Be(EstadoConsultaAviso.NoEncontrada);
        (await registro.EstadoConsultaAsync(_empresa, "ext-inexistente", Ct)).Should().Be(EstadoConsultaAviso.NoEncontrada);
    }

    [Fact]
    public async Task Purga_BorraSoloLoAnteriorAlCorte()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Registro(Ahora.AddDays(-8)).RegistrarEntregaAsync(_empresa, "vieja", "ext-1", 1, Ct);
        await Registro().RegistrarEntregaAsync(_empresa, "nueva", "ext-1", 2, Ct);

        var borradas = await Registro().PurgarAsync(Ahora.AddDays(-7), Ct);

        borradas.Should().Be(1);
        (await Registro().RegistrarEntregaAsync(_empresa, "vieja", "ext-1", 1, Ct)).Should().BeTrue("la purgada ya no deduplica");
        (await Registro().RegistrarEntregaAsync(_empresa, "nueva", "ext-1", 2, Ct)).Should().BeFalse();
    }
}
