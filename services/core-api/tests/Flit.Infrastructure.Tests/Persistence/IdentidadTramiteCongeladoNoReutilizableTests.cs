using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #13055 — la validación de identidad de un trámite anulado o revocado deja de contar a nivel
/// persona: ni sirve como identidad vigente para otros trámites (<c>FindVigenteApprovedByDocumentAsync</c>),
/// ni cuenta como envío en vuelo que bloquee uno nuevo (<c>ListInFlightByDocumentAsync</c>). La de un
/// trámite aprobado sigue siendo reutilizable.
/// </summary>
public sealed class IdentidadTramiteCongeladoNoReutilizableTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private const string Documento = "1090123456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TramiteEstado.Anulado, false)]
    [InlineData(TramiteEstado.Revocado, false)]
    [InlineData(TramiteEstado.Aprobado, true)]
    [InlineData(TramiteEstado.Entregado, true)]
    public async Task FindVigenteApproved_SoloReutilizaTramitesNoCongelados(string estadoTramite, bool reutilizable)
    {
        var db = await SembrarAsync(estadoTramite, BiometricEstados.Aprobado);
        await using var ctx = NewContext(db);

        var found = await new ProcedureInstanceRepository(ctx)
            .FindVigenteApprovedByDocumentAsync(Tenant, "CC", Documento, Now, Ct);

        (found is not null).Should().Be(reutilizable);
    }

    [Theory]
    [InlineData(TramiteEstado.Anulado, 0)]
    [InlineData(TramiteEstado.Revocado, 0)]
    [InlineData(TramiteEstado.Asignado, 1)]
    public async Task ListInFlight_NoCuentaLosEnviosDeTramitesCongelados(string estadoTramite, int esperados)
    {
        var db = await SembrarAsync(estadoTramite, BiometricEstados.EnProceso);
        await using var ctx = NewContext(db);

        var enVuelo = await new ProcedureInstanceRepository(ctx)
            .ListInFlightByDocumentAsync(Tenant, "CC", Documento, Ct);

        enVuelo.Should().HaveCount(esperados);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<string> SembrarAsync(string estadoTramite, string estadoValidacion)
    {
        var db = $"flit-identidad-congelada-{Guid.NewGuid()}";
        var instanceId = Guid.NewGuid();
        await using var seed = NewContext(db);
        seed.Set<ProcedureInstance>().Add(new ProcedureInstance
        {
            Id = instanceId,
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013055",
            Status = estadoTramite,
            CreatedAt = Now.AddDays(-2),
        });
        seed.Set<ProcedureInstanceBiometricValidation>().Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instanceId,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = estadoValidacion,
            Provider = BiometricProviders.Kyverum,
            ValidatedAt = estadoValidacion == BiometricEstados.Aprobado ? Now.AddDays(-1) : null,
            ValidUntil = estadoValidacion == BiometricEstados.Aprobado ? Now.AddDays(29) : null,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddDays(-1),
        });
        await seed.SaveChangesAsync(Ct);
        return db;
    }
}
