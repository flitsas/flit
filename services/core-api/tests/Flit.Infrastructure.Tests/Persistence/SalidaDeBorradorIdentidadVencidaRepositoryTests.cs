using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13824 (regresión FT9-0000001) — consultas del repositorio que sostienen la regla «una identidad vencida a mano solo
/// vale para el trámite que salió de borrador antes del vencimiento»: salida de borrador (una por trámite y en lote), y las
/// identidades protegidas con su vencimiento manual más reciente por llave (basta que CUALQUIERA cumpla).
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>await new ProcedureInstanceRepository(ctx).GetSalidaDeBorradorAtAsync(id, tenant, ct)</c> ⇒ el
/// <c>changed_at</c> de la primera fila con <c>from_status = 'borrador'</c>, o <c>null</c> si no hay (legado).
/// </remarks>
public sealed class SalidaDeBorradorIdentidadVencidaRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ProcedureInstance Tramite(string estado = TramiteEstado.Preasignacion, Guid? tenant = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant ?? Tenant, ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = $"FT1-{Guid.NewGuid():N}"[..14], Status = estado, CreatedAt = Now.AddDays(-30),
    };

    private static ProcedureInstanceStatusHistory Historial(ProcedureInstance t, string? from, string to, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), TenantId = t.TenantId, ProcedureInstanceId = t.Id, FromStatus = from, ToStatus = to, ChangedAt = at,
    };

    private static ProcedureInstanceBiometricValidation Vencida(DateTimeOffset vencidaA, string numero = "9000000915")
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = Tenant, ProcedureInstanceId = null, PartyRole = null,
            Status = BiometricEstados.Aprobado, Provider = BiometricProviders.Kyverum, DocumentType = "CC", DocumentNumber = numero,
            TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = Now.AddDays(-20), CreatedAt = Now.AddDays(-40),
            ValidatedAt = Now.AddDays(-29), ValidUntil = Now.AddDays(1),
        };
        v.VencerManualmente(vencidaA);
        return v;
    }

    private static async Task<string> SembrarAsync(
        IEnumerable<ProcedureInstance> tramites,
        IEnumerable<ProcedureInstanceStatusHistory> historial,
        IEnumerable<ProcedureInstanceBiometricValidation>? validaciones = null)
    {
        var db = $"flit-13824-salida-{Guid.NewGuid()}";
        await using var seed = NewContext(db);
        seed.Set<ProcedureInstance>().AddRange(tramites);
        seed.Set<ProcedureInstanceStatusHistory>().AddRange(historial);
        if (validaciones is not null)
            seed.Set<ProcedureInstanceBiometricValidation>().AddRange(validaciones);
        await seed.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task GetSalidaDeBorradorAtAsync_DevuelveLaPrimeraSalida_YNullSinHistorial()
    {
        var conSalida = Tramite();
        var sinHistorial = Tramite();
        var salida = Now.AddHours(-3);
        var db = await SembrarAsync(
            [conSalida, sinHistorial],
            [
                Historial(conSalida, null, TramiteEstado.Borrador, Now.AddHours(-4)),
                Historial(conSalida, TramiteEstado.Borrador, TramiteEstado.Preparado, salida),
                Historial(conSalida, TramiteEstado.Preparado, TramiteEstado.Preasignacion, Now.AddHours(-2)),
            ]);

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        (await repo.GetSalidaDeBorradorAtAsync(conSalida.Id, Tenant, Ct)).Should().Be(salida);
        (await repo.GetSalidaDeBorradorAtAsync(sinHistorial.Id, Tenant, Ct)).Should().BeNull();
        (await repo.GetSalidaDeBorradorAtAsync(conSalida.Id, Guid.NewGuid(), Ct)).Should().BeNull("otro tenant no ve el historial");
    }

    [Fact]
    public async Task ListSalidasDeBorradorAsync_ResuelveVariosTramitesEnLote_SinLosQueNoSalieron()
    {
        var a = Tramite();
        var b = Tramite();
        var sinSalida = Tramite(TramiteEstado.Borrador);
        var db = await SembrarAsync(
            [a, b, sinSalida],
            [
                Historial(a, TramiteEstado.Borrador, TramiteEstado.Preparado, Now.AddDays(-2)),
                Historial(b, TramiteEstado.Borrador, TramiteEstado.Preparado, Now.AddDays(-1)),
                Historial(b, TramiteEstado.Borrador, TramiteEstado.Preparado, Now.AddHours(-1)), // defensa: gana la primera
                Historial(sinSalida, null, TramiteEstado.Borrador, Now.AddDays(-3)),
            ]);

        await using var ctx = NewContext(db);
        var salidas = await new ProcedureInstanceRepository(ctx)
            .ListSalidasDeBorradorAsync([Tenant], [a.Id, b.Id, sinSalida.Id], Ct);

        salidas.Should().HaveCount(2);
        salidas[a.Id].Should().Be(Now.AddDays(-2));
        salidas[b.Id].Should().Be(Now.AddDays(-1));
        (await new ProcedureInstanceRepository(ctx).ListSalidasDeBorradorAsync([Tenant], [], Ct)).Should().BeEmpty();
        (await new ProcedureInstanceRepository(ctx).ListSalidasDeBorradorAsync([Guid.NewGuid()], [a.Id, b.Id], Ct))
            .Should().BeEmpty("otro tenant no ve las salidas");
    }

    [Fact]
    public async Task ListProtegidasPorVencimientoManualKeysAsync_DaTodasLasIdentidadesVencidasPorLlave()
    {
        var antigua = Vencida(Now.AddDays(-20));
        var reciente = Vencida(Now.AddDays(-5));
        var otraPersona = Vencida(Now.AddDays(-9), numero: "9000000999");
        var db = await SembrarAsync([], [], [antigua, reciente, otraPersona]);

        await using var ctx = NewContext(db);
        var protegidas = await new ProcedureInstanceRepository(ctx)
            .ListProtegidasPorVencimientoManualKeysAsync([Tenant], Now, Ct);

        protegidas.Should().HaveCount(2);
        protegidas[BiometricRules.IdentidadKey(Tenant, "CC", "9000000915")].Should().BeEquivalentTo(
        [
            new IdentidadVencidaManual(antigua.ValidatedAt, antigua.ExpiredManuallyAt!.Value),
            new IdentidadVencidaManual(reciente.ValidatedAt, reciente.ExpiredManuallyAt!.Value),
        ]);
        protegidas[BiometricRules.IdentidadKey(Tenant, "CC", "9000000999")].Should().BeEquivalentTo(
            [new IdentidadVencidaManual(otraPersona.ValidatedAt, otraPersona.ExpiredManuallyAt!.Value)]);
    }

    [Fact]
    public async Task FindProtegidaPorVencimientoManualAsync_ExigeQueElTramiteHayaSalidoAntesDelVencimiento()
    {
        var vencida = Vencida(Now.AddDays(-10));
        var db = await SembrarAsync([], [], [vencida]);

        await using var ctx = NewContext(db);
        var repo = new ProcedureInstanceRepository(ctx);

        (await repo.FindProtegidaPorVencimientoManualAsync(Tenant, "CC", "9000000915", Now, Now.AddDays(-15), Ct))
            .Should().NotBeNull("salió 15 días atrás, antes del vencimiento de hace 10");
        (await repo.FindProtegidaPorVencimientoManualAsync(Tenant, "CC", "9000000915", Now, Now.AddDays(-2), Ct))
            .Should().BeNull("salió después del vencimiento: nunca tuvo esa firma");
        (await repo.FindProtegidaPorVencimientoManualAsync(Tenant, "CC", "9000000915", Now, Now.AddDays(-35), Ct))
            .Should().BeNull("salió ANTES de que se aprobara la identidad: nunca salió con esa firma");
        (await repo.FindProtegidaPorVencimientoManualAsync(Tenant, "CC", "9000000915", Now, null, Ct))
            .Should().NotBeNull("sin historial (legado) conserva el comportamiento previo");
    }
}
