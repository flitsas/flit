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
/// HU #13079 (Feature #13066, Épica #12737) contra Postgres real con TODAS las migraciones: la lectura
/// del feed arma el ítem del contrato v3.1 §4 desde las tablas de trámites en la misma consulta que la
/// página (AC1), con copropiedad (AC2), la precedencia comprador &gt; propietario (AC3), la marca de
/// borrado (AC4), la última aprobación y la factura más reciente.
/// </summary>
public sealed class ProcedureSyncItemsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013079");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013079");
    private static readonly Guid Organismo = new("5a5a5a5a-0003-4000-8000-000000013079");
    private static readonly TimeSpan SinVentana = TimeSpan.Zero;

    [PostgresFact]
    public async Task AC1_ElItemSaleCompletoDesdeLasTablasDelTramite()
    {
        await SembrarAsync();
        var id = await TramiteAsync(1);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET transit_office_id = @organismo, plate = 'ABC123', submitted_at = now() WHERE id = @id", id);
        await CamposAsync(id, ("vehicle_brand", "MARCA EJEMPLO"), ("vehicle_line", "LINEA EJEMPLO"), ("vehicle_year", "2026"),
            ("vehicle_class", "CAMIONETA"), ("vehicle_body_type", "SUV"), ("vehicle_engine_displacement", "2.0 L"),
            ("vehicle_passengers", "5"), ("vehicle_engine_number", "MTR000000"), ("vehicle_series", "SER000000"),
            ("vehicle_service", "servicio particular"));
        await ActorAsync(id, "comprador", 1, null, "900000000", 0);
        await FacturaAsync(id, "factura-vieja.pdf", "now() - interval '2 days'");
        var facturaNueva = await FacturaAsync(id, "factura.pdf", "now() - interval '1 day'");
        await HistorialAsync(id, "preasignacion");
        await HistorialAsync(id, "aprobado");

        var entrada = (await LeerAsync()).Single();
        var item = entrada.Item;

        item.Id.Should().Be(id);
        item.Radicado.Should().MatchRegex(@"^FT\d-\d{7}$");
        item.Consecutivo.Should().BePositive();
        item.SyncVersion.Should().Be(entrada.Position.Version);
        item.Estado.Should().Be("borrador");
        item.Tramite.Codigo.Should().NotBeNullOrWhiteSpace();
        item.FechaRadicacion.Should().NotBeNull();
        item.FechaAprobacion.Should().NotBeNull();
        item.FechaCreacion.Offset.Should().Be(TimeSpan.FromHours(-5));

        item.Vehiculo.Should().NotBeNull();
        item.Vehiculo!.Placa.Should().Be("ABC123");
        item.Vehiculo.Marca.Should().Be("MARCA EJEMPLO");
        item.Vehiculo.ModeloAno.Should().Be(2026);
        item.Vehiculo.Cilindraje.Should().BeNull();
        item.Vehiculo.CilindrajeTexto.Should().Be("2.0 L");
        item.Vehiculo.Capacidad.Should().Be(5);
        item.Vehiculo.TipoServicio!.Codigo.Should().Be("PARTICULAR");
        item.Vehiculo.TipoServicio.Nombre.Should().NotBeNullOrWhiteSpace("sale del catálogo de tipos de servicio");

        item.Organismo.Should().Be(new ProcedureSyncOrganismo("99999000", "ORGANISMO DE PRUEBA 13079", "99999", "CIUDAD EJEMPLO", "DEPARTAMENTO EJEMPLO"));
        item.Compradores.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Ordinal = 1,
            PorcentajeParticipacion = (decimal?)null,
            RolActor = "comprador",
            NumeroDocumento = "900000000",
            Direccion = "CALLE 1 # 2-3",
            Ciudad = "PALMIRA",
        });
        item.Factura!.AdjuntoId.Should().Be(facturaNueva, "la factura es el adjunto más reciente");
        item.CompaniaGestora.TenantId.Should().Be(Compania);
        item.CompaniaGestora.Nombre.Should().Be("Cliente de integración IT-13079");
    }

    [PostgresFact]
    public async Task AC2_CopropiedadConSusPorcentajesPorOrdinal()
    {
        await SembrarAsync();
        var id = await TramiteAsync(1);
        await ActorAsync(id, "comprador", 2, 40.00m, "1000000000", 1);
        await ActorAsync(id, "comprador", 1, 60.00m, "900000000", 0);
        await HistorialAsync(id, "entregado");

        var compradores = (await LeerAsync()).Single().Item.Compradores;

        compradores.Select(c => (c.Ordinal, c.PorcentajeParticipacion)).Should().Equal((1, 60.00m), (2, 40.00m));
    }

    [PostgresFact]
    public async Task AC3_SinCompradorVanLosPropietariosYSinNingunoLaListaVacia()
    {
        await SembrarAsync();
        var conPropietario = await TramiteAsync(1);
        await ActorAsync(conPropietario, "propietario", 1, null, "1000000000", 0);
        await HistorialAsync(conPropietario, "entregado");
        var conAmbos = await TramiteAsync(2);
        await ActorAsync(conAmbos, "propietario", 1, null, "1000000000", 0);
        await ActorAsync(conAmbos, "comprador", 1, null, "900000000", 1);
        await HistorialAsync(conAmbos, "entregado");
        var sinActores = await TramiteAsync(3);
        await HistorialAsync(sinActores, "entregado");

        var items = (await LeerAsync()).ToDictionary(e => e.Item.Id, e => e.Item);

        items[conPropietario].Compradores.Should().ContainSingle().Which.RolActor.Should().Be("propietario");
        items[conAmbos].Compradores.Should().ContainSingle().Which.RolActor.Should().Be("comprador");
        items[sinActores].Compradores.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC4_ElBorradoLlegaComoMarcaConLosBloquesVacios()
    {
        await SembrarAsync();
        var id = await TramiteAsync(1);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET transit_office_id = @organismo WHERE id = @id", id);
        await ActorAsync(id, "comprador", 1, null, "900000000", 0);
        await FacturaAsync(id, "factura.pdf", "now()");
        await HistorialAsync(id, "entregado");
        await EjecutarAsync("UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id", id);

        var item = (await LeerAsync()).Single().Item;

        item.Eliminado.Should().BeTrue();
        item.Vehiculo.Should().BeNull();
        item.Organismo.Should().BeNull();
        item.Factura.Should().BeNull();
        item.Compradores.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task LosItemsSonLaMismaPaginaQueLaLecturaDeCambios()
    {
        await SembrarAsync();
        for (var n = 1; n <= 3; n++)
        {
            await HistorialAsync(await TramiteAsync(n), "entregado");
        }

        await ProcedureSyncTestWait.EsperarFeedEstableAsync(Fixture);
        var repo = new ProcedureSyncReadRepository(NewContext());
        var cambios = await repo.ReadChangesAsync(new(null, null, 2, SinVentana), TestContext.Current.CancellationToken);
        var items = await LeerAsync(2);

        items.Select(e => (e.Item.Id, e.Position)).Should().Equal(cambios.Select(c => (c.ProcedureInstanceId, c.Position)));
    }

    // ── Siembra y utilidades ────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProcedureSyncEntry>> LeerAsync(int pagina = 100)
    {
        await ProcedureSyncTestWait.EsperarFeedEstableAsync(Fixture);
        return await new ProcedureSyncReadRepository(NewContext())
            .ReadItemsAsync(new(null, null, pagina, SinVentana), TestContext.Current.CancellationToken);
    }

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13079", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13079@flit.test",
                DisplayName = "Gestor 13079",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await EjecutarAsync(
            "INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name) "
            + "VALUES (@organismo, '99999000', 'ORGANISMO DE PRUEBA 13079', '99', '99999', 'CIUDAD EJEMPLO', 'DEPARTAMENTO EJEMPLO')",
            Guid.Empty);
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
            ReferenceNumber = $"IT13079-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13079{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private Task HistorialAsync(Guid id, string estado) =>
        EjecutarAsync(
            "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) "
            + $"VALUES (@tenant, @id, '{estado}')",
            id);

    private async Task CamposAsync(Guid id, params (string Clave, string Valor)[] campos)
    {
        foreach (var (clave, valor) in campos)
        {
            await using var conn = await Fixture.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text) VALUES (@tenant, @id, @k, @v)",
                conn);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("k", clave);
            cmd.Parameters.AddWithValue("v", valor);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Actor con una entidad distinta por posición (<paramref name="entidad"/>-ésima del catálogo).</summary>
    private async Task ActorAsync(Guid id, string rol, int ordinal, decimal? porcentaje, string documento, int entidad)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tramites.procedure_instance_actors (tenant_id, procedure_instance_id, procedure_entity_id, actor_type, document_type, "
            + "document_number, full_name, ordinal, ownership_percentage, person_type, metadata) "
            + "VALUES (@tenant, @id, (SELECT id FROM tramites.procedure_entities ORDER BY id OFFSET @entidad LIMIT 1), @rol, 'CC', @doc, "
            + "'PERSONA EJEMPLO', @ordinal, @pct, 'natural', '{\"direccion\":\"CALLE 1 # 2-3\",\"ciudad\":\"PALMIRA\"}'::jsonb)",
            conn);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("entidad", entidad);
        cmd.Parameters.AddWithValue("rol", rol);
        cmd.Parameters.AddWithValue("doc", documento);
        cmd.Parameters.AddWithValue("ordinal", ordinal);
        cmd.Parameters.AddWithValue("pct", (object?)porcentaje ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> FacturaAsync(Guid id, string nombre, string cargadaEn)
    {
        var adjunto = Guid.CreateVersion7();
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tramites.procedure_instance_attachments (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at) "
            + $"VALUES (@adjunto, @tenant, @id, 'factura', @nombre, 'application/pdf', 10, repeat('a', 64), 'it/13079/' || @nombre, {cargadaEn})",
            conn);
        cmd.Parameters.AddWithValue("adjunto", adjunto);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", id);
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
        cmd.Parameters.AddWithValue("organismo", Organismo);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
