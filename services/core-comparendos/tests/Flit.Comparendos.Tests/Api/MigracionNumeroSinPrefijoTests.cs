using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC punto 12 — la migración <c>GDC_NumeroComparendoSoloDigitos</c> sobre una base propia: se migra hasta la anterior,
/// se siembran choques y se aplica. Lo que choca no cambia y queda reportado; el documento OCR sigue la misma regla que
/// el comparendo. Sin Postgres se omite en local y falla en CI.
/// </summary>
public sealed class MigracionNumeroSinPrefijoTests : IAsyncLifetime
{
    private const string Anterior = "20261022193744_GDC_ReprocesoOcr";
    private const string Objetivo = "20261106094303_GDC_NumeroComparendoSoloDigitos";
    private static readonly Guid TenantA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");

    private string _database = string.Empty;
    private string? _omitir;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_cmp_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _omitir = $"Postgres no alcanzable: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_omitir is not null)
            return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_database) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(_database).Database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Limpia_LoQueNoChoca_YReporta_LoQueChoca_EnComparendosYDocumentosOcr()
    {
        Assert.SkipWhen(_omitir is not null, _omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(_database);
        var avisos = new List<string>();
        conexion.Notice += (_, e) => avisos.Add(e.Notice.MessageText);
        await conexion.OpenAsync(ct);
        var opciones = new DbContextOptionsBuilder<ComparendosDb>()
            .UseNpgsql(conexion, n => n.MigrationsHistoryTable("__EFMigrationsHistory", ComparendosDb.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ComparendosDb(opciones);
        var migrador = db.GetService<IMigrator>();
        await migrador.MigrateAsync(Anterior, cancellationToken: ct);

        var paquete = new PaqueteOcr { Id = Guid.NewGuid(), TenantId = TenantA, Nombre = "Lote", CreadoEn = DateTimeOffset.UtcNow };
        db.Add(paquete);
        // Tenant A: dos con prefijo distinto que quedarían iguales (choque), uno con prefijo cuyo número ya existe sin
        // prefijo (choque) y uno que no choca. Tenant B: el mismo número del choque de A, que en B no choca.
        db.AddRange(
            Comparendo(TenantA, "D1234567890"), Comparendo(TenantA, "A1234567890"),
            Comparendo(TenantA, "D9999999999"), Comparendo(TenantA, "9999999999"),
            Comparendo(TenantA, "D5555555555"), Comparendo(TenantA, "AV3"),
            Comparendo(TenantB, "D1234567890"));
        db.AddRange(
            Documento(paquete, "D1234567890"), Documento(paquete, "D5555555555"),
            Documento(paquete, "D7777777777"), Comparendo(TenantA, "7777777777"));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await migrador.MigrateAsync(Objetivo, cancellationToken: ct);

        var comparendos = await db.Set<Comparendo>().AsNoTracking().Select(c => new { c.TenantId, c.Numero, c.NumeroNormalizado }).ToListAsync(ct);
        string NumeroDe(Guid tenant, string numero) => comparendos.Single(c => c.TenantId == tenant && c.Numero == numero).NumeroNormalizado;
        NumeroDe(TenantA, "D1234567890").Should().Be("D1234567890");
        NumeroDe(TenantA, "A1234567890").Should().Be("A1234567890");
        NumeroDe(TenantA, "D9999999999").Should().Be("D9999999999");
        NumeroDe(TenantA, "D5555555555").Should().Be("5555555555");
        NumeroDe(TenantA, "AV3").Should().Be("AV3");
        NumeroDe(TenantB, "D1234567890").Should().Be("1234567890");

        var documentos = await db.DocumentosOcr.AsNoTracking().Select(d => new { d.NombreArchivo, d.NumeroNormalizado }).ToListAsync(ct);
        documentos.Single(d => d.NombreArchivo == "D1234567890.pdf").NumeroNormalizado.Should().Be("D1234567890", "su comparendo conserva el prefijo");
        documentos.Single(d => d.NombreArchivo == "D5555555555.pdf").NumeroNormalizado.Should().Be("5555555555");
        documentos.Single(d => d.NombreArchivo == "D7777777777.pdf").NumeroNormalizado.Should().Be("D7777777777", "ya hay un comparendo sin prefijo con ese número");

        var reporte = avisos.Where(a => a.StartsWith("GDC_NumeroComparendoSoloDigitos", StringComparison.Ordinal)).ToList();
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("D1234567890"));
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("A1234567890"));
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains("D9999999999"));
        reporte.Should().Contain(a => a.Contains("documento OCR sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("D1234567890"));
        reporte.Should().Contain(a => a.Contains("documento OCR sin cambiar") && a.Contains("D7777777777"));
        reporte.Should().NotContain(a => a.Contains(TenantB.ToString()));
        reporte.Should().Contain(a => a.Contains("2 comparendos y 1 documentos OCR sin prefijo; 3 comparendos y 2 documentos sin cambiar por choque"));
    }

    private static Comparendo Comparendo(Guid tenant, string numero) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        Numero = numero,
        NumeroNormalizado = numero,
        CreadoEn = DateTimeOffset.UtcNow,
    };

    private static DocumentoOcr Documento(PaqueteOcr paquete, string numero) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = paquete.TenantId,
        PaqueteId = paquete.Id,
        ArchivoId = Guid.NewGuid(),
        NombreArchivo = $"{numero}.pdf",
        NumeroComparendoExtraido = numero,
        NumeroNormalizado = numero,
    };
}
