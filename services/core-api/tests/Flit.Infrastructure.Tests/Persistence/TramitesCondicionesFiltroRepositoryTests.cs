using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
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
        Consecutivo = RadicadoFixture.ConsecutivoDe(reference),
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

    // ── HU #12153 AC3 / HU #12371 AC7 — buscar por el radicado ─────────────────────────────────

    [Theory]
    [InlineData("4571")]
    [InlineData("0004571")]
    [InlineData("FT1-0004571")]
    [InlineData("ft1 4571")]
    public async Task BuscarPorElRadicado_LoLeeComoLoEscribeElUsuario(string valor)
    {
        // «Es alguno» lee el valor como radicado: sin prefijo casa el consecutivo, con prefijo el
        // texto canónico. Los cuatro son el mismo trámite.
        await using var db = NewContext($"{nameof(BuscarPorElRadicado_LoLeeComoLoEscribeElUsuario)}-{valor}");
        db.ProcedureInstances.AddRange(Instancia("FT1-0004571"), Instancia("FT1-0004572"), Instancia("FT2-0000571"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Radicado, QueryOperator.EsAlguno, valor));

        refs.Should().ContainSingle().Which.Should().Be("FT1-0004571");
        total.Should().Be(1);
    }

    [Fact]
    public async Task BuscarPorElRadicado_ConPrefijoDeOtraFamilia_NoCasa()
    {
        await using var db = NewContext(nameof(BuscarPorElRadicado_ConPrefijoDeOtraFamilia_NoCasa));
        db.ProcedureInstances.Add(Instancia("FT1-0004571"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Radicado, QueryOperator.EsAlguno, "FT2-0004571"));

        refs.Should().BeEmpty();
    }

    [Fact]
    public async Task BuscarPorParteDelConsecutivoNoSeLlevaAlQueSoloLoContiene()
    {
        // «contiene» sigue siendo contiene, sobre el texto sin guion: 571 aparece dentro de
        // FT10004571 y de FT20000571. Se deja explícito para que nadie lo lea como búsqueda
        // numérica exacta, que es lo que hace «es alguno».
        await using var db = NewContext(nameof(BuscarPorParteDelConsecutivoNoSeLlevaAlQueSoloLoContiene));
        db.ProcedureInstances.AddRange(Instancia("FT1-0004571"), Instancia("FT2-0000571"), Instancia("FT1-0000900"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Radicado, QueryOperator.Contiene, "571"));

        refs.Should().BeEquivalentTo(["FT2-0000571", "FT1-0004571"]);
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

    // ── Feature #12276 (HU #12312 AC3) — «Confirmado en RUNT» resuelve en SQL sobre el universo ───

    [Theory]
    [InlineData("yes", new[] { "C1" })]
    [InlineData("no", new[] { "N1", "N2" })]
    [InlineData("not_consulted", new[] { "S1" })]
    public async Task ConfirmadoRunt_FiltraPorLasColumnasDelTramite_SoloEntreAprobados(string valor, string[] esperado)
    {
        await using var db = NewContext(nameof(ConfirmadoRunt_FiltraPorLasColumnasDelTramite_SoloEntreAprobados) + valor);
        var confirmado = Instancia("C1", estado: TramiteEstado.Aprobado);
        confirmado.RuntConfirmedAt = Base;
        confirmado.RuntAttempts = 2;
        var pendiente = Instancia("N1", estado: TramiteEstado.Aprobado);
        pendiente.RuntAttempts = 1;
        var discrepancia = Instancia("N2", estado: TramiteEstado.Aprobado);
        discrepancia.RuntFlag = "discrepancia";
        var sinConsultar = Instancia("S1", estado: TramiteEstado.Aprobado);
        // No aprobado con intentos: la columna no aplica, así que ningún valor lo trae.
        var entregado = Instancia("E1", estado: TramiteEstado.Entregado);
        entregado.RuntAttempts = 1;
        db.ProcedureInstances.AddRange(confirmado, pendiente, discrepancia, sinConsultar, entregado);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.ConfirmadoRunt, QueryOperator.EsAlguno, valor));

        refs.Should().BeEquivalentTo(esperado);
        total.Should().Be(esperado.Length);
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


    // ── HU #12199 — filtrar por las dos marcas del listado ───────────────────────────────────
    //
    // Cada marca tiene DOS disparadores: el trámite LLEVA la capa encima, o el trámite ES la capa
    // (ADR-0050). Lo que estas pruebas defienden es que el WHERE reproduce los dos, porque mirar
    // solo uno da un filtro que parece funcionar —devuelve trámites, no falla— y silenciosamente
    // deja fuera filas que el listado sí está pintando con el ícono.

    /// <summary>Tipos de la familia OTROS, donde la capa ES el trámite. Instancias únicas: EF no
    /// admite dos objetos distintos con la misma clave adjuntos a la vez.</summary>
    private static readonly ProcedureType TipoCambioColor = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000b1"),
        Code = "CAMBIO_COLOR",
        Name = "Cambio de color",
        Family = "OTROS",
    };

    private static readonly ProcedureType TipoLevantamientoPrenda = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-0000000000b2"),
        Code = "LEVANTAMIENTO_PRENDA",
        Name = "Levantar prenda",
        Family = "OTROS",
    };

    private static ProcedureInstance DeTipo(string reference, ProcedureType tipo)
    {
        var instancia = Instancia(reference);
        instancia.ProcedureType = tipo;
        return instancia;
    }

    private static ProcedureInstance ConCampo(string reference, string clave, string valor)
    {
        var instancia = Instancia(reference);
        instancia.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = instancia.Id,
            FieldKey = clave,
            ValueText = valor,
            CreatedAt = Base,
        });
        return instancia;
    }

    private static ProcedureInstancePrenda Decision(
        ProcedureInstance instancia, string decision, string estado = PrendaEstado.Vigente) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = instancia.Id,
        Decision = decision,
        Estado = estado,
        CreatedAt = Base,
    };

    [Fact]
    public async Task Prenda_TraeTantoElGravamenVigenteComoElTramiteQueEsDePrenda()
    {
        // AC2. El segundo caso es el que se pierde si el WHERE solo mira la tabla de decisiones: un
        // LEVANTAMIENTO_PRENDA es un trámite de prenda desde que se abre, antes de que nadie haya
        // capturado nada, y el listado ya le pinta el ícono.
        await using var db = NewContext(nameof(Prenda_TraeTantoElGravamenVigenteComoElTramiteQueEsDePrenda));
        var conGravamen = Instancia("R1");
        db.ProcedureInstances.AddRange(conGravamen, DeTipo("R2", TipoLevantamientoPrenda), Instancia("R3"));
        db.ProcedureInstancePrendas.Add(Decision(conGravamen, PrendaDecision.Registrar));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1", "R2");
        total.Should().Be(2);
    }

    [Fact]
    public async Task Prenda_NoCuentaLaDecisionQueNoEsUnGravamen()
    {
        // `omitir` y `sin_prenda` son decisiones registradas, pero decir «este trámite no tiene
        // prenda» no es tenerla. Mismo WHERE que la consulta de la empresa y que el ícono.
        await using var db = NewContext(nameof(Prenda_NoCuentaLaDecisionQueNoEsUnGravamen));
        var omitida = Instancia("R1");
        var sinPrenda = Instancia("R2");
        var reemplazada = Instancia("R3");
        db.ProcedureInstances.AddRange(omitida, sinPrenda, reemplazada);
        db.ProcedureInstancePrendas.AddRange(
            Decision(omitida, PrendaDecision.Omitir),
            Decision(sinPrenda, PrendaDecision.SinPrenda),
            // Versionada: la fila existe, pero ya no es la vigente.
            Decision(reemplazada, PrendaDecision.Registrar, PrendaEstado.Reemplazada));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "true"));

        refs.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task Prenda_ElNoEsElComplementoExactoDelSi()
    {
        // AC5 en su forma más simple: los dos conjuntos parten el universo sin solaparse ni perder
        // filas. Si «No» se hubiera escrito como una condición aparte en vez de como la negación de
        // la misma expresión, aquí es donde se vería la grieta.
        await using var db = NewContext(nameof(Prenda_ElNoEsElComplementoExactoDelSi));
        var conGravamen = Instancia("R1");
        db.ProcedureInstances.AddRange(
            conGravamen, DeTipo("R2", TipoLevantamientoPrenda), Instancia("R3"), Instancia("R4"));
        db.ProcedureInstancePrendas.Add(Decision(conGravamen, PrendaDecision.Solicitar));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (conMarca, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "true"));
        var (sinMarca, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "false"));

        conMarca.Should().Equal("R1", "R2");
        sinMarca.Should().Equal("R3", "R4");
        conMarca.Should().NotIntersectWith(sinMarca);
    }

    [Fact]
    public async Task Transformacion_TraeLaDeclaradaYLaQueEsElTramite()
    {
        // AC3. Las cuatro claves son las del mandato, blindaje incluido: el catálogo de Consultas
        // solo lista tres y esta marca no hereda esa omisión.
        await using var db = NewContext(nameof(Transformacion_TraeLaDeclaradaYLaQueEsElTramite));
        db.ProcedureInstances.AddRange(
            ConCampo("R1", MandatoObjetoComposer.CambioCarroceria, "true"),
            ConCampo("R2", MandatoObjetoComposer.Blindaje, "true"),
            DeTipo("R3", TipoCambioColor),
            Instancia("R4"),
            // Declarada en falso: el campo existe, la transformación no.
            ConCampo("R5", MandatoObjetoComposer.CambioColor, "false"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Transformacion, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1", "R2", "R3");
        total.Should().Be(3);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("SÍ")]
    [InlineData("  true  ")]
    public async Task Transformacion_ReconoceLasFormasAfirmativasQueCirculanEnLosMigrados(string valor)
    {
        // El asistente guarda "true", pero por los trámites migrados de V1 circulan 1 y si, y con
        // espacios. El ícono los acepta todos; el filtro tiene que aceptar exactamente los mismos.
        // El nombre lleva el valor CRUDO: "true" y "  true  " son casos distintos y comparten
        // base si se normaliza, con lo que uno vería las filas del otro.
        await using var db = NewContext($"transf-afirmativo-[{valor}]");
        db.ProcedureInstances.AddRange(
            ConCampo("R1", MandatoObjetoComposer.CambioColor, valor), Instancia("R2"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Transformacion, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1");
    }

    [Fact]
    public async Task LasDosMarcasALaVezSeAcumulan()
    {
        // AC4. Dos condiciones son AND, así que solo pasa quien tiene las dos.
        await using var db = NewContext(nameof(LasDosMarcasALaVezSeAcumulan));
        var ambas = ConCampo("R1", MandatoObjetoComposer.CambioColor, "true");
        var soloPrenda = Instancia("R2");
        db.ProcedureInstances.AddRange(
            ambas, soloPrenda, ConCampo("R3", MandatoObjetoComposer.Blindaje, "true"));
        db.ProcedureInstancePrendas.AddRange(
            Decision(ambas, PrendaDecision.Registrar),
            Decision(soloPrenda, PrendaDecision.Registrar));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "true"),
            Cond(TramitesQueryFieldCatalog.Transformacion, QueryOperator.EsAlguno, "true"));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }

    [Fact]
    public async Task ElFiltroDevuelveExactamenteLoQueElDominioMarca()
    {
        // AC5 contra la fuente del ícono, no contra una lista escrita a mano: se recorre el mismo
        // universo con `TramiteMarcas` —que es lo que decide qué fila pinta el ícono— y se exige que
        // los dos conjuntos coincidan. Es la prueba que impide que el WHERE y la marca se separen.
        await using var db = NewContext(nameof(ElFiltroDevuelveExactamenteLoQueElDominioMarca));
        var conGravamen = Instancia("R1");
        db.ProcedureInstances.AddRange(
            conGravamen,
            DeTipo("R2", TipoLevantamientoPrenda),
            DeTipo("R3", TipoCambioColor),
            ConCampo("R4", MandatoObjetoComposer.CambioCombustible, "1"),
            ConCampo("R5", MandatoObjetoComposer.CambioColor, "false"),
            Instancia("R6"));
        db.ProcedureInstancePrendas.Add(Decision(conGravamen, PrendaDecision.Registrar));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var universo = await db.ProcedureInstances
            .Include(i => i.ProcedureType).Include(i => i.FieldValues)
            .ToListAsync(TestContext.Current.CancellationToken);
        var conPrendaVigente = db.ProcedureInstancePrendas
            .Where(p => p.Estado == PrendaEstado.Vigente
                && p.Decision != PrendaDecision.SinPrenda && p.Decision != PrendaDecision.Omitir)
            .Select(p => p.ProcedureInstanceId)
            .ToHashSet();

        var segunElDominio = (Func<Func<ProcedureInstance, bool>, string[]>)(predicado =>
            [.. universo.Where(predicado).Select(i => i.ReferenceNumber).Order()]);

        var (prendaSegunSql, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Prenda, QueryOperator.EsAlguno, "true"));
        var (transfSegunSql, _) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Transformacion, QueryOperator.EsAlguno, "true"));

        prendaSegunSql.Should().Equal(segunElDominio(i =>
            TramiteMarcas.TienePrenda(conPrendaVigente.Contains(i.Id), i.ProcedureType?.Code)));
        transfSegunSql.Should().Equal(segunElDominio(i => TramiteMarcas.TieneTransformacion(
            i.FieldValues.ToDictionary(fv => fv.FieldKey, fv => fv.ValueText),
            i.ProcedureType?.Code)));
    }

    // ── HU #12162 — «Gestor» en la gramática apunta al responsable de HOY ────────────────────

    [Fact]
    public async Task Gestor_FiltraPorElReasignadoYNoPorElCreador()
    {
        // La columna del listado muestra al reasignado desde la HU #12162. Si el filtro siguiera
        // casando contra quien radicó, buscar al responsable actual no encontraría nada y buscar al
        // anterior devolvería una fila que en pantalla lleva otro nombre.
        await using var db = NewContext(nameof(Gestor_FiltraPorElReasignadoYNoPorElCreador));
        var (creador, asignado) = (Guid.NewGuid(), Guid.NewGuid());
        db.Users.AddRange(
            new User { Id = creador, Email = "ana@flit.io", DisplayName = "Ana Gestora" },
            new User { Id = asignado, Email = "beto@flit.io", DisplayName = "Beto Gestor" });
        var reasignado = Instancia("R1");
        reasignado.CreatedByUserId = creador;
        reasignado.AssignedToUserId = asignado;
        var normal = Instancia("R2");
        normal.CreatedByUserId = creador;
        db.ProcedureInstances.AddRange(reasignado, normal);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (porElNuevo, totalNuevo) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Gestor, QueryOperator.Contiene, "Beto"));
        porElNuevo.Should().Equal("R1");
        totalNuevo.Should().Be(1);

        var (porElCreador, totalCreador) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Gestor, QueryOperator.Contiene, "Ana"));
        porElCreador.Should().Equal("R2");
        totalCreador.Should().Be(1);
    }

    [Fact]
    public async Task Gestor_SinReasignar_SigueCayendoAQuienRadico()
    {
        // El fallback de la HU #12162: un trámite que nunca se reasignó se sigue encontrando por
        // quien lo radicó. Es lo que evita que este arreglo se lleve por delante el comportamiento
        // de siempre para la inmensa mayoría de los trámites.
        await using var db = NewContext(nameof(Gestor_SinReasignar_SigueCayendoAQuienRadico));
        var creador = Guid.NewGuid();
        db.Users.Add(new User { Id = creador, Email = "ana@flit.io", DisplayName = "Ana Gestora" });
        var instancia = Instancia("R1");
        instancia.CreatedByUserId = creador;
        instancia.AssignedToUserId = null;
        db.ProcedureInstances.Add(instancia);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (refs, total) = await Filtrar(db,
            Cond(TramitesQueryFieldCatalog.Gestor, QueryOperator.EsAlguno, "Ana Gestora"));

        refs.Should().Equal("R1");
        total.Should().Be(1);
    }
}
