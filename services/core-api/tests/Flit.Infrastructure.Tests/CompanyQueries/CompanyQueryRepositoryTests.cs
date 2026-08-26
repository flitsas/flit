using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.CompanyQueries;

/// <summary>
/// Consultas propias de la empresa gestora.
///
/// <para>Lo que más se prueba aquí es la COBERTURA: que el resultado sepa explicar por qué falta lo
/// que el usuario pidió por nombre. Para una gestora pesa incluso más que para el organismo, porque
/// su trabajo es seguir lotes: pega las cuarenta placas de una flota y salen treinta y siete. Sin
/// una respuesta en pantalla, la conclusión es «se perdió un dato» y se vuelve a la consulta
/// manual.</para>
///
/// <para>Lo segundo es el aislamiento: una consulta de empresa NUNCA puede ver un trámite de otra.
/// Un fallo ahí no rompe nada visible — filtra datos ajenos.</para>
/// </summary>
public sealed class CompanyQueryRepositoryTests
{
    private static readonly Guid Empresa = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Organismo = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TipoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TipoMatriculaId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid Gustavo = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Cobertura ─────────────────────────────────────────────────────────────────────────────

    [Fact] // El caso real de una gestora: pega la lista de placas de una flota, activa un filtro más
           // y no salen todas. Sin esto la lectura natural es «se me perdió un dato».
    public async Task Cobertura_DicePorQueSeCayoCadaPlacaPedida()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);

            var conLt = Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(3));
            ConAdjunto(seed, conLt, "licencia_transito");

            // Existe y está en el rango, pero no tiene LT: el filtro la deja fuera.
            Tramite(seed, "REF-2", placa: "XYZ789", creadoEn: Hace(3));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(
            Cond(CompanyQueryFieldCatalog.Placa, "ABC123", "XYZ789", "NOP000"),
            Cond(CompanyQueryFieldCatalog.LicenciaTransito, "true")));

        result.Total.Should().Be(1);

        var cobertura = result.Cobertura.ToDictionary(c => c.Valor);

        cobertura["ABC123"].Resultado.Should().Be(QueryCoverageResult.Encontrado);

        cobertura["XYZ789"].Resultado.Should().Be(QueryCoverageResult.Excluido);
        cobertura["XYZ789"].MotivoCampo.Should().Be(CompanyQueryFieldCatalog.LicenciaTransito);
        cobertura["XYZ789"].Motivo.Should().Contain("Licencia de tránsito cargada");

        // No existir y quedar excluida son cosas distintas, y quien lee el informe necesita
        // distinguirlas: una se arregla aflojando un filtro y la otra no.
        cobertura["NOP000"].Resultado.Should().Be(QueryCoverageResult.NoExiste);
    }

    [Fact] // El aviso tiene que hablar del ámbito de quien pregunta. Decirle a un gestor que su placa
           // «no está en el organismo» lo mandaría a reclamar al sitio equivocado.
    public async Task Cobertura_HablaDeLaEmpresaYNoDelOrganismo()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(CompanyQueryFieldCatalog.Placa, "ABC123")));

        var item = result.Cobertura.Should().ContainSingle().Subject;
        item.Resultado.Should().Be(QueryCoverageResult.NoExiste);
        item.Motivo.Should().Contain("su empresa");
    }

    [Fact] // La fecha vive en la barra de arriba y no entre los chips, así que es el filtro que el
           // usuario tiene menos presente cuando le falta una fila. Se comprueba primero.
    public async Task Cobertura_SeñalaLaFechaCuandoElTramiteQuedaFueraDelRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(400));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(CompanyQueryFieldCatalog.Placa, "ABC123")));

        result.Total.Should().Be(0);

        var item = result.Cobertura.Should().ContainSingle().Subject;
        item.Resultado.Should().Be(QueryCoverageResult.Excluido);
        item.MotivoCampo.Should().Be(CompanyQueryDateField.Creacion);
        item.Motivo.Should().Contain("fuera del rango");
    }

    [Fact] // Una lista pegada desde Excel trae «ABC-123» tan a menudo como «ABC123». Si la
           // comparación dependiera de eso, el aviso se llenaría de «no existe» falsos, que es peor
           // que no tener aviso: enseña a desconfiar de él.
    public async Task Placa_IgnoraGuionesYEspaciosAlComparar()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(CompanyQueryFieldCatalog.Placa, " abc-123 ")));

        result.Total.Should().Be(1);
        result.Cobertura.Should().ContainSingle()
            .Which.Resultado.Should().Be(QueryCoverageResult.Encontrado);
    }

    // ── Aislamiento ───────────────────────────────────────────────────────────────────────────

    [Fact] // Un fallo aquí no rompe nada visible: enseña trámites de otra empresa. Se comprueba
           // incluso pidiendo la placa por nombre, que es el camino que empuja el filtro a SQL.
    public async Task NuncaSeVenLosTramitesDeOtraEmpresa()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-MIO", placa: "ABC123", creadoEn: Hace(3));
            Tramite(seed, "REF-AJENO", placa: "ZZZ999", creadoEn: Hace(3), tenantId: OtraEmpresa);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var todo = await RunAsync(db, Definir());
        todo.Total.Should().Be(1);
        todo.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-MIO");

        // Y pedirla por placa tampoco la saca: para esta empresa, sencillamente no existe.
        var porPlaca = await RunAsync(db, Definir(Cond(CompanyQueryFieldCatalog.Placa, "ZZZ999")));
        porPlaca.Total.Should().Be(0);
        porPlaca.Cobertura.Should().ContainSingle()
            .Which.Resultado.Should().Be(QueryCoverageResult.NoExiste);
    }

    // ── Una fila = un trámite ─────────────────────────────────────────────────────────────────

    [Fact] // Filtrar por comprador toca una tabla hija. Con un join directo, un trámite con dos
           // actores saldría dos veces y todos los totales quedarían inflados sin que nada fallara.
    public async Task UnTramiteConVariosActores_SigueSiendoUnaSolaFila()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            var id = Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(3));
            Actor(seed, id, "comprador", "Cándida Compradora", "1020304050");
            Actor(seed, id, "comprador", "Cornelio Comprador", "1020304051");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(
            db, Definir(Cond(CompanyQueryFieldCatalog.Comprador, "1020304050", "1020304051")));

        result.Total.Should().Be(1);
        result.Filas.Should().ContainSingle();
    }

    // ── Fechas ────────────────────────────────────────────────────────────────────────────────

    [Fact] // «Lo entregado en julio» no puede incluir lo que sigue en borrador. Sin fecha de envío,
           // la fila no cae en el rango — y es lo correcto: la pregunta era qué se entregó.
    public async Task FechaDeEnvio_DejaFueraLoQueSigueSinEntregar()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-ENVIADO", placa: "ABC123", creadoEn: Hace(5), enviadoEn: Hace(3));
            Tramite(seed, "REF-BORRADOR", placa: "DEF456", creadoEn: Hace(5),
                status: TramiteEstado.Borrador);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var porCreacion = await RunAsync(db, Definir());
        porCreacion.Total.Should().Be(2);

        var porEnvio = await RunAsync(db, Definir(campoFecha: CompanyQueryDateField.Envio));
        porEnvio.Total.Should().Be(1);
        porEnvio.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-ENVIADO");
    }

    [Fact] // «Fecha de aprobación» tiene que excluir lo rechazado: los dos cierran, pero solo uno
           // aprueba, y confundirlos mezclaría rechazados en un reporte de aprobados.
    public async Task FechaDeAprobacion_ExcluyeLoRechazadoAunqueTambienHayaCerrado()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);

            var aprobado = Tramite(seed, "REF-APROBADO", placa: "ABC123", creadoEn: Hace(5),
                status: TramiteEstado.Aprobado);
            Historia(seed, aprobado, TramiteEstado.Entregado, Hace(4));
            Historia(seed, aprobado, TramiteEstado.Aprobado, Hace(2));

            var rechazado = Tramite(seed, "REF-RECHAZADO", placa: "XYZ789", creadoEn: Hace(5),
                status: TramiteEstado.Rechazado);
            Historia(seed, rechazado, TramiteEstado.Entregado, Hace(4));
            Historia(seed, rechazado, TramiteEstado.Rechazado, Hace(2));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var porCreacion = await RunAsync(db, Definir());
        porCreacion.Total.Should().Be(2);

        var porAprobacion = await RunAsync(db, Definir(campoFecha: CompanyQueryDateField.Aprobacion));
        porAprobacion.Total.Should().Be(1);

        var fila = porAprobacion.Filas.Should().ContainSingle().Subject;
        fila.ReferenceNumber.Should().Be("REF-APROBADO");
        fila.AprobadoEn.Should().NotBeNull();

        // El rechazado también cerró (tiene un evento de decisión), pero nunca aprobó: el DTO no le
        // asigna esta fecha aunque se le pida por otro rango.
        var todos = await RunAsync(db, Definir());
        todos.Filas.Single(f => f.ReferenceNumber == "REF-RECHAZADO").AprobadoEn.Should().BeNull();
    }

    // ── Catálogo ──────────────────────────────────────────────────────────────────────────────

    [Fact] // Esta prueba sostiene la promesa del catálogo: un campo declarado sin traducción en el
           // repositorio no filtraría nada EN SILENCIO, y el usuario leería el resultado vacío como
           // «no hay trámites así» en vez de «este filtro no está conectado».
    public async Task TodoCampoDelCatalogo_FiltraDeVerdad()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);

            var id = Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(5),
                status: TramiteEstado.Aprobado, vin: "VIN00000000001", prioritario: true,
                enSubsanacion: true, modalidad: "traspaso");

            Actor(seed, id, "comprador", "Cándida Compradora", "1020304050");
            Actor(seed, id, "vendedor", "Vera Vendedora", "9080706050");
            Prenda(seed, id, PrendaEstado.Vigente, PrendaDecision.Registrar, "Banco X");
            ConAdjunto(seed, id, "licencia_transito");
            Valor(seed, id, "cambio_color");
            Valor(seed, id, "es_leasing");
            Valor(seed, id, "es_unilateral");
            ConPago(seed, id, "Efectivo");

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var esperado = new Dictionary<string, string>
        {
            [CompanyQueryFieldCatalog.Placa] = "ABC123",
            [CompanyQueryFieldCatalog.Vin] = "VIN00000000001",
            [CompanyQueryFieldCatalog.Radicado] = "REF-1",
            [CompanyQueryFieldCatalog.Comprador] = "1020304050",
            [CompanyQueryFieldCatalog.Vendedor] = "Vera Vendedora",
            [CompanyQueryFieldCatalog.Organismo] = Organismo.ToString(),
            [CompanyQueryFieldCatalog.TipoTramite] = TipoId.ToString(),
            [CompanyQueryFieldCatalog.Estado] = "aprobado",
            [CompanyQueryFieldCatalog.RadicadoPor] = Gustavo.ToString(),
            [CompanyQueryFieldCatalog.Prioritario] = "true",
            [CompanyQueryFieldCatalog.EnSubsanacion] = "true",
            [CompanyQueryFieldCatalog.Prenda] = "true",
            [CompanyQueryFieldCatalog.LicenciaTransito] = "true",
            [CompanyQueryFieldCatalog.Transformaciones] = "cambio_color",
            [CompanyQueryFieldCatalog.Leasing] = "true",
            [CompanyQueryFieldCatalog.MetodoPago] = "Efectivo",
            [CompanyQueryFieldCatalog.TipoTraspaso] = CompanyQueryFieldCatalog.TraspasoUnilateral,
            [CompanyQueryFieldCatalog.Compania] = Empresa.ToString(),
        };

        esperado.Keys.Should().BeEquivalentTo(
            CompanyQueryFieldCatalog.Fields.Select(f => f.Id),
            "cada campo del catálogo necesita su traducción y su caso aquí");

        foreach (var (fieldId, valor) in esperado)
        {
            var result = await RunAsync(db, Definir(Cond(fieldId, valor)));

            result.Total.Should().Be(1, $"el campo «{fieldId}» debería filtrar por «{valor}»");
        }
    }

    [Fact] // Un filtro por «bilateral» que arrastre todas las matrículas iniciales es peor que no
           // tener el filtro: el usuario no tiene forma de notar que está mal.
    public async Task TipoDeTraspaso_NoClasificaLasMatriculasIniciales()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-MAT", placa: "ABC123", creadoEn: Hace(3));
            Tramite(seed, "REF-TRA", placa: "DEF456", creadoEn: Hace(3), modalidad: "traspaso");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(
            CompanyQueryFieldCatalog.TipoTraspaso, CompanyQueryFieldCatalog.TraspasoBilateral)));

        result.Total.Should().Be(1);
        result.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-TRA");
    }

    [Fact] // Ofrecer un organismo con el que la empresa nunca ha tramitado es ofrecer un filtro que
           // solo puede devolver cero, y el usuario lo lee como un fallo del reporte.
    public async Task Catalogo_SoloOfreceLoQueLaEmpresaTieneDeVerdad()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            seed.TransitOffices.Add(new TransitOffice
            {
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Code = "99999",
                Name = "Organismo con el que no trabajamos",
            });

            Tramite(seed, "REF-1", placa: "ABC123", creadoEn: Hace(3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var fields = await new CompanyQueryRepository(ctx)
            .GetFieldsAsync(Empresa, TestContext.Current.CancellationToken);

        var organismos = fields.Single(f => f.Id == CompanyQueryFieldCatalog.Organismo).Options;

        organismos.Should().ContainSingle()
            .Which.Value.Should().Be(Organismo.ToString());
    }

    // ── Consultas guardadas ───────────────────────────────────────────────────────────────────

    [Fact] // Las de fábrica existen para que la lista nunca esté vacía, y van al final: son el punto
           // de partida, no lo que alguien viene a buscar cuando ya tiene las suyas.
    public async Task Guardadas_DevuelveLasPropiasYLuegoLasDeFabrica()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        await repo.SaveAsync(Empresa, Gustavo, null,
            new SavedQueryInput("Mi consulta", null, Definir()),
            TestContext.Current.CancellationToken);

        var lista = await repo.ListSavedAsync(Empresa, Gustavo, TestContext.Current.CancellationToken);

        lista[0].Nombre.Should().Be("Mi consulta");
        lista[0].DeFabrica.Should().BeFalse();
        lista.Skip(1).Should().OnlyContain(q => q.DeFabrica);
        lista.Should().HaveCount(1 + CompanyFactoryQueries.Queries.Count);
    }

    [Fact] // Quien guarda con un nombre repetido casi siempre quería sobrescribir la que ya tenía;
           // crearle una copia silenciosa le deja dos consultas indistinguibles en la lista.
    public async Task Guardadas_NoDejaRepetirElNombre()
    {
        var db = NewDbName();

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        await repo.SaveAsync(Empresa, Gustavo, null,
            new SavedQueryInput("Revisión de los lunes", null, Definir()),
            TestContext.Current.CancellationToken);

        var repetir = async () => await repo.SaveAsync(Empresa, Gustavo, null,
            new SavedQueryInput("  revisión de los lunes  ", null, Definir()),
            TestContext.Current.CancellationToken);

        await repetir.Should().ThrowAsync<SavedQueryNameTakenException>();
    }

    [Fact] // Las de fábrica no viven en la base y tienen que seguir estando ahí para el siguiente que
           // abra la consola: guardar sobre una es duplicarla, no editarla.
    public async Task Guardadas_GuardarSobreUnaDeFabricaLaDuplica()
    {
        var db = NewDbName();
        var deFabrica = CompanyFactoryQueries.Queries[0];

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        var guardada = await repo.SaveAsync(Empresa, Gustavo, deFabrica.Id,
            new SavedQueryInput("Mi versión", null, deFabrica.Definition),
            TestContext.Current.CancellationToken);

        guardada.Id.Should().NotBe(deFabrica.Id);
        guardada.DeFabrica.Should().BeFalse();

        var lista = await repo.ListSavedAsync(Empresa, Gustavo, TestContext.Current.CancellationToken);
        lista.Should().Contain(q => q.Id == deFabrica.Id && q.DeFabrica);
    }

    [Fact] // Una consulta guardada es del usuario y de su empresa. Verla desde otra sería el mismo
           // fallo de aislamiento que ver sus trámites.
    public async Task Guardadas_NoSeVenDesdeOtraEmpresa()
    {
        var db = NewDbName();

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        await repo.SaveAsync(Empresa, Gustavo, null,
            new SavedQueryInput("Solo mía", null, Definir()),
            TestContext.Current.CancellationToken);

        var ajenas = await repo.ListSavedAsync(OtraEmpresa, Gustavo, TestContext.Current.CancellationToken);

        ajenas.Should().OnlyContain(q => q.DeFabrica);
    }

    // ── Orden y paginación ────────────────────────────────────────────────────────────────────

    [Fact] // Sin desempate estable, dos filas con la misma fecha pueden cambiar de sitio entre
           // páginas — y el export encadena páginas: se perderían filas y se repetirían otras.
    public async Task Orden_EsEstableEntrePaginasConFechasIguales()
    {
        var db = NewDbName();
        var mismaFecha = Hace(3);

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            foreach (var n in Enumerable.Range(1, 6))
            {
                Tramite(seed, $"REF-{n}", placa: $"AAA{n:000}", creadoEn: mismaFecha);
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var primera = await RunAsync(db, Definir(), page: 1, pageSize: 3);
        var segunda = await RunAsync(db, Definir(), page: 2, pageSize: 3);

        var vistos = primera.Filas.Concat(segunda.Filas).Select(f => f.ReferenceNumber).ToList();

        vistos.Should().OnlyHaveUniqueItems();
        vistos.Should().HaveCount(6);
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static QueryCondition Cond(string fieldId, params string[] values) =>
        new(fieldId, QueryOperator.EsAlguno, values);

    private static QueryDefinition Definir(params QueryCondition[] condiciones) =>
        Definir(CompanyQueryDateField.Creacion, condiciones);

    private static QueryDefinition Definir(string campoFecha, params QueryCondition[] condiciones) =>
        new(new QueryDateFilter(campoFecha, QueryRangePreset.Ultimos30), condiciones, []);

    private static async Task<CompanyQueryResultDto> RunAsync(
        string db,
        QueryDefinition definition,
        int page = 1,
        int pageSize = 50)
    {
        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        return await repo.ExecuteAsync(
            Empresa,
            new QueryRequest(definition, page, pageSize),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Hace <paramref name="dias"/> días, a media mañana de Bogotá. La hora se fija a propósito:
    /// sin eso una prueba lanzada cerca de medianoche cae en el día anterior y falla sola.
    /// </summary>
    private static DateTimeOffset Hace(int dias)
    {
        var hoy = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BogotaZone).DateTime);
        var dia = hoy.AddDays(-dias);

        return new DateTimeOffset(dia.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-5))
            .ToUniversalTime();
    }

    private static readonly TimeZoneInfo BogotaZone =
        TimeZoneInfo.CreateCustomTimeZone("Bogota", TimeSpan.FromHours(-5), "Bogota", "Bogota");

    private static Guid Tramite(
        FlitDbContext ctx,
        string reference,
        string placa,
        DateTimeOffset creadoEn,
        string status = TramiteEstado.Entregado,
        Guid? tenantId = null,
        string? vin = null,
        bool prioritario = false,
        bool enSubsanacion = false,
        string modalidad = "matricula_inicial",
        DateTimeOffset? enviadoEn = null)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId ?? Empresa,
            ProcedureTypeId = modalidad.Contains("traspaso", StringComparison.OrdinalIgnoreCase)
                ? TipoId
                : TipoMatriculaId,
            ReferenceNumber = reference,
            Status = status,
            Plate = placa,
            Vin = vin,
            Prioritario = prioritario,
            SubsanacionActiva = enSubsanacion,
            TransitOfficeId = Organismo,
            CreatedByUserId = Gustavo,
            CreatedAt = creadoEn,
            SubmittedAt = enviadoEn,
        });

        return id;
    }

    private static void Historia(
        FlitDbContext ctx, Guid instanceId, string toStatus, DateTimeOffset at, Guid? tenantId = null) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Empresa,
            ProcedureInstanceId = instanceId,
            FromStatus = null,
            ToStatus = toStatus,
            ChangedAt = at,
        });

    private static void Actor(
        FlitDbContext ctx, Guid instanceId, string actorType, string nombre, string documento) =>
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = nombre,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void Prenda(
        FlitDbContext ctx, Guid instanceId, string estado, string decision, string? acreedor) =>
        ctx.ProcedureInstancePrendas.Add(new ProcedureInstancePrenda
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            ProcedureInstanceId = instanceId,
            Estado = estado,
            Decision = decision,
            AcreedorNombre = acreedor,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void ConAdjunto(FlitDbContext ctx, Guid instanceId, string tipo) =>
        ctx.ProcedureInstanceAttachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            ProcedureInstanceId = instanceId,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            Sha256 = "abc",
            StoragePath = $"x/{tipo}.pdf",
            UploadedAt = DateTimeOffset.UtcNow,
        });

    private static void Valor(FlitDbContext ctx, Guid instanceId, string clave) =>
        ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            ProcedureInstanceId = instanceId,
            FieldKey = clave,
            ValueText = "true",
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void ConPago(FlitDbContext ctx, Guid instanceId, string metodo) =>
        ctx.ProcedureInstanceCommercials.Add(new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            TenantId = Empresa,
            ProcedureInstanceId = instanceId,
            MetodoPago = metodo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void SeedCatalogos(FlitDbContext ctx)
    {
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Organismo,
            Code = "11001",
            Name = "Secretaría de Movilidad",
        });

        // ADR-0050 — la familia del tipo es lo que clasifica el expediente, así que hacen falta dos
        // tipos para poder distinguir un traspaso de una matrícula en los filtros.
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoId,
            Code = "TRASPASO_STANDARD",
            Name = "Traspaso de vehículo",
            Family = "TRASPASO",
        });

        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoMatriculaId,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
        });

        ctx.Users.Add(new User
        {
            Id = Gustavo,
            Email = "gustavo@gestora.local",
            DisplayName = "Gustavo Gestor",
        });
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
