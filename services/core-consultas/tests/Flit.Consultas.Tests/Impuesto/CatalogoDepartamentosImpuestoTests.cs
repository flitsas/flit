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
/// <c>ImpuestoDepartamentosTodosActivos</c> (P5' del 2026-11-23) quedan activos los 33 códigos DANE y la cobertura real la
/// decide la API de impuesto. Revertirla vuelve al estado de <c>ImpuestoDepartamentosFuenteReal</c> (05/13/17/63/68/76).
/// <para>Uso de ejemplo: <c>await new CatalogoDepartamentosImpuesto(db, TimeProvider.System).ActivosAsync(ct)</c>.</para>
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

    private const string FuenteReal = "20261120002000_ImpuestoDepartamentosFuenteReal";

    [Fact]
    public async Task Tras_las_migraciones_quedan_activos_los_33_codigos_DANE()
    {
        var catalogo = Catalogo();
        var activos = await catalogo.ActivosAsync(CancellationToken.None);
        activos.Select(d => d.Codigo).Should().Equal(HomologacionDocumentoImpuestoTests.CodigosDane);
        activos.Should().Contain(d => d.Codigo == "08" && d.Nombre == "ATLANTICO")
            .And.Contain(d => d.Codigo == "11" && d.Nombre == "BOGOTA D.C.")
            .And.Contain(d => d.Codigo == "54" && d.Nombre == "NORTE DE SANTANDER")
            .And.Contain(d => d.Codigo == "88" && d.Nombre == "SAN ANDRES")
            .And.Contain(d => d.Codigo == "99" && d.Nombre == "VICHADA");
        activos.Should().OnlyContain(d => d.Nombre.Length > 0 && d.Nombre.Length <= 50);
        (await _db.DepartamentosImpuesto.AllAsync(d => d.IsActive && d.DeletedAt == null, CancellationToken.None)).Should().BeTrue();
        (await _db.DepartamentosImpuesto.SingleAsync(d => d.DepartmentCode == "05", CancellationToken.None)).Id
            .Should().Be(Guid.Parse("01941215-0000-7000-8000-000000000005"));
        (await _db.DepartamentosImpuesto.SingleAsync(d => d.DepartmentCode == "15", CancellationToken.None)).Id
            .Should().Be(Guid.Parse("01941215-0000-7000-8000-000000000015"));
    }

    [Fact]
    public async Task Revertir_todos_activos_vuelve_a_los_seis_de_la_fuente_real_y_borra_solo_lo_que_inserto()
    {
        var migrador = _db.GetService<IMigrator>();
        await migrador.MigrateAsync(FuenteReal, cancellationToken: CancellationToken.None);
        var catalogo = Catalogo();
        (await catalogo.ActivosAsync(CancellationToken.None)).Select(d => d.Codigo)
            .Should().Equal("05", "13", "17", "63", "68", "76");
        (await _db.DepartamentosImpuesto.Select(d => d.DepartmentCode).OrderBy(c => c).ToListAsync(CancellationToken.None))
            .Should().Equal("05", "08", "11", "13", "17", "50", "52", "63", "68", "76", "94");
        (await _db.DepartamentosImpuesto.Where(d => !d.IsActive).AllAsync(d => d.DeletedAt != null, CancellationToken.None)).Should().BeTrue();

        await migrador.MigrateAsync(cancellationToken: CancellationToken.None);
        (await catalogo.ActivosAsync(CancellationToken.None)).Should().HaveCount(33);
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
        activos.Select(d => d.Codigo).Should().Equal(HomologacionDocumentoImpuestoTests.CodigosDane.Where(c => c != "05"));

        var todos = await catalogo.ListarTodosAsync(CancellationToken.None);
        todos.Should().Contain(d => d.Codigo == "05" && !d.Activo);
        todos.Should().Contain(d => d.Codigo == "11" && d.Activo);
    }

    [Fact]
    public async Task Revertir_fuente_real_vuelve_a_05_08_11_activos()
    {
        var migrador = _db.GetService<IMigrator>();
        await migrador.MigrateAsync("20261117044000_ImpuestoDepartamentosActivos", cancellationToken: CancellationToken.None);
        var catalogo = Catalogo();
        (await catalogo.ActivosAsync(CancellationToken.None)).Select(d => d.Codigo).Should().Equal("05", "08", "11");

        await migrador.MigrateAsync(FuenteReal, cancellationToken: CancellationToken.None);
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
    [InlineData("12", "NO EXISTE")]
    [InlineData("13", "")]
    [InlineData("13", "NOMBRE DEMASIADO LARGO PARA LA COLUMNA DE CINCUENTA!")]
    public async Task Un_codigo_fuera_de_la_homologacion_o_un_nombre_invalido_no_se_guarda(string codigo, string nombre)
    {
        var accion = async () => await Catalogo().GuardarAsync(codigo, nombre, true, CancellationToken.None);
        await accion.Should().ThrowAsync<InvalidOperationException>();
        (await _db.DepartamentosImpuesto.AnyAsync(d => d.DepartmentCode == "12", CancellationToken.None)).Should().BeFalse();
    }

    private CatalogoDepartamentosImpuesto Catalogo() => new(_db, TimeProvider.System);
}
