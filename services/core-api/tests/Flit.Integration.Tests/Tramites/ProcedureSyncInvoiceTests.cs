using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13077 (Feature #13066, Épica #12737) contra Postgres real con TODAS las migraciones: la búsqueda de
/// la factura para la URL firmada respeta el alcance del feed (radicado, no migrado, no eliminado), solo
/// entrega adjuntos de tipo factura (decisión del PO) y nunca el de otro trámite (AC2).
/// </summary>
public sealed class ProcedureSyncInvoiceTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013077");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013077");

    [PostgresFact]
    public async Task AC1_LaFacturaDeUnTramiteRadicadoSeEncuentraConLoNecesarioParaFirmar()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync(1);
        var factura = await AdjuntoAsync(tramite, "factura", "factura.pdf");

        var encontrada = await BuscarAsync(tramite, factura);

        encontrada.Should().Be(new ProcedureSyncInvoiceFile("it/13077/factura.pdf", "factura.pdf", "application/pdf"));
    }

    [PostgresFact]
    public async Task AC1_TambienUnaFacturaAnteriorDelMismoTramite()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync(1);
        var anterior = await AdjuntoAsync(tramite, "factura", "anterior.pdf");
        await AdjuntoAsync(tramite, "factura", "reemplazo.pdf");

        (await BuscarAsync(tramite, anterior)).Should().NotBeNull("el feed puede haber entregado la anterior antes del reemplazo");
    }

    [PostgresFact]
    public async Task AC2_AdjuntoDeOtroTramiteInexistenteONoFacturaNoSeEncuentra()
    {
        await SembrarAsync();
        var tramite = await RadicadoAsync(1);
        var otro = await RadicadoAsync(2);
        var facturaDelOtro = await AdjuntoAsync(otro, "factura", "otra.pdf");
        var cedula = await AdjuntoAsync(tramite, "cedula_comprador", "cedula.pdf");

        (await BuscarAsync(tramite, facturaDelOtro)).Should().BeNull("pertenece a otro trámite");
        (await BuscarAsync(tramite, Guid.CreateVersion7())).Should().BeNull("no existe");
        (await BuscarAsync(Guid.CreateVersion7(), facturaDelOtro)).Should().BeNull("el trámite no existe");
        (await BuscarAsync(tramite, cedula)).Should().BeNull("solo se entregan facturas");
    }

    [PostgresFact]
    public async Task AC2_TramiteFueraDeAlcanceOEliminadoNoEntregaSuFactura()
    {
        await SembrarAsync();
        var borrador = await TramiteAsync(1);
        var migrado = await RadicadoAsync(2);
        var eliminado = await RadicadoAsync(3);
        var deBorrador = await AdjuntoAsync(borrador, "factura", "borrador.pdf");
        var deMigrado = await AdjuntoAsync(migrado, "factura", "migrado.pdf");
        var deEliminado = await AdjuntoAsync(eliminado, "factura", "eliminado.pdf");
        await EjecutarAsync("UPDATE tramites.procedure_instances SET is_migrated = true WHERE id = @id", migrado);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id", eliminado);

        (await BuscarAsync(borrador, deBorrador)).Should().BeNull("nunca radicado");
        (await BuscarAsync(migrado, deMigrado)).Should().BeNull("migrado desde FLIT 1");
        (await BuscarAsync(eliminado, deEliminado)).Should().BeNull("el tombstone llega con factura null");
    }

    // ── Siembra y utilidades ────────────────────────────────────────────────

    private async Task<ProcedureSyncInvoiceFile?> BuscarAsync(Guid tramite, Guid adjunto) =>
        await new ProcedureSyncReadRepository(NewContext())
            .FindInvoiceAsync(tramite, adjunto, TestContext.Current.CancellationToken);

    private async Task SembrarAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13077", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = Gestor,
            Email = "it-13077@flit.test",
            DisplayName = "Gestor 13077",
            Status = "active",
            HomeTenantId = Compania,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> TramiteAsync(int n)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        var id = Guid.CreateVersion7();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Compania,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13077-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13077{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> RadicadoAsync(int n)
    {
        var id = await TramiteAsync(n);
        await EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + "VALUES (@tenant, @id, 'preasignacion')",
            id);
        return id;
    }

    private async Task<Guid> AdjuntoAsync(Guid id, string tipo, string nombre)
    {
        var adjunto = Guid.CreateVersion7();
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tramites.procedure_instance_attachments (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at) "
            + "VALUES (@adjunto, @tenant, @id, @tipo, @nombre, 'application/pdf', 10, repeat('a', 64), 'it/13077/' || @nombre, now())",
            conn);
        cmd.Parameters.AddWithValue("adjunto", adjunto);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tipo", tipo);
        cmd.Parameters.AddWithValue("nombre", nombre);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return adjunto;
    }

    private async Task EjecutarAsync(string sql, Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", Compania);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
