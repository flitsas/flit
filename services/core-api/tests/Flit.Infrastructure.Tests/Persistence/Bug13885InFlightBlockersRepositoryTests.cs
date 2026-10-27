using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #13885 — consultas de validaciones en vuelo por documento:
/// <list type="bullet">
///   <item><c>ListInFlightByDocumentAsync</c> (precedencia de envío) ya no devuelve las de trámites anulados o revocados:
///   encauzaba el reenvío hacia un trámite muerto (<c>enlace_vencido_reenvio</c> en FT2-0000181).</item>
///   <item><c>ListInFlightBlockersByDocumentAsync</c> replica el índice único <c>uq_biometric_validations_inflight_doc_norm</c>:
///   todas las filas en vuelo del documento (incluidas las de trámites inactivos), sin las del mandatario, con el trámite
///   cargado y rastreadas.</item>
/// </list>
/// </summary>
public sealed class Bug13885InFlightBlockersRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ProcedureInstance Tramite(string estado, bool borrado = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, ProcedureTypeId = Guid.NewGuid(), ReferenceNumber = $"FT2-{Guid.NewGuid():N}"[..14],
        Status = estado, CreatedAt = Now.AddDays(-30), DeletedAt = borrado ? Now.AddDays(-1) : null,
    };

    private static ProcedureInstanceBiometricValidation Fila(
        string estado, ProcedureInstance? tramite, string tipo = "CC", string numero = "10741866", Guid? mandateSignerId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, ProcedureInstanceId = tramite?.Id, MandateSignerId = mandateSignerId,
        PartyRole = mandateSignerId is null ? "comprador" : BiometricRules.ParteMandatario, DocumentType = tipo, DocumentNumber = numero,
        Status = estado, Provider = BiometricProviders.Kyverum, TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = Now.AddDays(-20), CreatedAt = Now.AddDays(-21),
    };

    private static async Task<string> SembrarAsync(IEnumerable<ProcedureInstance> tramites, IEnumerable<ProcedureInstanceBiometricValidation> filas)
    {
        var db = $"flit-13885-{Guid.NewGuid()}";
        await using var seed = NewContext(db);
        seed.Set<ProcedureInstance>().AddRange(tramites);
        seed.Set<ProcedureInstanceBiometricValidation>().AddRange(filas);
        await seed.SaveChangesAsync(Ct);
        return db;
    }

    [Theory]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task ListInFlightByDocumentAsync_ExcluyeLasDeTramitesAnuladosORevocados(string estado)
    {
        var muerto = Tramite(estado);
        var vivo = Tramite(TramiteEstado.Borrador);
        var zombie = Fila(BiometricEstados.EnProceso, muerto);
        var enCurso = Fila(BiometricEstados.EnProceso, vivo);
        var prevalidacion = Fila(BiometricEstados.Enviado, tramite: null);
        var db = await SembrarAsync([muerto, vivo], [zombie, enCurso, prevalidacion]);

        await using var ctx = NewContext(db);
        var inFlight = await new ProcedureInstanceRepository(ctx).ListInFlightByDocumentAsync(Tenant, "CC", "10741866", Ct);

        inFlight.Select(v => v.Id).Should().BeEquivalentTo([enCurso.Id, prevalidacion.Id]);
    }

    [Fact]
    public async Task ListInFlightBlockersByDocumentAsync_DevuelveTodasLasEnVueloDelDocumento_ComoElIndice()
    {
        var anulado = Tramite(TramiteEstado.Anulado);
        var borrado = Tramite(TramiteEstado.Borrador, borrado: true);
        var vivo = Tramite(TramiteEstado.Borrador);
        var zombieAnulado = Fila(BiometricEstados.EnProceso, anulado);
        var zombieBorrado = Fila(BiometricEstados.PendienteEnvio, borrado, tipo: " cc ");
        var viva = Fila(BiometricEstados.Enviado, vivo, numero: " 10741866 ");
        var db = await SembrarAsync(
            [anulado, borrado, vivo],
            [
                zombieAnulado, zombieBorrado, viva,
                Fila(BiometricEstados.Aprobado, vivo),
                Fila(BiometricEstados.Expirado, anulado),
                Fila(BiometricEstados.EnProceso, vivo, numero: "99999999"),
                Fila(BiometricEstados.EnProceso, tramite: null, mandateSignerId: Guid.NewGuid()),
            ]);

        await using var ctx = NewContext(db);
        var bloqueos = await new ProcedureInstanceRepository(ctx).ListInFlightBlockersByDocumentAsync(Tenant, "cc", "10741866 ", Ct);

        bloqueos.Select(v => v.Id).Should().BeEquivalentTo([zombieAnulado.Id, zombieBorrado.Id, viva.Id],
            "son las filas que el índice único cuenta para este documento");
        bloqueos.Should().OnlyContain(v => v.ProcedureInstance != null, "el caso de uso decide con el estado del trámite");
        bloqueos.Count(BiometricRules.EsEnVueloDeTramiteInactivo).Should().Be(2);
        ctx.ChangeTracker.Entries<ProcedureInstanceBiometricValidation>().Should().HaveCount(3, "se devuelven rastreadas para cerrarlas");
    }
}
