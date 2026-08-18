using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtQueries;

/// <summary>
/// Consultas del organismo.
///
/// <para>Lo que más se prueba aquí es la COBERTURA: que el resultado sepa explicar por qué falta lo
/// que el usuario pidió por nombre. Un reporte que devuelve menos filas de las esperadas sin decir
/// por qué no es un reporte incompleto — es un reporte que deja de usarse, porque la primera vez que
/// alguien sospecha que se perdió un dato vuelve a la consulta manual.</para>
///
/// <para>Lo segundo es que una fila siga siendo un trámite pase lo que pase: los filtros sobre
/// tablas hijas son la vía clásica por la que un listado empieza a duplicar filas y a inflar
/// totales sin que nada falle.</para>
/// </summary>
public sealed class OtQueryRepositoryTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Carla = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Cobertura ─────────────────────────────────────────────────────────────────────────────

    [Fact] // El caso que Samuel describió: dos placas, un filtro más, y una de las dos no sale. Sin
           // esto la lectura natural del usuario es «se me perdió un dato».
    public async Task Cobertura_DicePorQueSeCayoCadaPlacaPedida()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var conLt = Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(3));
            ConLicencia(seed, conLt);

            // Existe y está en el rango, pero no tiene LT: el filtro la deja fuera.
            Radicar(seed, "REF-2", placa: "XYZ789", radicadoEn: Hace(3));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(
            Cond(OtQueryFieldCatalog.Placa, "ABC123", "XYZ789", "NOP000"),
            Cond(OtQueryFieldCatalog.LicenciaTransito, "true")));

        result!.Total.Should().Be(1);

        var cobertura = result.Cobertura.ToDictionary(c => c.Valor);

        cobertura["ABC123"].Resultado.Should().Be(QueryCoverageResult.Encontrado);

        cobertura["XYZ789"].Resultado.Should().Be(QueryCoverageResult.Excluido);
        cobertura["XYZ789"].MotivoCampo.Should().Be(OtQueryFieldCatalog.LicenciaTransito);
        cobertura["XYZ789"].Motivo.Should().Contain("Licencia de tránsito cargada");

        // No existir y quedar excluida son cosas distintas, y quien lee el informe necesita
        // distinguirlas: una se arregla aflojando un filtro y la otra no.
        cobertura["NOP000"].Resultado.Should().Be(QueryCoverageResult.NoExiste);
    }

    [Fact] // La fecha vive en la barra de arriba y no entre los chips, así que es el filtro que el
           // usuario tiene menos presente cuando le falta una fila. Se comprueba primero.
    public async Task Cobertura_SeñalaLaFechaCuandoElTramiteQuedaFueraDelRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(400));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Placa, "ABC123")));

        result!.Total.Should().Be(0);

        var item = result.Cobertura.Should().ContainSingle().Subject;
        item.Resultado.Should().Be(QueryCoverageResult.Excluido);
        item.MotivoCampo.Should().Be(OtQueryDateField.Radicacion);
        item.Motivo.Should().Contain("fuera del rango");
    }

    [Fact] // Una lista pegada desde Excel trae «ABC-123» tan a menudo como «ABC123». Si la
           // comparación dependiera de eso, el aviso de cobertura se llenaría de «no existe» falsos,
           // que es peor que no tener aviso: enseña a desconfiar de él.
    public async Task Placa_IgnoraGuionesYEspaciosAlComparar()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Placa, " abc-123 ")));

        result!.Total.Should().Be(1);
        result.Cobertura.Should().ContainSingle()
            .Which.Resultado.Should().Be(QueryCoverageResult.Encontrado);
    }

    [Fact] // Solo se rinden cuentas de lo que el usuario escribió valor a valor. Avisar de que
           // «traspaso» no salió en un filtro por tipo de trámite sería ruido disfrazado de rigor.
    public async Task Cobertura_SoloAplicaALosCamposQueElUsuarioEnumera()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(3));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(
            Cond(OtQueryFieldCatalog.TipoTramite, "traspaso")));

        result!.Cobertura.Should().BeEmpty();
    }

    // ── Una fila = un trámite ─────────────────────────────────────────────────────────────────

    [Fact] // La trampa clásica: filtrar por una tabla hija con un cruce duplica la fila del padre
           // por cada hija que coincide, y todos los totales quedan inflados sin que nada falle.
    public async Task FiltrarPorActores_NoDuplicaElTramite()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var id = Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(3));

            // Dos actores del mismo rol: el trámite sigue siendo uno.
            Actor(seed, id, "comprador", "Cándida Compradora", "1020304050");
            Actor(seed, id, "comprador", "Cándido Comprador", "1020304051");

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(
            Cond(OtQueryFieldCatalog.Comprador, "1020304050", "1020304051")));

        result!.Total.Should().Be(1);
        result.Filas.Should().ContainSingle();
    }

    [Fact] // Quien tiene la cédula a mano no debería tener que averiguar cómo se escribió el nombre.
    public async Task Comprador_BuscaPorNombreYTambienPorDocumento()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var id = Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(3));
            Actor(seed, id, "comprador", "Cándida Compradora", "1020304050");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Comprador, "1020304050"))))!
            .Total.Should().Be(1);

        (await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Comprador, "Cándida Compradora"))))!
            .Total.Should().Be(1);
    }

    // ── Semántica de los campos ───────────────────────────────────────────────────────────────

    [Fact] // Las filas de prenda son versionadas: cada cambio deja la anterior como reemplazada.
           // Mirarlas todas diría «tiene prenda» de un trámite al que se le quitó.
    public async Task Prenda_SoloCuentaLaVigenteYNoLaDecisionDeNoTenerla()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var vigente = Radicar(seed, "REF-1", placa: "AAA111", radicadoEn: Hace(3));
            Prenda(seed, vigente, PrendaEstado.Vigente, PrendaDecision.Registrar, "Banco X");

            var levantada = Radicar(seed, "REF-2", placa: "BBB222", radicadoEn: Hace(3));
            Prenda(seed, levantada, PrendaEstado.Reemplazada, PrendaDecision.Registrar, "Banco Y");

            var sinPrenda = Radicar(seed, "REF-3", placa: "CCC333", radicadoEn: Hace(3));
            Prenda(seed, sinPrenda, PrendaEstado.Vigente, PrendaDecision.SinPrenda, null);

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var conPrenda = await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Prenda, "true")));

        conPrenda!.Total.Should().Be(1);
        conPrenda.Filas.Single().ReferenceNumber.Should().Be("REF-1");
        conPrenda.Filas.Single().AcreedorPrenda.Should().Be("Banco X");
    }

    [Fact] // «No es ninguna» sobre las tres transformaciones es cómo se pregunta por los trámites
           // sin transformaciones, sin tener que inventar un campo aparte para la negación.
    public async Task Transformaciones_PreguntaPorCualesYTambienPorNinguna()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var conColor = Radicar(seed, "REF-1", placa: "AAA111", radicadoEn: Hace(3));
            Transformacion(seed, conColor, "cambio_color");

            var conCarroceria = Radicar(seed, "REF-2", placa: "BBB222", radicadoEn: Hace(3));
            Transformacion(seed, conCarroceria, "cambio_carroceria");

            Radicar(seed, "REF-3", placa: "CCC333", radicadoEn: Hace(3));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var soloColor = await RunAsync(db, Definir(
            Cond(OtQueryFieldCatalog.Transformaciones, "cambio_color")));
        soloColor!.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-1");

        var ninguna = await RunAsync(db, Definir(
            new QueryCondition(
                OtQueryFieldCatalog.Transformaciones,
                QueryOperator.NoEsNinguno,
                ["cambio_color", "cambio_carroceria", "cambio_combustible"])));

        ninguna!.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-3");
    }

    [Fact] // Un trámite sin decidir no aparece en una consulta filtrada por fecha de decisión, y es
           // lo correcto: la pregunta era qué se decidió en esas fechas. Colarlo haría que el mismo
           // filtro significara cosas distintas según la fila.
    public async Task FechaDeDecision_DejaFueraLoQueSigueSinDecidirse()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var decidido = Radicar(seed, "REF-1", placa: "AAA111", radicadoEn: Hace(10),
                status: TramiteEstado.Aprobado);
            Decidir(seed, decidido, TramiteEstado.Aprobado, Hace(2));

            Radicar(seed, "REF-2", placa: "BBB222", radicadoEn: Hace(10));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var porDecision = await RunAsync(db, new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Decision, QueryRangePreset.Ultimos30),
            [],
            []));

        porDecision!.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-1");

        var porRadicacion = await RunAsync(db, new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30),
            [],
            []));

        porRadicacion!.Total.Should().Be(2);
    }

    [Fact] // «Fecha de aprobación» tiene que excluir lo rechazado: los dos decidieron, pero solo uno
           // aprobó, y confundirlos mezclaría rechazados en un reporte de aprobados.
    public async Task FechaDeAprobacion_ExcluyeLoRechazadoAunqueTambienHayaDecidido()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var aprobado = Radicar(seed, "REF-APROBADO", placa: "AAA111", radicadoEn: Hace(10),
                status: TramiteEstado.Aprobado);
            Decidir(seed, aprobado, TramiteEstado.Aprobado, Hace(2));

            var rechazado = Radicar(seed, "REF-RECHAZADO", placa: "BBB222", radicadoEn: Hace(10),
                status: TramiteEstado.Rechazado);
            Decidir(seed, rechazado, TramiteEstado.Rechazado, Hace(2));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var porDecision = await RunAsync(db, new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Decision, QueryRangePreset.Ultimos30),
            [],
            []));
        porDecision!.Total.Should().Be(2);

        var porAprobacion = await RunAsync(db, new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Aprobacion, QueryRangePreset.Ultimos30),
            [],
            []));

        var fila = porAprobacion!.Filas.Should().ContainSingle().Subject;
        fila.ReferenceNumber.Should().Be("REF-APROBADO");
        fila.AprobadoEn.Should().NotBeNull();

        porDecision.Filas.Single(f => f.ReferenceNumber == "REF-RECHAZADO").AprobadoEn.Should().BeNull();
    }

    [Fact] // Un organismo no puede ver los trámites de una empresa sin convenio, venga la consulta
           // como venga. Es el límite que ninguna condición del usuario puede aflojar.
    public async Task Alcance_NoDevuelveTramitesDeEmpresasSinConvenio()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", placa: "AAA111", radicadoEn: Hace(3), tenantId: OtraEmpresa);

            // Sin convenio: no debe salir ni siquiera pidiéndola por placa.
            var ajena = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
            Radicar(seed, "REF-2", placa: "ZZZ999", radicadoEn: Hace(3), tenantId: ajena);

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir(Cond(OtQueryFieldCatalog.Placa, "AAA111", "ZZZ999")));

        result!.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-1");
        result.Cobertura.Single(c => c.Valor == "ZZZ999")
            .Resultado.Should().Be(QueryCoverageResult.NoExiste);
    }

    // ── Rangos y comparación ──────────────────────────────────────────────────────────────────

    [Fact] // Los rangos se guardan en relativo justamente para esto: la consulta tiene que seguir
           // significando lo mismo cuando pase el mes.
    public void PresetRelativo_SeResuelveContraElDiaDeHoy()
    {
        var hoy = new DateOnly(2026, 8, 5);

        QueryRangePreset.Resolve(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.MesAnterior), hoy)
            .Should().Be((new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        QueryRangePreset.Resolve(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos7), hoy)
            .Should().Be((new DateOnly(2026, 7, 30), hoy));

        // Un preset que dejó de existir abre la consulta en el defecto en vez de reventar: una
        // consulta guardada que ya no se puede abrir es una consulta perdida.
        QueryRangePreset.Resolve(
            new QueryDateFilter(OtQueryDateField.Radicacion, "inventado"), hoy)
            .Should().Be((hoy.AddDays(-29), hoy));
    }

    [Fact] // El periodo anterior es de igual ancho y pegado al actual. Sirve para decir «12 % más
           // que antes» sin obligar a nadie a ejecutar la consulta dos veces.
    public async Task PeriodoAnterior_CuentaLaMismaConsultaEnLaVentanaPrevia()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", placa: "AAA111", radicadoEn: Hace(3));
            Radicar(seed, "REF-2", placa: "BBB222", radicadoEn: Hace(5));
            // Dentro de los 7 días ANTERIORES a la ventana de 7 días.
            Radicar(seed, "REF-3", placa: "CCC333", radicadoEn: Hace(10));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, new QueryDefinition(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos7),
            [],
            []));

        result!.Total.Should().Be(2);
        result.TotalPeriodoAnterior.Should().Be(1);
    }

    // ── Normalización ─────────────────────────────────────────────────────────────────────────

    [Fact] // Lo que llega de la red es una propuesta, no una definición. Un campo inventado se
           // descarta en silencio en vez de tumbar la consulta entera: si tumbara, un despliegue que
           // retirara un campo dejaría inservibles las consultas guardadas que lo usaban.
    public void Normalize_DescartaLoDesconocidoYDejaElRestoIntacto()
    {
        var definition = OtQueryFieldCatalog.Normalize(new QueryDefinition(
            new QueryDateFilter("inventado", "raro"),
            [
                new QueryCondition("campo_que_no_existe", QueryOperator.EsAlguno, ["x"]),
                new QueryCondition(OtQueryFieldCatalog.Placa, "operador_raro", ["x"]),
                // Sin valores no restringe nada; descartarla evita romper la consulta justo cuando el
                // usuario acaba de borrar el último valor para escribir otro.
                new QueryCondition(OtQueryFieldCatalog.Vin, QueryOperator.EsAlguno, []),
                new QueryCondition(OtQueryFieldCatalog.Placa, QueryOperator.EsAlguno, ["ABC123", "ABC123"]),
            ],
            [],
            "orden_inventado"));

        definition.Fechas.Campo.Should().Be(OtQueryDateField.Radicacion);
        definition.Fechas.Preset.Should().Be(QueryRangePreset.Ultimos30);
        definition.SortBy.Should().Be(OtQuerySort.Radicado);

        var condicion = definition.Condiciones.Should().ContainSingle().Subject;
        condicion.FieldId.Should().Be(OtQueryFieldCatalog.Placa);
        condicion.Values.Should().Equal("ABC123");
    }

    [Fact] // El catálogo promete que agregar un campo consultable es tocar un solo archivo. Esta
           // prueba es lo que hace que la promesa sea cierta: un campo declarado sin traducción en el
           // repositorio no filtraría nada y lo haría en SILENCIO, que es la peor forma de fallar.
    public async Task TodoCampoDelCatalogo_FiltraDeVerdad()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var id = Radicar(seed, "REF-1", placa: "ABC123", radicadoEn: Hace(5),
                status: TramiteEstado.Aprobado, prioritario: true, vin: "VIN00000000001");
            Decidir(seed, id, TramiteEstado.Aprobado, Hace(2));
            Actor(seed, id, "comprador", "Cándida Compradora", "1020304050");
            Actor(seed, id, "vendedor", "Vera Vendedora", "9080706050");
            Prenda(seed, id, PrendaEstado.Vigente, PrendaDecision.Registrar, "Banco X");
            ConLicencia(seed, id);
            Transformacion(seed, id, "cambio_color");

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var esperado = new Dictionary<string, string>
        {
            [OtQueryFieldCatalog.Placa] = "ABC123",
            [OtQueryFieldCatalog.Vin] = "VIN00000000001",
            [OtQueryFieldCatalog.Radicado] = "REF-1",
            [OtQueryFieldCatalog.Comprador] = "1020304050",
            [OtQueryFieldCatalog.Vendedor] = "Vera Vendedora",
            [OtQueryFieldCatalog.Empresa] = ClientTenant.ToString(),
            [OtQueryFieldCatalog.TipoTramite] = "matricula_inicial",
            [OtQueryFieldCatalog.Estado] = "aprobado",
            [OtQueryFieldCatalog.Revisor] = Carla.ToString(),
            [OtQueryFieldCatalog.Prioritario] = "true",
            [OtQueryFieldCatalog.Prenda] = "true",
            [OtQueryFieldCatalog.LicenciaTransito] = "true",
            [OtQueryFieldCatalog.Transformaciones] = "cambio_color",
        };

        esperado.Keys.Should().BeEquivalentTo(
            OtQueryFieldCatalog.Fields.Select(f => f.Id),
            "cada campo del catálogo necesita su traducción y su caso aquí");

        foreach (var (fieldId, valor) in esperado)
        {
            var result = await RunAsync(db, Definir(Cond(fieldId, valor)));

            result!.Total.Should().Be(1, $"el campo «{fieldId}» debería filtrar por «{valor}»");
        }
    }

    // ── Consultas guardadas ───────────────────────────────────────────────────────────────────

    [Fact] // Las de fábrica existen para que la lista nunca esté vacía, y van al final: son el punto
           // de partida, no lo que alguien viene a buscar cuando ya tiene las suyas.
    public async Task Guardadas_DevuelveLasPropiasYLuegoLasDeFabrica()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        await repo.SaveAsync(
            OtTenant, Carla, null,
            new SavedQueryInput("Mi consulta", null, Definir(Cond(OtQueryFieldCatalog.Placa, "ABC123"))),
            cancellationToken: TestContext.Current.CancellationToken);

        var lista = await repo.ListSavedAsync(
            OtTenant, Carla, cancellationToken: TestContext.Current.CancellationToken);

        lista.Should().NotBeNull();
        lista![0].Nombre.Should().Be("Mi consulta");
        lista[0].DeFabrica.Should().BeFalse();
        lista.Skip(1).Should().OnlyContain(q => q.DeFabrica);
        lista.Should().HaveCount(1 + OtFactoryQueries.Queries.Count);
    }

    [Fact] // Quien guarda con un nombre repetido casi siempre quería sobrescribir. Crear una copia
           // silenciosa le deja dos consultas indistinguibles en la lista.
    public async Task Guardadas_NoAdmiteDosConElMismoNombre()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);
        var input = new SavedQueryInput("Rechazados", null, Definir());

        await repo.SaveAsync(OtTenant, Carla, null, input,
            cancellationToken: TestContext.Current.CancellationToken);

        var repetir = async () => await repo.SaveAsync(
            OtTenant, Carla, null, input, cancellationToken: TestContext.Current.CancellationToken);

        await repetir.Should().ThrowAsync<SavedQueryNameTakenException>();
    }

    [Fact] // Guardar sobre una de fábrica la DUPLICA. Editarla no puede ser posible: no vive en la
           // base y tiene que seguir estando ahí para el siguiente que abra la consola.
    public async Task Guardadas_GuardarSobreUnaDeFabricaCreaUnaPropia()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);
        var deFabrica = OtFactoryQueries.Queries[0];

        var guardada = await repo.SaveAsync(
            OtTenant, Carla, deFabrica.Id,
            new SavedQueryInput("Mi versión", null, deFabrica.Definition),
            cancellationToken: TestContext.Current.CancellationToken);

        guardada!.Id.Should().NotBe(deFabrica.Id);
        guardada.DeFabrica.Should().BeFalse();

        var lista = await repo.ListSavedAsync(
            OtTenant, Carla, cancellationToken: TestContext.Current.CancellationToken);

        lista!.Should().HaveCount(1 + OtFactoryQueries.Queries.Count);
        lista.Should().ContainSingle(q => q.Id == deFabrica.Id && q.DeFabrica);
    }

    [Fact] // Las consultas son de una persona: la de otro usuario no se lista ni se borra.
    public async Task Guardadas_NoSeVenNiSeBorranLasDeOtroUsuario()
    {
        var db = NewDbName();
        var otro = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.Users.Add(new User { Id = otro, Email = "otro@ot.local", DisplayName = "Otro" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        var mia = await repo.SaveAsync(
            OtTenant, Carla, null, new SavedQueryInput("Mía", null, Definir()),
            cancellationToken: TestContext.Current.CancellationToken);

        var deOtro = await repo.ListSavedAsync(
            OtTenant, otro, cancellationToken: TestContext.Current.CancellationToken);

        deOtro!.Should().OnlyContain(q => q.DeFabrica);

        var borrado = await repo.DeleteSavedAsync(
            OtTenant, otro, mia!.Id, cancellationToken: TestContext.Current.CancellationToken);

        borrado.Should().BeFalse();
    }

    // ── Orden y paginación ────────────────────────────────────────────────────────────────────

    [Fact] // Sin desempate estable, dos filas con la misma fecha pueden cambiar de sitio entre
           // páginas — y el export encadena páginas: se perderían filas y se repetirían otras sin
           // que nada avisara.
    public async Task Orden_EsEstableEntrePaginasConFechasIguales()
    {
        var db = NewDbName();
        var mismaFecha = Hace(3);

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            foreach (var n in Enumerable.Range(1, 6))
            {
                Radicar(seed, $"REF-{n}", placa: $"AAA{n:000}", radicadoEn: mismaFecha);
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var primera = await RunAsync(db, Definir(), page: 1, pageSize: 3);
        var segunda = await RunAsync(db, Definir(), page: 2, pageSize: 3);

        var vistos = primera!.Filas.Concat(segunda!.Filas).Select(f => f.ReferenceNumber).ToList();

        vistos.Should().OnlyHaveUniqueItems();
        vistos.Should().HaveCount(6);
    }

    // ── GetSavedByIdAsync (Reportes 2.0, HU-D, tercera ola) ──────────────────────────────────
    //
    // La usa el scheduler de informes programados para re-ejecutar una consulta guardada del
    // organismo sin sesión de usuario. A diferencia del resto del repositorio, NO filtra por
    // userId: por diseño, el informe programado lo puede haber creado un colaborador distinto
    // al que lo va a recibir.

    [Fact]
    public async Task GetSavedByIdAsync_ConsultaPropiaDelOrganismo_LaDevuelve()
    {
        var db = NewDbName();
        var savedId = Guid.NewGuid();
        var otroUsuario = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.OtSavedQueries.Add(new OtSavedQueryEntity
            {
                Id = savedId,
                TransitOfficeId = TransitOffice,
                UserId = otroUsuario,
                Nombre = "Solo traspasos",
                Definicion = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        var result = await repo.GetSavedByIdAsync(
            OtTenant, savedId, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Nombre.Should().Be("Solo traspasos");
        result.DeFabrica.Should().BeFalse();
    }

    [Fact]
    public async Task GetSavedByIdAsync_ConsultaDeFabrica_LaDevuelveSinTocarLaBaseDeDatos()
    {
        var db = NewDbName();
        var fabricaId = OtFactoryQueries.Queries[0].Id;

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        var result = await repo.GetSavedByIdAsync(
            OtTenant, fabricaId, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.DeFabrica.Should().BeTrue();
    }

    [Fact]
    public async Task GetSavedByIdAsync_ConsultaDeOtroOrganismo_DevuelveNull()
    {
        var db = NewDbName();
        var savedId = Guid.NewGuid();
        var transitOfficeAjeno = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.OtSavedQueries.Add(new OtSavedQueryEntity
            {
                Id = savedId,
                TransitOfficeId = transitOfficeAjeno,
                UserId = Carla,
                Nombre = "De otro organismo",
                Definicion = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        var result = await repo.GetSavedByIdAsync(
            OtTenant, savedId, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static QueryCondition Cond(string fieldId, params string[] values) =>
        new(fieldId, QueryOperator.EsAlguno, values);

    private static QueryDefinition Definir(params QueryCondition[] condiciones) =>
        new(
            new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30),
            condiciones,
            []);

    private static async Task<OtQueryResultDto?> RunAsync(
        string db,
        QueryDefinition definition,
        int page = 1,
        int pageSize = 50)
    {
        await using var ctx = NewContext(db);
        var repo = new OtQueryRepository(ctx);

        return await repo.ExecuteAsync(
            OtTenant,
            new QueryRequest(definition, page, pageSize),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Hace <paramref name="dias"/> días, a media mañana de Bogotá. La hora se fija a propósito:
    /// sin eso una prueba lanzada cerca de medianoche cae en el día anterior y falla sola.
    /// </summary>
    private static DateTimeOffset Hace(int dias)
    {
        var hoy = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, OtTenantScope.Bogota).DateTime);
        var dia = hoy.AddDays(-dias);

        return new DateTimeOffset(dia.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-5))
            .ToUniversalTime();
    }

    private static Guid Radicar(
        FlitDbContext ctx,
        string reference,
        string placa,
        DateTimeOffset radicadoEn,
        string status = TramiteEstado.Entregado,
        Guid? tenantId = null,
        bool prioritario = false,
        string? vin = null)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId ?? ClientTenant,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = reference,
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            Plate = placa,
            Vin = vin,
            Prioritario = prioritario,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Carla,
            CreatedAt = radicadoEn,
        });

        Historia(ctx, id, TramiteEstado.Entregado, radicadoEn, tenantId);
        return id;
    }

    private static void Decidir(FlitDbContext ctx, Guid instanceId, string status, DateTimeOffset at) =>
        Historia(ctx, instanceId, status, at, null, Carla);

    private static void Historia(
        FlitDbContext ctx,
        Guid instanceId,
        string toStatus,
        DateTimeOffset at,
        Guid? tenantId = null,
        Guid? changedBy = null) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = null,
            ToStatus = toStatus,
            ChangedAt = at,
            ChangedBy = changedBy,
        });

    private static void Actor(
        FlitDbContext ctx,
        Guid instanceId,
        string actorType,
        string nombre,
        string documento) =>
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = nombre,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void Prenda(
        FlitDbContext ctx,
        Guid instanceId,
        string estado,
        string decision,
        string? acreedor) =>
        ctx.ProcedureInstancePrendas.Add(new ProcedureInstancePrenda
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            Estado = estado,
            Decision = decision,
            AcreedorNombre = acreedor,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void ConLicencia(FlitDbContext ctx, Guid instanceId) =>
        ctx.ProcedureInstanceAttachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            Tipo = "licencia_transito",
            Filename = "lt.pdf",
            Mimetype = "application/pdf",
            Sha256 = "abc",
            StoragePath = "x/lt.pdf",
            UploadedAt = DateTimeOffset.UtcNow,
        });

    private static void Transformacion(FlitDbContext ctx, Guid instanceId, string clave) =>
        ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FieldKey = clave,
            ValueText = "true",
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void SeedScope(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var tenantId in new[] { ClientTenant, OtraEmpresa })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = TransitOffice,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.Users.Add(new User { Id = Carla, Email = "carla@ot.local", DisplayName = "Carla Revisora" });
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
