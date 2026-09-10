using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Feature #10701 / HU #10860 — cualquier cambio del expediente invalida el consolidado persistido.
/// Antes solo lo hacían cinco casos de uso (transición de estado, decisión del OT, regenerar el FUR,
/// adjuntar la LT y el <c>force</c> del wizard) y el resto dejaba el PDF congelado: el organismo
/// abría «Ver consolidado» tras una gestión y recibía el de antes.
///
/// <para>Los hijos se dan de alta con <c>db.Add</c> además de por la colección de navegación, igual
/// que los handlers reales: su PK es store-generated (<c>uuidv7()</c>) y con el <c>Id</c> ya asignado
/// EF los tomaría por filas existentes y emitiría un UPDATE en vez de un INSERT.</para>
/// </summary>
public sealed class ConsolidadoVigenciaTrackerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(name).Options);

    private static ProcedureInstance Instancia(Guid id, string referencia, bool vigente) => new()
    {
        Id = id,
        TenantId = TenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = referencia,
        Status = "entregado",
        CreatedAt = DateTimeOffset.UtcNow,
        ConsolidadoMaestroVigente = vigente,
        ConsolidadoWizardVigente = vigente,
    };

    /// <summary>Trámite con AMBOS consolidados marcados vigentes (ya se generaron alguna vez).</summary>
    private static async Task<Guid> SeedAsync(string dbName, bool vigente = true)
    {
        var id = Guid.NewGuid();
        await using var db = NewDb(dbName);
        db.ProcedureInstances.Add(Instancia(id, "TRM-2026-000001", vigente));
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static ProcedureInstanceAttachment NuevoAdjunto(Guid instanceId, string tipo) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = instanceId,
        Tipo = tipo,
        Filename = $"{tipo}.pdf",
        Mimetype = "application/pdf",
        StoragePath = $"p/{tipo}",
        UploadedAt = DateTimeOffset.UtcNow,
    };

    private static void Adjuntar(FlitDbContext db, ProcedureInstance instancia, string tipo)
    {
        var adjunto = NuevoAdjunto(instancia.Id, tipo);
        instancia.Attachments.Add(adjunto);
        db.Add(adjunto);
    }

    private static async Task<(bool Maestro, bool Wizard)> LeerMarcasAsync(string dbName, Guid id)
    {
        await using var db = NewDb(dbName);
        var i = await db.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == id, Ct);
        return (i.ConsolidadoMaestroVigente, i.ConsolidadoWizardVigente);
    }

    [Fact]
    public async Task SubirUnDocumento_InvalidaAmbosConsolidados()
    {
        // EL CASO REPORTADO: se sube un documento después de que el consolidado ya existía. Ningún
        // handler de adjuntos llamaba a InvalidarConsolidados, así que el PDF no lo incluía nunca.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            Adjuntar(db, instancia, "soat");
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Should().Be((false, false));
    }

    [Fact]
    public async Task BorrarUnDocumento_InvalidaAmbosConsolidados()
    {
        // La otra cara: el documento borrado seguía apareciendo en el PDF.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            Adjuntar(db, instancia, "soat");
            await db.SaveChangesAsync(Ct);

            // El alta ya invalidó; se vuelven a marcar vigentes porque lo que prueba este test es la baja.
            instancia.ConsolidadoMaestroVigente = true;
            instancia.ConsolidadoWizardVigente = true;
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances
                .Include(p => p.Attachments)
                .SingleAsync(p => p.Id == id, Ct);
            var previo = instancia.Attachments.Single(a => a.Tipo == "soat");
            instancia.Attachments.Remove(previo);
            db.Remove(previo);
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Should().Be((false, false));
    }

    [Fact]
    public async Task EditarUnDatoDelTramite_InvalidaAmbosConsolidados()
    {
        // El color, el VIN o la placa salen en el FUR y en la portada: cambiarlos cambia el PDF.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            var campo = new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = id,
                FieldKey = "color",
                ValueText = "AZUL",
                Source = "user",
            };
            instancia.FieldValues.Add(campo);
            db.Add(campo);
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Should().Be((false, false));
    }

    [Fact]
    public async Task GenerarElConsolidado_NoSeInvalidaASiMismo()
    {
        // El guard que impide el peor fallo posible: la generación persiste el PDF y sube la marca en
        // el MISMO save. Si el adjunto del consolidado contara como «cambió el expediente», la marca
        // se bajaría acto seguido y el PDF se regeneraría en CADA acceso.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, vigente: false);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            Adjuntar(db, instancia, "consolidado_maestro");
            instancia.ConsolidadoMaestroVigente = true;
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Maestro.Should().BeTrue();
    }

    [Fact]
    public async Task GenerarElConsolidadoJuntoAOtroCambio_ConservaLaMarca()
    {
        // Red de seguridad del guard: aunque el mismo save toque además una tabla hija —la cascada
        // que produce el FUR antes de fusionar—, lo que este save DECLARA vigente no se invalida.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, vigente: false);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            Adjuntar(db, instancia, "fur");
            Adjuntar(db, instancia, "consolidado");
            instancia.ConsolidadoWizardVigente = true;
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Wizard.Should().BeTrue();
    }

    [Fact]
    public async Task SinConsolidadoVigente_NoTocaLaInstancia()
    {
        // El caso mayoritario —trámite en curso al que nadie le generó el consolidado— no debe pagar
        // ni una lectura extra: la instancia ni siquiera se marca como modificada.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, vigente: false);

        await using (var db = NewDb(dbName))
        {
            var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
            Adjuntar(db, instancia, "cedulas");
            await db.SaveChangesAsync(Ct);

            db.ChangeTracker.Entries<ProcedureInstance>()
                .Should().OnlyContain(e => e.State == EntityState.Unchanged);
        }

        (await LeerMarcasAsync(dbName, id)).Should().Be((false, false));
    }

    [Fact]
    public async Task LaTrazabilidad_NoInvalidaNada()
    {
        // Historial de estados y eventos son auditoría: no cambian una sola página del PDF. Si
        // invalidaran, el consolidado se regeneraría cada vez que se registra una traza — incluida
        // la que deja la propia generación (`consolidado_maestro_generado`).
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        await using (var db = NewDb(dbName))
        {
            db.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = id,
                Tipo = "consolidado_maestro_generado",
                Payload = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        (await LeerMarcasAsync(dbName, id)).Should().Be((true, true));
    }

    [Fact]
    public async Task DevuelveElConteoDelSaveDelLlamador()
    {
        // El segundo save de las marcas es contabilidad interna del consolidado: no puede inflar el
        // número de filas que ve el llamador (SaveChangesWithConcurrencyGuardAsync depende de él).
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        await using var db = NewDb(dbName);
        var instancia = await db.ProcedureInstances.SingleAsync(p => p.Id == id, Ct);
        Adjuntar(db, instancia, "rtm");

        var afectadas = await db.SaveChangesAsync(Ct);

        afectadas.Should().Be(1);
        instancia.ConsolidadoMaestroVigente.Should().BeFalse();
    }
}
