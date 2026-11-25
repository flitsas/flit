using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.CompanyQueries;

/// <summary>
/// Épica #14666 / HU #14699 (A2) — «Firmado por todas las partes» y «Falta firmar» en las consultas del gestor.
/// La regla es la de <see cref="ITramiteFirmaEvaluator"/> (en memoria, ADR-0092); aquí se prueba que el
/// repositorio la aplique igual en listado, contador y columna, sin tocar las consultas anteriores.
/// </summary>
public sealed class CompanyQueryFirmaTests
{
    private static readonly Guid Empresa = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TipoMatriculaId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid Gustavo = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Evaluador de prueba: firma = documento del actor en <c>firmados</c>; rechazada si está en <c>rechazados</c>.</summary>
    private sealed class FakeEvaluator(HashSet<string> firmados, HashSet<string>? rechazados = null) : ITramiteFirmaEvaluator
    {
        public int Cargas { get; private set; }

        public Task<TramiteFirmaContexto> CargarContextoAsync(
            IReadOnlyCollection<ProcedureInstance> tramites, DateTimeOffset now, CancellationToken ct = default)
        {
            Cargas++;
            return Task.FromResult(TramiteFirmaContexto.Vacio);
        }

        public TramiteFirmaResultado Evaluar(ProcedureInstance tramite, TramiteFirmaContexto contexto, DateTimeOffset now)
        {
            var pendientes = tramite.Actors
                .Where(a => !firmados.Contains(a.DocumentNumber))
                .OrderBy(a => a.ActorType == "vendedor" ? 0 : 1).ThenBy(a => a.Ordinal)
                .Select(a => new ParteFirmaPendiente(
                    a.ActorType == "vendedor" ? "Vendedor" : "Comprador",
                    a.FullName ?? string.Empty,
                    rechazados?.Contains(a.DocumentNumber) == true))
                .ToList();
            return new TramiteFirmaResultado(pendientes.Count == 0, pendientes);
        }
    }

    [Fact] // Escenario: el catálogo publica los dos campos nuevos.
    public void ElCatalogoPublicaLosCamposNuevosEnPersonas()
    {
        var firmado = CompanyQueryFieldCatalog.Fields.Single(f => f.Id == CompanyQueryFieldCatalog.FirmadoPorTodas);
        firmado.Group.Should().Be(CompanyQueryFieldCatalog.GrupoPersonas);
        firmado.Label.Should().Be("Firmado por todas las partes");
        firmado.Operators.Should().Equal(QueryOperator.EsAlguno);
        firmado.Options.Select(o => o.Label).Should().Equal("Sí", "No");
        firmado.Hint.Should().Be("Sí: todas las partes del trámite ya firmaron. No: falta firmar al menos una.");

        var falta = CompanyQueryFieldCatalog.Fields.Single(f => f.Id == CompanyQueryFieldCatalog.FaltaFirmar);
        falta.Group.Should().Be(CompanyQueryFieldCatalog.GrupoPersonas);
        falta.Operators.Should().BeEmpty("es solo columna");
        falta.Hint.Should().Be("Nombre y rol de quien aún no ha firmado. Vacío si ya firmaron todos.");
    }

    [Fact] // «Falta firmar» no admite filtro: una condición sobre él se descarta al normalizar.
    public void FaltaFirmarNoFiltra()
    {
        var definicion = CompanyQueryFieldCatalog.Normalize(Definir(Cond(CompanyQueryFieldCatalog.FaltaFirmar, "x")));
        definicion.Condiciones.Should().BeEmpty();
    }

    [Fact] // Es Sí / es No con contador exacto + paridad entre filtro y columna.
    public async Task EsSi_EsNo_ContadorExacto_YParidadConLaColumna()
    {
        var db = NewDbName();
        await Sembrar(db);
        var evaluador = new FakeEvaluator(["100", "300", "301"], ["201"]);

        var si = await RunAsync(db, evaluador, Definir(Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "true")));
        si.Total.Should().Be(1);
        si.Filas.Select(f => f.ReferenceNumber).Should().Equal("REF-FIRMADO");
        si.Filas[0].FirmadoPorTodas.Should().BeTrue();
        si.Filas[0].FaltaFirmar.Should().BeEmpty();

