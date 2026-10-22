using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #13055 — cualquier cambio del expediente, desde cualquier pantalla (gestor, OT o
/// administración), deja el FUR y los consolidados al día sin «Limpiar consolidado»:
/// <list type="bullet">
///   <item>un cambio de DATOS que imprime el FUR sella <c>expediente_actualizado_en</c> si el trámite ya
///   tiene FUR (un FUR anterior se regenera antes de consolidar);</item>
///   <item>cualquier cambio (datos o documentos) encola la regeneración anticipada de los consolidados
///   que el trámite ya tiene;</item>
///   <item>no se toca nada en estados finales ni se crean consolidados que nadie pidió, y la propia
///   regeneración (FUR/consolidado) no vuelve a disparar el ciclo.</item>
/// </list>
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var (db, cola) = NewDb(nombre);          // contexto con la cola en el contenedor de la app
/// editar un campo del trámite; await db.SaveChangesAsync();
/// // instancia.ExpedienteActualizadoEn != null, cola.Solicitudes == [(tenant, id, Wizard), (tenant, id, Maestro)]
/// </code>
/// </summary>
public sealed class ExpedienteRegeneracionAutomaticaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingQueue : IConsolidadoRegeneracionQueue
    {
        public List<(Guid Tenant, Guid Id, TipoConsolidado Doc)> Solicitudes { get; } = [];

        public bool Encolar(Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento)
        {
            Solicitudes.Add((tenantId, procedureInstanceId, documento));
            return true;
        }
    }

    private static (FlitDbContext Db, RecordingQueue Cola) NewDb(string name)
    {
        var cola = new RecordingQueue();
        var sp = new ServiceCollection().AddSingleton<IConsolidadoRegeneracionQueue>(cola).BuildServiceProvider();
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(name)
            .UseApplicationServiceProvider(sp)
            .Options;
        return (new FlitDbContext(options), cola);
    }

    private static async Task<Guid> SeedAsync(
        string dbName, string estado = TramiteEstado.Asignado, bool conFur = true, params string[] consolidados)
    {
        var id = Guid.NewGuid();
        var (db, _) = NewDb(dbName);
        await using (db)
        {
            db.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = id,
                TenantId = TenantId,
                ProcedureTypeId = Guid.NewGuid(),
                ReferenceNumber = "TRM-2026-013055",
                Status = estado,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            if (conFur)
                db.Add(Adjunto(id, FurVigenciaExpediente.TipoFur));
            foreach (var tipo in consolidados)
                db.Add(Adjunto(id, tipo));
            await db.SaveChangesAsync(Ct);
        }

        return id;
    }

    private static ProcedureInstanceAttachment Adjunto(Guid instanceId, string tipo) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = instanceId,
        Tipo = tipo,
        Filename = $"{tipo}.pdf",
        Mimetype = "application/pdf",
        StoragePath = $"p/{tipo}",
        Source = "system",
        UploadedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceFieldValue Campo(Guid instanceId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = instanceId,
        FieldKey = "color",
        ValueText = "AZUL",
        Source = "user",
    };

    private static async Task<ProcedureInstance> LeerAsync(string dbName, Guid id)
    {
        var (db, _) = NewDb(dbName);
        await using (db)
            return await db.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == id, Ct);
    }

    [Fact]
    public async Task EditarUnDato_ConFur_SellaLaMarca_YEncolaLosConsolidadosExistentes()
    {
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, consolidados: ["consolidado", "consolidado_maestro"]);
        var antes = DateTimeOffset.UtcNow;

        var (db, cola) = NewDb(dbName);
        await using (db)
        {
            db.Add(Campo(id));
            await db.SaveChangesAsync(Ct);

            cola.Solicitudes.Should().BeEquivalentTo(new[]
            {
                (TenantId, id, TipoConsolidado.Wizard),
                (TenantId, id, TipoConsolidado.Maestro),
            });
        }

        var instancia = await LeerAsync(dbName, id);
        instancia.ExpedienteActualizadoEn.Should().NotBeNull().And.BeOnOrAfter(antes);
    }

    [Fact]
    public async Task EditarUnDato_SinFur_NoSellaLaMarca()
    {
        // Borrador que aún no llegó a Preparar: no hay FUR que desactualizar ni UPDATE extra que pagar.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, estado: TramiteEstado.Borrador, conFur: false);

        var (db, _) = NewDb(dbName);
        await using (db)
        {
            db.Add(Campo(id));
            await db.SaveChangesAsync(Ct);
        }

        (await LeerAsync(dbName, id)).ExpedienteActualizadoEn.Should().BeNull();
    }

    [Fact]
    public async Task SubirUnDocumento_EncolaElConsolidado_PeroNoMarcaElFur()
    {
        // El FUR no imprime los adjuntos: basta con rearmar el consolidado.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, consolidados: ["consolidado_maestro"]);

        var (db, cola) = NewDb(dbName);
        await using (db)
        {
            db.Add(Adjunto(id, "soat"));
            await db.SaveChangesAsync(Ct);

            cola.Solicitudes.Should().ContainSingle().Which.Should().Be((TenantId, id, TipoConsolidado.Maestro));
        }

        (await LeerAsync(dbName, id)).ExpedienteActualizadoEn.Should().BeNull();
    }

    [Fact]
    public async Task SinConsolidadoPrevio_NoEncolaNada()
    {
        // No se generan consolidados que nadie pidió (p. ej. cada edición de un borrador).
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName);

        var (db, cola) = NewDb(dbName);
        await using (db)
        {
            db.Add(Campo(id));
            await db.SaveChangesAsync(Ct);
            cola.Solicitudes.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task EstadoFinal_NoSellaNiEncola(string estado)
    {
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, estado: estado, consolidados: ["consolidado", "consolidado_maestro"]);

        var (db, cola) = NewDb(dbName);
        await using (db)
        {
            db.Add(Campo(id));
            await db.SaveChangesAsync(Ct);
            cola.Solicitudes.Should().BeEmpty();
        }

        (await LeerAsync(dbName, id)).ExpedienteActualizadoEn.Should().BeNull();
    }

    [Fact]
    public async Task RegenerarElFur_NoVuelveASellarLaMarca()
    {
        // Sin esta garantía habría un ciclo: regenerar el FUR (adjuntos) → marca → FUR desactualizado → …
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, consolidados: ["consolidado"]);

        var (db, _) = NewDb(dbName);
        await using (db)
        {
            var viejo = await db.Set<ProcedureInstanceAttachment>().SingleAsync(a => a.ProcedureInstanceId == id && a.Tipo == "fur", Ct);
            db.Remove(viejo);
            db.Add(Adjunto(id, FurVigenciaExpediente.TipoFur));
            await db.SaveChangesAsync(Ct);
        }

        (await LeerAsync(dbName, id)).ExpedienteActualizadoEn.Should().BeNull();
    }

    [Fact]
    public async Task ContextoSinContenedor_GuardaIgual_SinEncolar()
    {
        // Contexto armado a mano (tests, herramientas): sin cola el guardado no falla.
        var dbName = Guid.NewGuid().ToString();
        var id = await SeedAsync(dbName, consolidados: ["consolidado"]);

        await using (var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options))
        {
            db.Add(Campo(id));
            await db.SaveChangesAsync(Ct);
        }

        (await LeerAsync(dbName, id)).ExpedienteActualizadoEn.Should().NotBeNull();
    }
}
