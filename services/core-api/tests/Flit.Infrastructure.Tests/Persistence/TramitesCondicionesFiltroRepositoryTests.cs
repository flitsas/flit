using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12106 — traducción a SQL de las condiciones de la gramática de Consultas.
///
/// <para>Lo que estas pruebas fijan es que el <c>WHERE</c> se aplica en la CONSULTA y no sobre las
/// filas ya traídas: por eso cada caso comprueba también el <c>total</c>, que es el conteo del
/// universo. Un filtro que se resolviera en memoria daría los mismos ítems en la página y un total
/// equivocado, y nadie lo notaría hasta tener un tenant grande.</para>
/// </summary>
public sealed class TramitesCondicionesFiltroRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ProcedureInstance Instancia(
        string reference, string? plate = null, string? vin = null,
        string? comprador = null, string? vendedor = null, string? estado = null,
        string? origin = null, bool migrado = false, string modalidad = "traspaso") => new()
    {
        ProcedureType = ProcedureTypeFixture.For(modalidad),
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = reference,
        Plate = plate,
        Vin = vin,
        CompradorNombre = comprador,
        VendedorNombre = vendedor,
        Status = estado ?? TramiteEstado.Borrador,
        Origin = origin,
        IsMigrated = migrado,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Base,
    };

    private static QueryCondition Cond(string campo, string op, params string[] valores) =>
        new(campo, op, valores);

    private static async Task<(List<string> Referencias, int Total)> Filtrar(
        FlitDbContext db, params QueryCondition[] condiciones)
    {
        var repo = new ProcedureInstanceRepository(db);
        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 100,
            new ProcedureInstanceListFilter { Condiciones = condiciones },
            ProcedureInstanceSortBy.Radicado, SortDirection.Ascending,
            TestContext.Current.CancellationToken);

        return (items.Select(i => i.ReferenceNumber).ToList(), total);
    }

    // ── AC3 — los cinco operadores, con la normalización de identificadores ──────────────────

    [Fact]
    public async Task EsAlguno_EnPlaca_IgnoraGuionesYEspacios()
    {
        await using var db = NewContext(nameof(EsAlguno_EnPlaca_IgnoraGuionesYEspacios));
        db.ProcedureInstances.AddRange(
            Instancia("R1", plate: "ABC123"),
            Instancia("R2", plate: "XYZ999"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Pegado desde Excel con guion: tiene que casar igual, o el aviso de cobertura de Consultas y
        // este filtro dirían cosas distintas del mismo vehículo.
        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.EsAlguno, "abc-123"));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }

    [Fact]
    public async Task Contiene_EnPlaca_HaceBusquedaParcial()
    {
        await using var db = NewContext(nameof(Contiene_EnPlaca_HaceBusquedaParcial));
        db.ProcedureInstances.AddRange(
            Instancia("R1", plate: "ABC123"),
            Instancia("R2", plate: "ABC777"),
            Instancia("R3", plate: "XYZ999"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // El criterio pedía "exacta o parcial": no hay que elegir, lo decide el operador.
        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.Contiene, "bc"));

        refs.Should().Equal("R1", "R2");
        total.Should().Be(2);
    }

    [Fact]
    public async Task NoEsNinguno_EnPlaca_IncluyeLasQueNoTienenPlaca()
    {
        await using var db = NewContext(nameof(NoEsNinguno_EnPlaca_IncluyeLasQueNoTienenPlaca));
        db.ProcedureInstances.AddRange(
            Instancia("R1", plate: "ABC123"),
            Instancia("R2", plate: "XYZ999"),
            Instancia("R3"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Un trámite sin placa NO es "uno de los excluidos": negar el filtro no puede esconderlo.
        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.NoEsNinguno, "ABC123"));

        refs.Should().Equal("R2", "R3");
    }

    [Fact]
    public async Task EstaVacio_YTieneDato_SonComplementarios()
    {
        await using var db = NewContext(nameof(EstaVacio_YTieneDato_SonComplementarios));
        db.ProcedureInstances.AddRange(
            Instancia("R1", vin: "VIN0001"),
            Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (vacios, _) = await Filtrar(db, Cond(TramitesQueryFieldCatalog.Vin, QueryOperator.EstaVacio));
        var (conDato, _) = await Filtrar(db, Cond(TramitesQueryFieldCatalog.Vin, QueryOperator.NoEstaVacio));

        vacios.Should().Equal("R2");
        conDato.Should().Equal("R1");
    }

    // ── AC4 — campos nuevos ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FiltraPorIdTramite()
    {
        await using var db = NewContext(nameof(FiltraPorIdTramite));
        db.ProcedureInstances.AddRange(Instancia("TD-2026-00001"), Instancia("TD-2026-00002"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Radicado, QueryOperator.EsAlguno, "td202600002"));

        refs.Should().Equal("TD-2026-00002");
        total.Should().Be(1);
    }

    [Theory]
    // La migración gana sobre el origen operativo, igual que en `TramiteFuente.Desde`: un trámite
    // importado con origin='ict' es "Migrado", no "Integración".
    [InlineData("dashboard", new[] { "R1" })]
    [InlineData("integracion", new[] { "R2" })]
    [InlineData("migrado", new[] { "R3" })]
    public async Task FiltraPorFuente(string fuente, string[] esperado)
    {
        await using var db = NewContext($"fuente-{fuente}");
        db.ProcedureInstances.AddRange(
            Instancia("R1"),
            Instancia("R2", origin: "ict"),
            Instancia("R3", origin: "ict", migrado: true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Fuente, QueryOperator.EsAlguno, fuente));

        refs.Should().Equal(esperado);
    }

    [Fact]
    public async Task Comprador_BuscaPorNombreYPorDocumento()
    {
        await using var db = NewContext(nameof(Comprador_BuscaPorNombreYPorDocumento));
        var conDocumento = Instancia("R2", comprador: "Otra Persona");
        conDocumento.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = conDocumento.Id,
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "1098765",
            FullName = "Otra Persona",
        });
        db.ProcedureInstances.AddRange(Instancia("R1", comprador: "Ana Gómez"), conDocumento);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (porNombre, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Comprador, QueryOperator.EsAlguno, "Ana Gómez"));
        var (porDocumento, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Comprador, QueryOperator.EsAlguno, "1098765"));

        porNombre.Should().Equal("R1");
        porDocumento.Should().Equal("R2");
    }

    // ── AC2 — combinación y universo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task VariasCondiciones_SeCombinanConY()
    {
        await using var db = NewContext(nameof(VariasCondiciones_SeCombinanConY));
        db.ProcedureInstances.AddRange(
            Instancia("R1", plate: "ABC123", estado: TramiteEstado.Borrador),
            Instancia("R2", plate: "ABC123", estado: TramiteEstado.Entregado),
            Instancia("R3", plate: "XYZ999", estado: TramiteEstado.Borrador));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.Contiene, "ABC"),
            Cond(TramitesQueryFieldCatalog.Estado, QueryOperator.EsAlguno, TramiteEstado.Borrador));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }

    [Fact]
    public async Task ElTotalEsElDelUniverso_NoElDeLaPagina()
    {
        await using var db = NewContext(nameof(ElTotalEsElDelUniverso_NoElDeLaPagina));
        for (var i = 1; i <= 12; i++)
            db.ProcedureInstances.Add(Instancia($"R{i:00}", plate: "ABC123"));
        db.ProcedureInstances.Add(Instancia("Z99", plate: "XYZ999"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ProcedureInstanceRepository(db);
        var (items, total) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 5,
            new ProcedureInstanceListFilter
            {
                Condiciones = [Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.EsAlguno, "ABC123")],
            },
            ProcedureInstanceSortBy.Radicado, SortDirection.Ascending,
            TestContext.Current.CancellationToken);

        // La página trae 5, pero el total dice cuántos cumplen de verdad: es lo que necesita el
        // recorrido del export para saber cuándo parar.
        items.Should().HaveCount(5);
        total.Should().Be(12);
    }

    // ── AC7 — sin condiciones nada cambia ────────────────────────────────────────────────────

    [Fact]
    public async Task SinCondiciones_DevuelveTodo()
    {
        await using var db = NewContext(nameof(SinCondiciones_DevuelveTodo));
        db.ProcedureInstances.AddRange(Instancia("R1"), Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db);

        refs.Should().Equal("R1", "R2");
        total.Should().Be(2);
    }

    [Fact]
    public async Task ValoresTodosVacios_NoVacianElListado()
    {
        await using var db = NewContext(nameof(ValoresTodosVacios_NoVacianElListado));
        db.ProcedureInstances.AddRange(Instancia("R1", plate: "ABC123"), Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Filtrar por "nada" no es filtrar por una cadena vacía: devolvería cero y parecería un fallo.
        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.EsAlguno, "   ", ""));

        refs.Should().Equal("R1", "R2");
    }

    // ── AC6 — las claves de orden nuevas ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(ProcedureInstanceSortBy.Radicado, new[] { "A1", "B2", "C3" })]
    [InlineData(ProcedureInstanceSortBy.Estado, new[] { "C3", "A1", "B2" })]
    public async Task OrdenaPorLasClavesNuevas(ProcedureInstanceSortBy sortBy, string[] esperado)
    {
        await using var db = NewContext($"orden-{sortBy}");
        db.ProcedureInstances.AddRange(
            Instancia("A1", estado: TramiteEstado.Borrador),
            Instancia("B2", estado: TramiteEstado.Entregado),
            Instancia("C3", estado: TramiteEstado.Aprobado));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ProcedureInstanceRepository(db);
        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 100, new ProcedureInstanceListFilter(), sortBy, SortDirection.Ascending,
            TestContext.Current.CancellationToken);

        items.Select(i => i.ReferenceNumber).Should().Equal(esperado);
    }

    [Fact]
    public async Task OrdenaPorFuente_ConLaMismaPrecedenciaQueLaColumna()
    {
        await using var db = NewContext(nameof(OrdenaPorFuente_ConLaMismaPrecedenciaQueLaColumna));
        db.ProcedureInstances.AddRange(
            Instancia("R3", origin: "ict", migrado: true),
            Instancia("R1"),
            Instancia("R2", origin: "ict"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ProcedureInstanceRepository(db);
        var (items, _) = await repo.ListWithSummaryGraphFilteredAsync(
            TenantId, 0, 100, new ProcedureInstanceListFilter(),
            ProcedureInstanceSortBy.Fuente, SortDirection.Ascending,
            TestContext.Current.CancellationToken);

        // Dashboard → Integración → Migrado.
        items.Select(i => i.ReferenceNumber).Should().Equal("R1", "R2", "R3");
    }

    // ── Campos añadidos para acercar el listado a Consultas ───────────────────────────────────
    //
    // Salieron de una revisión en pantalla: el panel de /tramites ofrecía bastantes menos filtros
    // que "consultas personalizadas" sobre el MISMO universo de trámites.

    [Fact]
    public async Task Prioritario_FiltraPorLaColumna_YCuentaElUniverso()
    {
        await using var db = NewContext(nameof(Prioritario_FiltraPorLaColumna_YCuentaElUniverso));
        var marcado = Instancia("R1");
        marcado.Prioritario = true;
        db.ProcedureInstances.AddRange(marcado, Instancia("R2"), Instancia("R3"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prioritario, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }

    [Fact]
    public async Task Booleano_ConLasDosOpciones_NoAcotaNada()
    {
        await using var db = NewContext(nameof(Booleano_ConLasDosOpciones_NoAcotaNada));
        var marcado = Instancia("R1");
        marcado.Prioritario = true;
        db.ProcedureInstances.AddRange(marcado, Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // "Sí o no" es el universo entero: filtrar por ambas no puede vaciar ni recortar la lista.
        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prioritario, QueryOperator.EsAlguno, "true", "false"));

        refs.Should().Equal("R1", "R2");
        total.Should().Be(2);
    }

    [Fact]
    public async Task EnSubsanacion_FiltraPorLaColumnaDedicada()
    {
        await using var db = NewContext(nameof(EnSubsanacion_FiltraPorLaColumnaDedicada));
        var devuelto = Instancia("R1");
        devuelto.SubsanacionActiva = true;
        db.ProcedureInstances.AddRange(devuelto, Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.EnSubsanacion, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }

    [Fact]
    public async Task MetodoPago_NoEsNinguno_DejaPasarAlQueNoTieneDatosComerciales()
    {
        await using var db = NewContext(nameof(MetodoPago_NoEsNinguno_DejaPasarAlQueNoTieneDatosComerciales));
        var conPago = Instancia("R1");
        conPago.Commercial = new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = conPago.Id,
            MetodoPago = "EFECTIVO",
        };
        var otroPago = Instancia("R2");
        otroPago.Commercial = new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = otroPago.Id,
            MetodoPago = "TRANSFERENCIA",
        };
        // Sin fila comercial: no tiene método de pago, así que NO es "EFECTIVO".
        db.ProcedureInstances.AddRange(conPago, otroPago, Instancia("R3"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.MetodoPago, QueryOperator.NoEsNinguno, "EFECTIVO"));

        refs.Should().Equal("R2", "R3");
        total.Should().Be(2);
    }

}
