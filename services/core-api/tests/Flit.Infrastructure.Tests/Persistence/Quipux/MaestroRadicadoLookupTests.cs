using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Domain.Envios;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Quipux;

/// <summary>
/// HU #12787 (AC2, F1) — <see cref="MaestroRadicadoLookup"/>: criterio de «maestro radicado» = el de
/// HU #12791 (<c>RegisteredAt</c> con valor y estado distinto de <c>fallido</c>), la ÚLTIMA radicación
/// por <c>RegisteredAt</c>, filtrado por tenant Y trámite explícitos. EF InMemory.
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
        (await lookup.AttachmentsRadicadosAsync(tenant, instance, ct)).Should().BeEquivalentTo([vieja, nueva]);
    }

    [Fact]
    public async Task FallidaOSinRegistrar_NoCuentaComoRadicada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(FallidaOSinRegistrar_NoCuentaComoRadicada));
        var (tenant, instance) = (Guid.NewGuid(), Guid.NewGuid());
        db.QuipuxSubmissions.AddRange(
            Submission(tenant, instance, Guid.NewGuid(), QuipuxSubmissionEstado.Fallido, Base),
            Submission(tenant, instance, Guid.NewGuid(), QuipuxSubmissionEstado.Pendiente, registeredAt: null));
        await db.SaveChangesAsync(ct);

        var lookup = new MaestroRadicadoLookup(db);

        (await lookup.AttachmentRadicadoAsync(tenant, instance, ct)).Should().BeNull();
        (await lookup.AttachmentsRadicadosAsync(tenant, instance, ct)).Should().BeEmpty();
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