        var no = await RunAsync(db, evaluador, Definir(Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "false")));
        no.Total.Should().Be(2);
        no.Filas.Should().OnlyContain(f => f.FirmadoPorTodas == false);

        // Parte rechazada cuenta como No y se marca; el separador es « · ».
        var parcial = no.Filas.Single(f => f.ReferenceNumber == "REF-PARCIAL");
        parcial.FaltaFirmar.Should().Be("Vendedor: Vera Vendedora (rechazada) · Comprador: Carlos Comprador");
        no.Filas.Single(f => f.ReferenceNumber == "REF-NADIE").FaltaFirmar.Should().Be("Comprador: Nora Nadie");

        // Sí + No = universo (misma regla en el filtro y en la columna).
        (si.Total + no.Total).Should().Be(3);
    }

    [Fact] // El contador de la cabecera sigue siendo del universo, no de la página.
    public async Task ElContadorEsDelUniversoYNoDeLaPagina()
    {
        var db = NewDbName();
        await Sembrar(db);

        var pagina = await RunAsync(db, new FakeEvaluator([]),
            Definir(Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "false")), pageSize: 1);

        pagina.Total.Should().Be(3);
        pagina.Filas.Should().HaveCount(1);
    }

    [Fact] // Sin filtro, la columna se calcula solo si se pidió; consultas anteriores no pagan el cálculo.
    public async Task SinFiltroNiColumna_NoSeCalculaLaFirma_YElResultadoEsElDeSiempre()
    {
        var db = NewDbName();
        await Sembrar(db);
        var evaluador = new FakeEvaluator([]);

        var resultado = await RunAsync(db, evaluador, Definir());

        evaluador.Cargas.Should().Be(0);
        resultado.Total.Should().Be(3);
        resultado.Filas.Should().OnlyContain(f => f.FirmadoPorTodas == null && f.FaltaFirmar == null);
    }

    [Fact] // Pedir la columna «Falta firmar» la llena para la página, sin filtrar.
    public async Task LaColumnaFaltaFirmarSeLlenaSinFiltro()
    {
        var db = NewDbName();
        await Sembrar(db);
        var definicion = new QueryDefinition(
            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos30),
            [],
            [CompanyQueryFieldCatalog.FaltaFirmar]);

        var resultado = await RunAsync(db, new FakeEvaluator(["100"]), definicion);

        resultado.Total.Should().Be(3);
        resultado.Filas.Single(f => f.ReferenceNumber == "REF-FIRMADO").FaltaFirmar.Should().BeEmpty();
        resultado.Filas.Single(f => f.ReferenceNumber == "REF-NADIE").FaltaFirmar.Should().Be("Comprador: Nora Nadie");
    }

    [Fact] // Una consulta guardada anterior (sin los campos nuevos) da lo mismo que antes.
    public async Task UnaConsultaAnteriorSigueIgual()
    {
        var db = NewDbName();
        await Sembrar(db);

        var resultado = await RunAsync(db, new FakeEvaluator([]),
            Definir(Cond(CompanyQueryFieldCatalog.Placa, "AAA111")));

        resultado.Total.Should().Be(1);
        resultado.Filas.Single().ReferenceNumber.Should().Be("REF-FIRMADO");
    }

    [Fact] // Aislamiento: el trámite de otra compañía no entra ni en Sí ni en No.
    public async Task AislamientoPorCompania()
    {
        var db = NewDbName();
        await Sembrar(db);

        var evaluador = new FakeEvaluator(["100", "999"]);
        var si = await RunAsync(db, evaluador, Definir(Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "true")));

        si.Filas.Should().NotContain(f => f.ReferenceNumber == "REF-AJENO");
        si.Total.Should().Be(1);
    }

    [Fact] // Matrícula inicial con titular firmado (evaluador real, regla única) aparece en «es Sí».
    public async Task MatriculaInicialConTitularFirmado_AparecaEnEsSi_ConElEvaluadorReal()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            var id = Tramite(seed, "REF-MAT", "MMM111", Empresa);
            Actor(seed, id, "comprador", "Tito Titular", "500", Empresa);
            seed.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = Empresa,
                ProcedureInstanceId = id,
                PartyRole = "comprador",
                DocumentType = "CC",
                DocumentNumber = "500",
                Name = "Tito Titular",
                Email = "tito@flit.test",
                Status = BiometricEstados.Aprobado,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);
        var si = await repo.ExecuteAsync(
            Empresa,
            new QueryRequest(Definir(Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "true")), 1, 50),
            TestContext.Current.CancellationToken);

        si.Total.Should().Be(1);
        si.Filas[0].FaltaFirmar.Should().BeEmpty();
    }

    [Fact] // HU #14700 — Super Admin: «es No» sobre varias compañías hereda filtro y columnas, sin salirse de la consulta.
    public async Task SuperAdmin_FiltraEsNoSobreVariasCompanias_YHeredaLasColumnas()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            foreach (var (id, code, nit) in new[] { (Empresa, "TESLA", "900000001"), (OtraEmpresa, "RENTING", "900000002") })
            {
                seed.Tenants.Add(new Flit.Infrastructure.Persistence.Entities.Identity.Tenant
                {
                    Id = id, Code = code, LegalName = code, TaxId = nit, TenantType = "FLIT", IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            var a = Tramite(seed, "REF-A-FIRMADO", "AAA111", Empresa);
            Actor(seed, a, "comprador", "Carla Compradora", "100", Empresa);
            var b = Tramite(seed, "REF-A-FALTA", "BBB222", Empresa);
            Actor(seed, b, "comprador", "Nora Nadie", "203", Empresa);
            var c = Tramite(seed, "REF-B-FALTA", "CCC333", OtraEmpresa);
            Actor(seed, c, "vendedor", "Vera Vendedora", "201", OtraEmpresa);
            var d = Tramite(seed, "REF-B-FIRMADO", "DDD444", OtraEmpresa);
            Actor(seed, d, "comprador", "Ana Ajena", "999", OtraEmpresa);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx, new FakeEvaluator(["100", "999"]));
        var definicion = new QueryDefinition(
            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos30),
            [Cond(CompanyQueryFieldCatalog.FirmadoPorTodas, "false")],
            [CompanyQueryFieldCatalog.FaltaFirmar]);

        var no = await repo.ExecuteForSuperAdminAsync(
            new QueryRequest(definicion, 1, 50), TestContext.Current.CancellationToken);

        no.Total.Should().Be(2);
        no.Filas.Select(f => f.ReferenceNumber).Should().BeEquivalentTo("REF-A-FALTA", "REF-B-FALTA");
        no.Filas.Select(f => f.CompaniaId).Should().BeEquivalentTo([Empresa, OtraEmpresa]);
        no.Filas.Single(f => f.ReferenceNumber == "REF-B-FALTA").FaltaFirmar.Should().Be("Vendedor: Vera Vendedora");
        no.Filas.Should().OnlyContain(f => f.FirmadoPorTodas == null || f.FirmadoPorTodas == false);

        // El catálogo de Super Admin también publica los campos (misma definición que el del gestor).
        var campos = await repo.GetFieldsForSuperAdminAsync(TestContext.Current.CancellationToken);
        campos.Select(f => f.Id).Should().Contain([CompanyQueryFieldCatalog.FirmadoPorTodas, CompanyQueryFieldCatalog.FaltaFirmar]);
    }

    [Fact] // La exportación (hoja de Excel) lleva las dos columnas.
    public void LaExportacionIncluyeLasColumnas()
    {
        var fila = new CompanyQueryRowDto(
            Guid.NewGuid(), "REF-1", null, null, null, null, Empresa, "Empresa", TipoMatriculaId, "Matrícula",
            "preparado", false, false, 0, null, null, false, null, false, [], false, null, "", "Gustavo",
            DateTimeOffset.UtcNow, null, null, null, null, null, null, 0, [],
            FirmadoPorTodas: false, FaltaFirmar: "Comprador: Nora Nadie");

        var hoja = CompanyQueryReportColumns.BuildSheet(
            "Consulta", [CompanyQueryFieldCatalog.FirmadoPorTodas, CompanyQueryFieldCatalog.FaltaFirmar], [fila]);

        hoja.Columns.Select(c => c.Header).Should().Equal("Firmado por todas las partes", "Falta firmar");
        hoja.Rows.Should().HaveCount(1);
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static async Task Sembrar(string db)
    {
        await using var seed = NewContext(db);
        SeedCatalogos(seed);

        var firmado = Tramite(seed, "REF-FIRMADO", "AAA111", Empresa);
        Actor(seed, firmado, "comprador", "Carla Compradora", "100", Empresa);

        var parcial = Tramite(seed, "REF-PARCIAL", "BBB222", Empresa);
        Actor(seed, parcial, "vendedor", "Vera Vendedora", "201", Empresa);
        Actor(seed, parcial, "comprador", "Carlos Comprador", "202", Empresa);

        var nadie = Tramite(seed, "REF-NADIE", "CCC333", Empresa);
        Actor(seed, nadie, "comprador", "Nora Nadie", "203", Empresa);

        var ajeno = Tramite(seed, "REF-AJENO", "DDD444", OtraEmpresa);
        Actor(seed, ajeno, "comprador", "Ana Ajena", "999", OtraEmpresa);

        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Guid Tramite(FlitDbContext ctx, string reference, string placa, Guid tenant)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = TipoMatriculaId,
            ReferenceNumber = reference,
            Status = TramiteEstado.Entregado,
            Plate = placa,
            CreatedByUserId = Gustavo,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        });
        return id;
    }

    private static void Actor(FlitDbContext ctx, Guid instanceId, string rol, string nombre, string doc, Guid tenant) =>
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = nombre,
            PersonType = "natural",
            Ordinal = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void SeedCatalogos(FlitDbContext ctx)
    {
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoMatriculaId,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
        });
        ctx.Users.Add(new User { Id = Gustavo, Email = "gustavo@gestora.local", DisplayName = "Gustavo Gestor" });
    }

    private static QueryCondition Cond(string fieldId, params string[] values) =>
        new(fieldId, QueryOperator.EsAlguno, values);

    private static QueryDefinition Definir(params QueryCondition[] condiciones) =>
        new(new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos30), condiciones, []);

    private static async Task<CompanyQueryResultDto> RunAsync(
        string db, ITramiteFirmaEvaluator evaluador, QueryDefinition definition, int pageSize = 50)
    {
        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx, evaluador);
        return await repo.ExecuteAsync(
            Empresa, new QueryRequest(definition, 1, pageSize), TestContext.Current.CancellationToken);
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
