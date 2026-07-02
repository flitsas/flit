using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Uso de ejemplo:
/// var repo = new ImprontaRepository(context);
/// await repo.SaveAsync(new ImprontaGeneration { ... }, ct);
///
/// Tests del repositorio de historial de improntas (HU #10466 / ADR-0022) sobre proveedor
/// InMemory, con el patrón "act en un contexto, verify en otro contexto nuevo" usado en
/// <c>Flit.Admin.Tests/Companies/TransitOffices/CreateTransitOfficeHandlerTests</c>. El proveedor
/// InMemory no aplica CHECK/FK/RLS de Postgres — esas reglas de esquema se verificaron
/// directamente contra Postgres real al generar la migración (ver evidencia PASO 6, AC1/AC3):
/// FKs a <c>identity.tenants</c>/<c>identity.users</c>, UNIQUE <c>uq_impronta_generations_radicado</c>,
/// trigger de auditoría, ausencia de RLS y rollback (Down) sin objetos huérfanos. El CHECK
/// <c>ck_impronta_generations_identificador_vehiculo</c> que originalmente exigía al menos un
/// identificador se eliminó (migración <c>DropImprontaVehiculoIdentificadorCheck</c>) tras verificar
/// contra el proveedor real que Kyverum no lo requiere.
/// </summary>
public sealed class ImprontaRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FlitUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SaveAsync_ConDatosValidos_PersisteMetadataYPdfEnUnaSolaOperacion()
    {
        var db = NewDbName();
        var fechaImpresa = new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // "%PDF-1.4"

        var generation = new ImprontaGeneration
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Radicado = "IMPR-00000001",
            HashSha256 = new string('a', 64),
            FechaImpresa = fechaImpresa,
            Placa = "ABC123",
            NumMotor = "MTR-123",
            NumChasis = null,
            NumSerie = null,
            Marca = "Chevrolet",
            Linea = "Spark",
            Modelo = "2024",
            OrgNombre = "FLIT SAS",
            OrgNit = "900000000-1",
            OrgCiudad = "Bogotá",
            Operador = "Operador X",
            PdfContent = pdfBytes,
            PdfSizeBytes = pdfBytes.Length,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var act = NewContext(db))
        {
            var repository = new ImprontaRepository(act);
            await repository.SaveAsync(generation, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var persisted = await verify.ImprontaGenerations.SingleAsync(
            x => x.Radicado == "IMPR-00000001", TestContext.Current.CancellationToken);

        persisted.Id.Should().Be(generation.Id);
        persisted.TenantId.Should().Be(TenantId);
        persisted.FlitUserId.Should().Be(FlitUserId);
        persisted.HashSha256.Should().Be(generation.HashSha256);
        persisted.FechaImpresa.Should().Be(fechaImpresa);
        persisted.Placa.Should().Be("ABC123");
        persisted.NumMotor.Should().Be("MTR-123");
        persisted.NumChasis.Should().BeNull();
        persisted.NumSerie.Should().BeNull();
        persisted.OrgNombre.Should().Be("FLIT SAS");
        persisted.Operador.Should().Be("Operador X");
        persisted.PdfContent.Should().Equal(pdfBytes);
        persisted.PdfSizeBytes.Should().Be(pdfBytes.Length);
    }

    [Fact]
    public async Task SaveAsync_SinIdAsignado_GeneraUnNuevoIdAlPersistir()
    {
        var db = NewDbName();
        var generation = BuildValidGeneration(radicado: "IMPR-00000002");

        await using (var act = NewContext(db))
        {
            var repository = new ImprontaRepository(act);
            await repository.SaveAsync(generation, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var persisted = await verify.ImprontaGenerations.SingleAsync(
            x => x.Radicado == "IMPR-00000002", TestContext.Current.CancellationToken);

        persisted.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_ConSoloNumSerie_PersisteConLosDemasIdentificadoresNulos()
    {
        var db = NewDbName();
        var generation = BuildValidGeneration(radicado: "IMPR-00000003", numMotor: null, numSerie: "SER-999");

        await using (var act = NewContext(db))
        {
            var repository = new ImprontaRepository(act);
            await repository.SaveAsync(generation, TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        var persisted = await verify.ImprontaGenerations.SingleAsync(
            x => x.Radicado == "IMPR-00000003", TestContext.Current.CancellationToken);

        persisted.NumMotor.Should().BeNull();
        persisted.NumChasis.Should().BeNull();
        persisted.NumSerie.Should().Be("SER-999");
    }

    [Fact]
    public async Task SaveAsync_DosGeneracionesDistintas_QuedanAmbasPersistidasIndependientemente()
    {
        var db = NewDbName();

        await using (var act = NewContext(db))
        {
            var repository = new ImprontaRepository(act);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000004"), TestContext.Current.CancellationToken);
            await repository.SaveAsync(
                BuildValidGeneration(radicado: "IMPR-00000005"), TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext(db);
        (await verify.ImprontaGenerations.CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
    }

    // ---------- Helpers ----------

    private static ImprontaGeneration BuildValidGeneration(
        string radicado, string? numMotor = "MTR-123", string? numSerie = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Radicado = radicado,
            HashSha256 = new string('a', 64),
            FechaImpresa = DateTimeOffset.UtcNow,
            Placa = "ABC123",
            NumMotor = numMotor,
            NumSerie = numSerie,
            OrgNombre = "FLIT SAS",
            OrgNit = "900000000-1",
            OrgCiudad = "Bogotá",
            Operador = "Operador X",
            PdfContent = [0x25, 0x50, 0x44, 0x46],
            PdfSizeBytes = 4,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string NewDbName() => $"flit-impronta-generations-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
