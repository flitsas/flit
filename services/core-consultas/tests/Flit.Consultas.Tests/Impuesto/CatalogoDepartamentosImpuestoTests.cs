using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// La lista efectiva sale de <c>consultas.vehicle_tax_departments</c> (ADR-0080). Tras la migración
/// <c>ImpuestoDepartamentosFuenteReal</c> (D2) quedan activos 05/13/17/63/68/76; el resto queda apagado con borrado lógico.
/// </summary>
public sealed class CatalogoDepartamentosImpuestoTests : IAsyncLifetime
{
    private string _database = string.Empty;
    private ConsultasDb _db = null!;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core")
            ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
        await _db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
        if (_database.Length == 0)
            return;
        NpgsqlConnection.ClearAllPools();
        var borrada = new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options;
        await using var db = new ConsultasDb(borrada);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Tras_las_migraciones_quedan_activos_los_seis_departamentos_de_la_fuente_real()
    {
        var catalogo = Catalogo();
        var activos = await catalogo.ActivosAsync(CancellationToken.None);
        activos.Select(d => (d.Codigo, d.Nombre)).Should().Equal(
            ("05", "ANTIOQUIA"), ("13", "BOLIVAR"), ("17", "CALDAS"),
            ("63", "QUINDIO"), ("68", "SANTANDER"), ("76", "VALLE DEL CAUCA"));
        foreach (var apagado in new[] { "08", "11", "50", "52", "94" })
            (await catalogo.ActivoAsync(apagado, CancellationToken.None)).Should().BeFalse($"el {apagado} quedó fuera de la lista");
        (await _db.DepartamentosImpuesto.Where(d => !d.IsActive).AllAsync(d => d.DeletedAt != null, CancellationToken.None)).Should().BeTrue();
        (await _db.DepartamentosImpuesto.SingleAsync(d => d.DepartmentCode == "05", CancellationToken.None)).Id
            .Should().Be(Guid.Parse("01941215-0000-7000-8000-000000000005"));
    }

    [Fact]
    public async Task Guardar_enciende_un_departamento_conocido_y_apaga_otro_sin_borrarlo()
    {
        var catalogo = Catalogo();
        var creado = await catalogo.GuardarAsync("08", "ATLANTICO", true, CancellationToken.None);
        creado.Activo.Should().BeTrue();

        var apagado = await catalogo.GuardarAsync("05", "ANTIOQUIA", false, CancellationToken.None);
        apagado.Activo.Should().BeFalse();

        var activos = await catalogo.ActivosAsync(CancellationToken.None);
        activos.Select(d => d.Codigo).Should().Equal("08", "13", "17", "63", "68", "76");

        var todos = await catalogo.ListarTodosAsync(CancellationToken.None);
        todos.Should().Contain(d => d.Codigo == "05" && !d.Activo);
        todos.Should().Contain(d => d.Codigo == "11" && !d.Activo);
    }

    [Fact]
    public async Task Revertir_fuente_real_vuelve_a_05_08_11_activos()
    {
        var migrador = _db.GetService<IMigrator>();
        await migrador.MigrateAsync("20261117044000_ImpuestoDepartamentosActivos", cancellationToken: CancellationToken.None);
        var catalogo = Catalogo();
        (await catalogo.ActivosAsync(CancellationToken.None)).Select(d => d.Codigo).Should().Equal("05", "08", "11");

        await migrador.MigrateAsync(cancellationToken: CancellationToken.None);
        (await catalogo.ActivosAsync(CancellationToken.None)).Select(d => d.Codigo)
            .Should().Equal("05", "13", "17", "63", "68", "76");

        await migrador.MigrateAsync("20261117044000_ImpuestoDepartamentosActivos", cancellationToken: CancellationToken.None);
        (await catalogo.ActivosAsync(CancellationToken.None)).Select(d => d.Codigo).Should().Equal("05", "08", "11");
        (await _db.DepartamentosImpuesto.AnyAsync(d => d.DepartmentCode == "68" || d.DepartmentCode == "76", CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Volver_a_guardar_el_mismo_codigo_no_duplica_la_fila()
    {
        var catalogo = Catalogo();
        await catalogo.GuardarAsync("17", "CALDAS", true, CancellationToken.None);
        (await catalogo.ActivoAsync("17", CancellationToken.None)).Should().BeTrue();
        (await _db.DepartamentosImpuesto.CountAsync(d => d.DepartmentCode == "17", CancellationToken.None)).Should().Be(1);
    }

    [Theory]
    [InlineData("99", "NO EXISTE")]
    [InlineData("13", "")]
    [InlineData("13", "NOMBRE DEMASIADO LARGO PARA LA COLUMNA DE CINCUENTA!")]
    public async Task Un_codigo_fuera_de_la_homologacion_o_un_nombre_invalido_no_se_guarda(string codigo, string nombre)
    {
        var accion = async () => await Catalogo().GuardarAsync(codigo, nombre, true, CancellationToken.None);
        await accion.Should().ThrowAsync<InvalidOperationException>();
        (await _db.DepartamentosImpuesto.AnyAsync(d => d.DepartmentCode == "99", CancellationToken.None)).Should().BeFalse();
    }

    private CatalogoDepartamentosImpuesto Catalogo() => new(_db, TimeProvider.System);
}
