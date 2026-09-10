using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #11583 (defecto 1) — <c>ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync</c> y
/// <c>ListInFlightByDocumentAsync</c> comparaban tipo/número de documento con igualdad exacta, sin
/// <c>Trim</c>/<c>Upper</c>. Una validación aprobada con <c>document_type = "cc"</c> quedaba invisible
/// para el gate que decide si hace falta pedir una nueva validación de identidad (consumido por
/// <c>PutActorsHandler</c>, el camino real detrás de "NIT sin representante en el directorio" — HU
/// #11195/#11265), aunque fuera la MISMA persona que el <c>"CC"</c> canónico del representante del
/// trámite.
/// </summary>
public sealed class ProcedureInstanceVigenteIdentityNormalizationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstanceBiometricValidation VigenteAprobada(
        string documentType, string documentNumber, Guid? procedureInstanceId) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = procedureInstanceId,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddDays(-1),
        };

    [Fact]
    public async Task FindVigenteApprovedByDocumentAsync_DocumentTypeEnMinusculas_SeReconoceComoLaMismaPersona()
    {
        var db = $"flit-vigente-norm-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            // Grabada con 'cc' minúscula — dato real observado en la base local (13 vs 15 personas al
            // normalizar).
            // Standalone (sin trámite ligado) para aislar el defecto de normalización del de
            // prevalidaciones — no hace falta sembrar un ProcedureInstance para esta aserción.
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                VigenteAprobada("cc", "1090123456", procedureInstanceId: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        var found = await repo.FindVigenteApprovedByDocumentAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        found.Should().NotBeNull("es la misma persona; el gate no debe volver a pedirle la validación");
    }

    [Fact]
    public async Task FindVigenteApprovedByDocumentAsync_DocumentNumberConEspacios_SeReconoceComoLaMismaPersona()
    {
        var db = $"flit-vigente-norm-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                VigenteAprobada("CC", " 1090123456 ", procedureInstanceId: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        var found = await repo.FindVigenteApprovedByDocumentAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task ListInFlightByDocumentAsync_DocumentTypeEnMinusculas_SeReconoceComoElMismoCandidato()
    {
        var db = $"flit-vigente-norm-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstanceBiometricValidation>().Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                ProcedureInstanceId = null,
                DocumentType = "cc",
                DocumentNumber = "1090123456",
                Status = BiometricEstados.EnProceso,
                Provider = BiometricProviders.Kyverum,
                TokenHash = "hash",
                ExpiresAt = Now.AddHours(1),
                CreatedAt = Now.AddDays(-1),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        var inFlight = await repo.ListInFlightByDocumentAsync(
            Tenant, "CC", "1090123456", TestContext.Current.CancellationToken);

        inFlight.Should().ContainSingle("hay una validación en vuelo de la MISMA persona");
    }

    [Fact]
    public async Task FindVigenteApprovedByDocumentAsync_DocumentoDistinto_NoEmpata()
    {
        var db = $"flit-vigente-norm-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                VigenteAprobada("CC", "9999999999", Guid.NewGuid()));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        var found = await repo.FindVigenteApprovedByDocumentAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }
}
