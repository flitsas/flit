using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Domain.Envios;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Quipux;

/// <summary>
/// HU #12787 (AC2, F1) + re-review #12760 (N1/N4) — <see cref="MaestroRadicadoLookup"/>: «fijo» = la
/// ÚLTIMA radicación VIGENTE (<c>registrado</c>/<c>aprobado</c> con <c>RegisteredAt</c>); «protegido» =
/// toda submission no <c>fallido</c> (incluidas pendientes y rechazadas). Filtrado por tenant Y trámite
/// explícitos. EF InMemory.
/// <para>Uso de ejemplo: <c>await new MaestroRadicadoLookup(db).AttachmentRadicadoAsync(tenantId, instanceId, ct)</c>.</para>
/// </summary>
public sealed class MaestroRadicadoLookupTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static QuipuxSubmission Submission(
        Guid tenantId, Guid instanceId, Guid attachmentId, string status, DateTimeOffset? registeredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = "FLIT_DOC",
            AttachmentId = attachmentId,
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            DivipoCode = "05001",
            Status = status,
            RegisteredAt = registeredAt,
            CreatedAt = Base,
        };

    [Fact]
    public async Task UltimaRadicacionExitosa_DevuelveSuAdjunto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(UltimaRadicacionExitosa_DevuelveSuAdjunto));
        var (tenant, instance, vieja, nueva) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.AddRange(
            Submission(tenant, instance, vieja, QuipuxSubmissionEstado.Rechazado, Base),
            Submission(tenant, instance, nueva, QuipuxSubmissionEstado.Registrado, Base.AddHours(1)));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);

        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().Be(nueva);
        (await lookup.AttachmentsProtegidosAsync(tenant, instance, ct)).Should().BeEquivalentTo([vieja, nueva]);
    }

    [Fact]
    public async Task FallidaOSinRegistrar_NoCuentaComoRadicada_PeroLaPendienteQuedaProtegida()
    {
        // Re-review #12760 (N4/L-N1) — la pendiente (en proceso, sin RegisteredAt) no fija el maestro, pero
        // su adjunto ya viaja hacia Quipux: se protege. La fallida nunca radicó: ni fija ni protege.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(FallidaOSinRegistrar_NoCuentaComoRadicada_PeroLaPendienteQuedaProtegida));
        var (tenant, instance, fallida, pendiente) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.AddRange(
            Submission(tenant, instance, fallida, QuipuxSubmissionEstado.Fallido, Base),
            Submission(tenant, instance, pendiente, QuipuxSubmissionEstado.Pendiente, registeredAt: null));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);

        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().BeNull();
        (await lookup.AttachmentsProtegidosAsync(tenant, instance, ct)).Should().BeEquivalentTo([pendiente]);
    }

    [Fact]
    public async Task Rechazada_NoFijaElMaestro_PeroSuAdjuntoSigueProtegido()
    {
        // Re-review #12760 (N1) — tras un rechazo de Quipux el worker y el POST OT regeneran; la secretaría
        // conserva el documento rechazado, así que su fila y su binario no se retiran.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Rechazada_NoFijaElMaestro_PeroSuAdjuntoSigueProtegido));
        var (tenant, instance, rechazada) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.Add(Submission(tenant, instance, rechazada, QuipuxSubmissionEstado.Rechazado, Base.AddHours(2)));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);

        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().BeNull();
        (await lookup.AttachmentsProtegidosAsync(tenant, instance, ct)).Should().BeEquivalentTo([rechazada]);
    }

    [Theory]
    [InlineData(QuipuxSubmissionEstado.Registrado)]
    [InlineData(QuipuxSubmissionEstado.Aprobado)]
    public async Task RegistradaOAprobada_FijaElMaestro(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(RegistradaOAprobada_FijaElMaestro) + estado);
        var (tenant, instance, adjunto) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.Add(Submission(tenant, instance, adjunto, estado, Base));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);

        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().Be(adjunto);
        (await lookup.AttachmentsProtegidosAsync(tenant, instance, ct)).Should().BeEquivalentTo([adjunto]);
    }

    [Fact]
    public async Task RechazadaPosteriorAUnaRegistrada_GanaLaRegistrada_ConRegistradoSinRegisteredAtNoFija()
    {
        // La última VIGENTE manda aunque haya una rechazada más reciente; una 'registrado' sin RegisteredAt
        // (dato incoherente) no fija, solo protege.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(RechazadaPosteriorAUnaRegistrada_GanaLaRegistrada_ConRegistradoSinRegisteredAtNoFija));
        var (tenant, instance, registrada, rechazada, sinFecha) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.AddRange(
            Submission(tenant, instance, registrada, QuipuxSubmissionEstado.Registrado, Base),
            Submission(tenant, instance, rechazada, QuipuxSubmissionEstado.Rechazado, Base.AddHours(3)));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);
        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().Be(registrada);

        var otra = Guid.NewGuid();
        db.QuipuxSubmissions.Add(Submission(tenant, otra, sinFecha, QuipuxSubmissionEstado.Registrado, registeredAt: null));
        await db.SaveChangesAsync(ct);
        (await lookup.AttachmentRadicadoAsync(tenant, otra, ct)).Should().BeNull();
        (await lookup.AttachmentsProtegidosAsync(tenant, otra, ct)).Should().BeEquivalentTo([sinFecha]);
    }

    [Fact]
    public async Task OtroTenant_NoVeLaRadicacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(OtroTenant_NoVeLaRadicacion));
        var (tenant, instance) = (Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.Add(Submission(tenant, instance, Guid.NewGuid(), QuipuxSubmissionEstado.Registrado, Base));
        await db.SaveChangesAsync(ct);

        (await new MaestroRadicadoLookup(db).AttachmentRadicadoAsync(Guid.NewGuid(), instance, ct)).Should().BeNull(
            "el aislamiento es el tenant_id explícito, no el RLS");
    }
}
