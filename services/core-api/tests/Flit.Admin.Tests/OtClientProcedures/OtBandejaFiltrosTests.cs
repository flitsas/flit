using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12217 — el organismo filtra su bandeja con la gramática de Consultas.
///
/// <para>Lo que estas pruebas vigilan de verdad no es que un <c>WHERE</c> funcione, sino dos cosas
/// que se rompen en silencio: que el catálogo prometa campos que la bandeja no puede responder, y
/// que la misma pregunta se conteste distinto aquí y en el listado del gestor.</para>
/// </summary>
public sealed class OtBandejaFiltrosTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TipoMatricula = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TipoTraspaso = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── El catálogo ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC2 — los estados ofrecidos son EXACTAMENTE los que la bandeja recibe.
    ///
    /// <para>Esta prueba es el único punto donde se cruzan las dos listas: el catálogo vive en
    /// <c>Flit.Admin.Domain</c>, que no ve el dominio de trámites, así que sus valores están escritos
    /// a mano. Sin esto, añadir un estado a <c>RecibidosPorOrganismo</c> dejaría un filtro que no lo
    /// ofrece, y quitarlo dejaría un filtro que solo puede devolver cero — las dos averías silenciosas.</para>
    /// </summary>
    [Fact]
    public void AC2_LosEstadosDelCatalogo_SonLosQueLaBandejaRecibe()
    {
        var enElCatalogo = OtBandejaQueryFieldCatalog.Find(OtBandejaQueryFieldCatalog.Estado)!
            .Options.Select(o => o.Value);

        enElCatalogo.Should().BeEquivalentTo(TramiteEstado.RecibidosPorOrganismo);
    }

    /// <summary>
    /// AC2 — el organismo no se filtra a sí mismo, y no se le ofrece el vocabulario del informe.
    /// </summary>
    [Fact]
    public void AC2_ElCatalogo_NoOfreceOrganismoNiRevisor()
    {
        var ids = OtBandejaQueryFieldCatalog.Fields.Select(f => f.Id).ToList();

        ids.Should().NotContain("organismo");
        ids.Should().NotContain(OtQueryFieldCatalog.Revisor);
        ids.Should().Contain(OtBandejaQueryFieldCatalog.Empresa);
        ids.Should().Contain(OtBandejaQueryFieldCatalog.SubEstadoPlaca);
    }

    /// <summary>
    /// El vocabulario es el MISMO que el organismo ya usa en Consultas. No se comprueba que las dos
    /// listas sean iguales —no lo son, cada superficie recorta lo suyo— sino que un campo que existe
    /// en las dos se llame igual: si no, el organismo tendría que aprender dos nombres para el mismo
    /// dato según por dónde entre.
    /// </summary>
    [Fact]
    public void ElVocabularioCompartido_SeLlamaIgualEnLasDosPantallasDelOrganismo()
    {
        var enConsultas = OtQueryFieldCatalog.Fields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        var compartidos = OtBandejaQueryFieldCatalog.Fields
            .Select(f => f.Id)
            .Where(enConsultas.Contains)
            .ToList();

        compartidos.Should().Contain(
        [
            OtQueryFieldCatalog.Placa,
            OtQueryFieldCatalog.Vin,
            OtQueryFieldCatalog.Radicado,
            OtQueryFieldCatalog.Comprador,
            OtQueryFieldCatalog.Vendedor,
            OtQueryFieldCatalog.Empresa,
            OtQueryFieldCatalog.TipoTramite,
            OtQueryFieldCatalog.Estado,
            OtQueryFieldCatalog.Prioritario,
            OtQueryFieldCatalog.Prenda,
        ]);
    }

    // ── La validación (AC5) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AC5_UnCampoFueraDelCatalogo_SeRechazaNombrandolo()
    {
        var problema = OtBandejaQueryConditions.Validate(
            [new QueryCondition("revisor", QueryOperator.EsAlguno, ["x"])]);

        problema.Should().NotBeNull();
        problema.Should().Contain("revisor").And.Contain("la bandeja del organismo");
    }

    [Fact]
    public void AC5_UnOperadorQueElCampoNoAdmite_SeRechaza()
    {
        // «Prioritario» es booleano: solo admite «es alguno».
        var problema = OtBandejaQueryConditions.Validate(
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Prioritario, QueryOperator.Contiene, ["true"]),
        ]);

        problema.Should().NotBeNull();
    }

    [Fact]
    public void AC5_UnaOpcionInexistente_SeRechaza()
    {
        var problema = OtBandejaQueryConditions.Validate(
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Estado, QueryOperator.EsAlguno, ["borrador"]),
        ]);

        problema.Should().NotBeNull();
        problema.Should().Contain("borrador");
    }

    [Fact]
    public void AC5_UnaCondicionValida_Pasa()
    {
        var problema = OtBandejaQueryConditions.Validate(
        [
            new QueryCondition(OtBandejaQueryFieldCatalog.Placa, QueryOperator.Contiene, ["ABC"]),
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Estado, QueryOperator.EsAlguno, ["entregado"]),
        ]);

        problema.Should().BeNull();
    }

    // ── La traducción a WHERE (AC3) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_PlacaContiene_AcotaSobreElUniverso()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
            [new QueryCondition(OtBandejaQueryFieldCatalog.Placa, QueryOperator.Contiene, ["ABC"])]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-1"]);
        // El total describe el universo filtrado, no la página: de él vive el pie y el export.
        bandeja.TotalCount.Should().Be(1);
    }

    /// <summary>
    /// AC4 — la misma placa se compara igual aquí que en el listado del gestor: mayúsculas y sin
    /// guiones, puntos ni espacios. Se prueba por el resultado y no por la función para que valga
    /// también si mañana cambia la forma de escribir el <c>WHERE</c>.
    /// </summary>
    [Theory]
    [InlineData("ABC123")]
    [InlineData("abc-123")]
    [InlineData(" ABC.123 ")]
    public async Task AC4_LaPlacaSeNormalizaIgualQueEnElListadoDelGestor(string escrita)
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
            [new QueryCondition(OtBandejaQueryFieldCatalog.Placa, QueryOperator.EsAlguno, [escrita])]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-1"]);
    }

    [Fact]
    public async Task AC3_EmpresaCliente_AcotaAUnaEmpresa()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Empresa, QueryOperator.EsAlguno, [OtraEmpresa.ToString()]),
        ]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-3"]);
    }

    [Fact]
    public async Task AC3_TipoDeTramite_SeFiltraPorIdentificador()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.TipoTramite,
                QueryOperator.EsAlguno,
                [TipoTraspaso.ToString()]),
        ]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-2"]);
    }

    [Fact]
    public async Task AC3_Estado_UsaElVocabularioDeLaBandeja()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Estado, QueryOperator.EsAlguno, [TramiteEstado.Aprobado]),
        ]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-3"]);
    }

    /// <summary>
    /// «Sin ruta de placa» es la AUSENCIA de valor, no un valor: se pide como una opción más y tiene
    /// que poder combinarse con las reales.
    /// </summary>
    [Fact]
    public async Task AC3_RutaDePlaca_SinRutaSeCombinaConLosValoresReales()
    {
        var db = await SembrarEscenarioAsync();

        var soloSinRuta = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.SubEstadoPlaca, QueryOperator.EsAlguno, ["sin_ruta"]),
        ]);
        soloSinRuta.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-1", "REF-3"]);

        var mezcla = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.SubEstadoPlaca,
                QueryOperator.EsAlguno,
                ["sin_ruta", PlateFlowStatus.Preasignado]),
        ]);
        mezcla.Data.Select(p => p.ReferenceNumber)
            .Should().BeEquivalentTo(["REF-1", "REF-2", "REF-3"]);
    }

    /// <summary>
    /// «No es ninguno» tiene que ser el complemento EXACTO de «es alguno»: escribir las dos ramas por
    /// separado es justo la forma en que dejan de serlo sin que nadie lo note.
    /// </summary>
    [Fact]
    public async Task AC3_RutaDePlaca_LaNegacionEsElComplementoExacto()
    {
        var db = await SembrarEscenarioAsync();

        var son = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.SubEstadoPlaca,
                QueryOperator.EsAlguno,
                [PlateFlowStatus.Preasignado]),
        ]);
        var noSon = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.SubEstadoPlaca,
                QueryOperator.NoEsNinguno,
                [PlateFlowStatus.Preasignado]),
        ]);

        var todos = await BuscarAsync(db, []);
        (son.TotalCount + noSon.TotalCount).Should().Be(todos.TotalCount);
        son.Data.Select(p => p.Id).Should().NotIntersectWith(noSon.Data.Select(p => p.Id));
    }

    [Fact]
    public async Task AC3_Prioritario_AcotaALosMarcados()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Prioritario, QueryOperator.EsAlguno, ["true"]),
        ]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-2"]);
    }

    [Fact]
    public async Task AC3_VariasCondiciones_SeCombinanEntreSi()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
        [
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Estado, QueryOperator.EsAlguno, [TramiteEstado.Entregado]),
            new QueryCondition(
                OtBandejaQueryFieldCatalog.Prioritario, QueryOperator.EsAlguno, ["true"]),
        ]);

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-2"]);
    }

    /// <summary>Filtrar por «nada» no debe vaciar la bandeja: una condición sin valores no acota.</summary>
    [Fact]
    public async Task UnaCondicionSinValores_NoVaciaLaBandeja()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await BuscarAsync(db,
            [new QueryCondition(OtBandejaQueryFieldCatalog.Placa, QueryOperator.EsAlguno, ["  "])]);

        bandeja.TotalCount.Should().Be(3);
    }

    // ── El rango de fechas (AC6) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_ElRangoDeRadicacion_AcotaLaPaginaYElTotal()
    {
        var db = await SembrarEscenarioAsync();

        var bandeja = await ListarAsync(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            CreatedFrom = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedTo = new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero),
        });

        bandeja.Data.Select(p => p.ReferenceNumber).Should().BeEquivalentTo(["REF-2"]);
        bandeja.TotalCount.Should().Be(1);
    }

    // ── El ordenamiento (AC7) ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ordenar por empresa no existía: la columna «Empresa / Gestor» solo sabía ordenar por el
    /// gestor y la cabecera prometía algo que no hacía.
    /// </summary>
    [Fact]
    public async Task AC7_OrdenPorEmpresa_UsaLaRazonSocial()
    {
        var db = await SembrarEscenarioAsync();

        var ascendente = await ListarAsync(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            SortBy = "empresa",
            SortDir = "asc",
        });

        // «Flota Andina» antes que «Transportes Zulia»; dentro de la empresa, el orden es estable.
        ascendente.Data.Select(p => p.ClientTenantId).Distinct()
            .Should().ContainInOrder(ClientTenant, OtraEmpresa);
    }

    [Fact]
    public async Task AC7_OrdenPorTipoDeTramite_NoRompeLaPrimaciaDeLosPrioritarios()
    {
        var db = await SembrarEscenarioAsync();

        var ordenada = await ListarAsync(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            SortBy = "tipo_tramite",
            SortDir = "asc",
        });

        // REF-2 es el único prioritario: pase lo que pase con el orden pedido, va primero.
        ordenada.Data[0].ReferenceNumber.Should().Be("REF-2");
    }

    // ── El catálogo servido (AC1) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// AC1 — el catálogo llega con las opciones del organismo ya resueltas: las empresas que le
    /// entregan de verdad y los tipos que de verdad ha recibido.
    /// </summary>
    [Fact]
    public async Task AC1_ElCatalogoServido_TraeLasOpcionesDelOrganismo()
    {
        var db = await SembrarEscenarioAsync();

        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var campos = await new GetOtBandejaFilterFieldsHandler(repo)
            .HandleAsync(OtTenant, null, TestContext.Current.CancellationToken);

        campos.Should().NotBeNull();

        var empresas = campos!.Single(f => f.Id == OtBandejaQueryFieldCatalog.Empresa).Options;
        empresas.Select(o => o.Value)
            .Should().BeEquivalentTo([ClientTenant.ToString(), OtraEmpresa.ToString()]);

        var tipos = campos!.Single(f => f.Id == OtBandejaQueryFieldCatalog.TipoTramite).Options;
        tipos.Select(o => o.Value).Should().Contain(
            [TipoMatricula.ToString(), TipoTraspaso.ToString()]);
    }

    // ── Andamiaje ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tres trámites que se distinguen por TODO lo que se filtra, para que cada prueba pueda pedir
    /// uno y comprobar que los otros dos se quedan fuera.
    ///
    /// <list type="bullet">
    /// <item><description>REF-1 — Flota Andina, matrícula, entregado, placa ABC123, sin ruta.</description></item>
    /// <item><description>REF-2 — Flota Andina, traspaso, entregado, PRIORITARIO, preasignado, marzo.</description></item>
    /// <item><description>REF-3 — Transportes Zulia, matrícula, aprobado, sin ruta.</description></item>
    /// </list>
    /// </summary>
    private static async Task<string> SembrarEscenarioAsync()
    {
        var db = Guid.NewGuid().ToString();
        await using var ctx = NewContext(db);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var (tenantId, razonSocial) in
                 new[] { (ClientTenant, "Flota Andina S.A.S."), (OtraEmpresa, "Transportes Zulia") })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = TransitOffice,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            ctx.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Code = tenantId.ToString()[..8],
                LegalName = razonSocial,
                TaxId = "900000000",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.ProcedureTypes.AddRange(
            new ProcedureType
            {
                Id = TipoMatricula,
                Code = "matricula_inicial",
                Name = "Matrícula inicial",
                Family = "MATRICULAS",
                IsActive = true,
                PublicationStatus = PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ProcedureType
            {
                Id = TipoTraspaso,
                Code = "traspaso",
                Name = "Traspaso",
                Family = "TRASPASO",
                IsActive = true,
                PublicationStatus = PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        ctx.ProcedureInstances.AddRange(
            new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureTypeId = TipoMatricula,
                ReferenceNumber = "REF-1",
                Status = TramiteEstado.Entregado,
                TransitOfficeId = TransitOffice,
                Plate = "ABC123",
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
            },
            new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureTypeId = TipoTraspaso,
                ReferenceNumber = "REF-2",
                Status = TramiteEstado.Entregado,
                TransitOfficeId = TransitOffice,
                PlateFlowStatus = PlateFlowStatus.Preasignado,
                Prioritario = true,
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero),
            },
            new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = OtraEmpresa,
                ProcedureTypeId = TipoMatricula,
                ReferenceNumber = "REF-3",
                Status = TramiteEstado.Aprobado,
                TransitOfficeId = TransitOffice,
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            });

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static Task<ListOtClientProceduresResult> BuscarAsync(
        string db, IReadOnlyList<QueryCondition> condiciones) =>
        ListarAsync(db, new ListOtClientProceduresQuery
        {
            OtTenantId = OtTenant,
            Condiciones = condiciones,
        });

    /// <summary>
    /// La consulta va SIN <c>Status</c> a propósito: la bandeja real abre por la cola de decisión,
    /// pero aquí se miden las condiciones y un estado por defecto escondería medio escenario.
    /// </summary>
    private static async Task<ListOtClientProceduresResult> ListarAsync(
        string db, ListOtClientProceduresQuery query)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        return await new ListOtClientProceduresHandler(repo)
            .HandleAsync(query, TestContext.Current.CancellationToken);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
