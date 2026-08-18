using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Bug #11583 (defectos 1 y 2) — <see cref="RepresentativeIdentityLookup"/> reimplementaba a mano el
/// empate por documento con igualdad exacta (sin normalizar) y excluía las prevalidaciones standalone
/// que <c>ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync</c> sí reconoce desde la HU
/// #10867. El resultado: un representante legal con identidad vigente REAL veía el trámite decir "Sin
/// identidad vigente".
/// </summary>
public sealed class RepresentativeIdentityLookupTests
{
    private static readonly Guid Tenant = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ProcedureInstanceBiometricValidation Vigente(
        string documentType,
        string documentNumber,
        Guid? procedureInstanceId) => new()
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

    /// <summary>Instancia NO eliminada para las validaciones que sí vienen ligadas a un trámite.</summary>
    private static ProcedureInstance InstanciaViva(Guid id) => new()
    {
        Id = id,
        TenantId = Tenant,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = $"TRM-{id:N}",
        Status = TramiteEstado.Borrador,
        CreatedAt = Now.AddDays(-2),
    };

    // ── Defecto 1 — sin normalización de llave ──────────────────────────────────

    [Fact]
    public async Task Defecto1_DocumentTypeEnMinusculasOConEspacios_SeReconoceComoLaMismaPersona()
    {
        var db = $"flit-rep-identity-{Guid.NewGuid()}";
        var instanceId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstance>().Add(InstanciaViva(instanceId));
            // La validación quedó grabada con 'cc' (minúscula) — dato real observado en la base local.
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                Vigente("cc", "1090123456", instanceId));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var lookup = new RepresentativeIdentityLookup(ctx);

        // El representante legal se busca con el tipo canónico 'CC' (mayúsculas).
        var result = await lookup.FindVigenteIdentityRefAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        result.Should().NotBeNull("es la MISMA persona; solo cambia el casing con el que se grabó");
    }

    [Fact]
    public async Task Defecto1_DocumentTypeConEspaciosAlrededor_SeReconoceComoLaMismaPersona()
    {
        var db = $"flit-rep-identity-{Guid.NewGuid()}";
        var instanceId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstance>().Add(InstanciaViva(instanceId));
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                Vigente(" Cc", "1090123456", instanceId));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var lookup = new RepresentativeIdentityLookup(ctx);

        var result = await lookup.FindVigenteIdentityRefAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
    }

    // ── Defecto 2 — prevalidaciones standalone invisibles ───────────────────────

    [Fact]
    public async Task Defecto2_PrevalidacionStandaloneSinTramite_EsVisibleParaElRepresentanteLegal()
    {
        // HU #10867: una validación puede no estar ligada a ningún trámite (ProcedureInstanceId == null).
        // ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync ya la reconocía; el lookup del
        // representante legal la descartaba con "ProcedureInstance != null".
        var db = $"flit-rep-identity-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                Vigente("CC", "1090123456", procedureInstanceId: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var lookup = new RepresentativeIdentityLookup(ctx);

        var result = await lookup.FindVigenteIdentityRefAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        result.Should().NotBeNull("la prevalidación standalone (sin trámite) SÍ cuenta como identidad vigente");
    }

    [Fact]
    public async Task Defecto2_ValidacionLigadaAUnaInstanciaNoEliminada_SigueVigente()
    {
        // Regresión: el caso "normal" (ligada a un trámite vivo) no debe romperse al ampliar la cláusula.
        var db = $"flit-rep-identity-{Guid.NewGuid()}";
        var instanceId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstance>().Add(InstanciaViva(instanceId));
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                Vigente("CC", "1090123456", instanceId));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var lookup = new RepresentativeIdentityLookup(ctx);

        var result = await lookup.FindVigenteIdentityRefAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DocumentoDistinto_NoAlcanzaAlRepresentante()
    {
        var db = $"flit-rep-identity-{Guid.NewGuid()}";
        await using (var seed = NewContext(db))
        {
            seed.Set<ProcedureInstanceBiometricValidation>().Add(
                Vigente("CC", "9999999999", procedureInstanceId: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var lookup = new RepresentativeIdentityLookup(ctx);

        var result = await lookup.FindVigenteIdentityRefAsync(
            Tenant, "CC", "1090123456", Now, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }
}
