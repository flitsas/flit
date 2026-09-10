using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// Bug #11613 — <c>RegeneracionDocumentalTrazaWriter</c> es la pieza de más riesgo del arreglo (SQL
/// crudo, fuera del change tracker) y no tenía ninguna prueba.
///
/// <para><b>Alcance real de estas pruebas.</b> Cubren las guardas de entrada y la rama NO RELACIONAL
/// (proveedor InMemory), que es la que ejercitan los tests. La rama RELACIONAL —el
/// <c>INSERT ... SELECT ... WHERE EXISTS</c> contra <c>tramites.procedure_instance_events</c>— NO se
/// puede ejecutar aquí: es SQL específico de PostgreSQL (esquema <c>tramites</c>, cast <c>::jsonb</c>)
/// y requeriría una base real (Testcontainers / base de integración), infraestructura que este proyecto
/// de pruebas no tiene. Queda explícitamente SIN COBERTURA automatizada; se verifica por revisión y en
/// el ambiente DEV.</para>
///
/// <para>Uso de ejemplo:
/// <code>
/// var writer = new RegeneracionDocumentalTrazaWriter(db);
/// var ok = await writer.EscribirFalloAsync(tenantId, instanceId, origen, "organismo_requerido", null, ct);
/// </code>
/// </para>
/// </summary>
public sealed class RegeneracionDocumentalTrazaWriterTests
{
    private static FlitDbContext Context(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public async Task EscribirFalloAsync_PersisteElEventoConTenantInstanciaYPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await using var db = Context(nameof(EscribirFalloAsync_PersisteElEventoConTenantInstanciaYPayload));

        var ok = await new RegeneracionDocumentalTrazaWriter(db).EscribirFalloAsync(
            tenantId, instanceId, RegeneracionDocumentalOrigen.AsignacionPlaca, "organismo_requerido", null, ct);

        ok.Should().BeTrue();
        var evento = await db.ProcedureInstanceEvents.SingleAsync(ct);
        evento.TenantId.Should().Be(tenantId);
        evento.ProcedureInstanceId.Should().Be(instanceId);
        evento.Tipo.Should().Be(RegenerarDocumentosTrazadoHandler.EventoFallo);
        evento.Payload.Should().Contain("organismo_requerido")
            .And.Contain(RegeneracionDocumentalOrigen.AsignacionPlaca)
            .And.Contain(tenantId.ToString());
    }

    [Fact]
    public async Task EscribirFalloAsync_ConDetalle_LoIncluyeEnElPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context(nameof(EscribirFalloAsync_ConDetalle_LoIncluyeEnElPayload));

        await new RegeneracionDocumentalTrazaWriter(db).EscribirFalloAsync(
            Guid.NewGuid(), Guid.NewGuid(), RegeneracionDocumentalOrigen.AprobacionOt,
            RegenerarDocumentosTrazadoHandler.ErrorExcepcion, "InvalidOperationException", ct);

        var evento = await db.ProcedureInstanceEvents.SingleAsync(ct);
        evento.Payload.Should().Contain("InvalidOperationException");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EscribirFalloAsync_SinTenantOSinInstancia_NoEscribeNada(bool tenantVacio, bool instanciaVacia)
    {
        // Guarda de entrada: una traza sin dueño no se escribe (y en la rama relacional tampoco pasaría
        // el WHERE EXISTS que ata la fila al trámite de ESE tenant).
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context($"guardas-{tenantVacio}-{instanciaVacia}");

        var ok = await new RegeneracionDocumentalTrazaWriter(db).EscribirFalloAsync(
            tenantVacio ? Guid.Empty : Guid.NewGuid(),
            instanciaVacia ? Guid.Empty : Guid.NewGuid(),
            RegeneracionDocumentalOrigen.AprobacionOt,
            "organismo_requerido",
            null,
            ct);

        ok.Should().BeFalse();
        (await db.ProcedureInstanceEvents.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task EscribirFalloAsync_DosVeces_DejaDosEventosIndependientes()
    {
        // La bitácora es append-only: cada fallo deja su propia fila (nada de upsert por trámite).
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await using var db = Context(nameof(EscribirFalloAsync_DosVeces_DejaDosEventosIndependientes));
        var writer = new RegeneracionDocumentalTrazaWriter(db);

        await writer.EscribirFalloAsync(tenantId, instanceId, RegeneracionDocumentalOrigen.AprobacionOt, "e1", null, ct);
        await writer.EscribirFalloAsync(tenantId, instanceId, RegeneracionDocumentalOrigen.AprobacionOt, "e2", null, ct);

        var eventos = await db.ProcedureInstanceEvents.ToListAsync(ct);
        eventos.Should().HaveCount(2);
        eventos.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }
}
