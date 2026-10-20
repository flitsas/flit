using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Contraventor;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13478 — asignar el conductor y sus documentos.</summary>
public sealed class ContraventorTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorConsultar, Permisos.ContraventorDocumentos];

    private static object Conductor(string doc, bool declaracion = true, string? motivo = null) => new
    {
        tipoDocumento = "CC", numeroDocumento = doc, nombres = $"Conductor {doc}", correo = $"c{doc}@example.test", telefono = "3000000000",
        direccion = "Calle 1", ciudad = "Bogotá", sucursal = (string?)null, tipoContrato = "Renting", motivo, declaracionTratamientoDatos = declaracion,
    };

    [Fact]
    public async Task AC1_Manual_ExigeDeclaracion_YAC2_ReemplazoExigeMotivoYDejaHistorial()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-1")))["K1"];
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));

        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("111", declaracion: false), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("111"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("222"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("222", motivo: "Contrato cedido"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        detalle.GetProperty("contraventor").GetProperty("numeroDocumento").GetString().Should().Be("222");
        detalle.GetProperty("gestionado").GetBoolean().Should().BeTrue();
        var historial = detalle.GetProperty("historialContraventor");
        historial.GetArrayLength().Should().Be(2);
        historial[0].GetProperty("motivo").GetString().Should().Be("Contrato cedido");
        historial[0].GetProperty("actorNombre").GetString().Should().NotBeNullOrWhiteSpace("la traza dice quién reemplazó al conductor");
        historial[0].GetProperty("datosAnteriores").GetString().Should().Contain("Conductor 111", "y quién era antes");
    }

    [Fact]
    public async Task ConsultaPorApi_EncuentraYAsigna_ONoEncuentra()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var ids = await IngresarAsync(api, empresa, Item("K-2", placa: "XYZ124"), Item("K-3", placa: "XYZ123"));
        ids.Keys.Should().HaveCount(2);
        await ConsultaPorApiDeLosNitsAsync(api, empresa);
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K2"], ct))).EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Pendiente);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));

        var par = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K2"]}/contraventor/consultar", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        par.GetProperty("resultado").GetString().Should().Be("Encontrado");
        var impar = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K3"]}/contraventor/consultar", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        impar.GetProperty("resultado").GetString().Should().Be("NoEncontrado");

        var k2 = await api.EnDbAsync(db => db.Comparendos.Include(c => c.Contraventor).SingleAsync(c => c.Id == ids["K2"], ct));
        k2.Contraventor!.Origen.Should().Be(OrigenContraventor.Api);
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?rapido=SinContraventor", ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numero").GetString()).Should().Equal("K-3");
    }

    [Fact]
    public async Task ConsultaPorApi_ConError_GuardaSoloElError()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var ids = await IngresarAsync(api, empresa, Item("K-5", placa: "XYZ120"), Item("K-6", placa: "XYZ125"));
        await ConsultaPorApiDeLosNitsAsync(api, empresa);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K5"]}/contraventor/consultar", null, ct);
        await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K6"]}/contraventor/consultar", null, ct);

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{ids["K5"]}", ct);
        detalle.GetProperty("estadoConsultaContraventor").GetString().Should().Be("Error");
        detalle.GetProperty("errorConsultaContraventor").GetString().Should().Contain("no disponible", "queda qué respondió la API para diagnosticar");
        (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{ids["K6"]}", ct)).GetProperty("errorConsultaContraventor").ValueKind
            .Should().Be(JsonValueKind.Null, "de una consulta que respondió bien no se guarda nada");
    }

    /// <summary>
    /// HU #13712: la API de Renting solo conoce su flota. Un NIT sin la casilla (Bancolombia administrado por Renting) se
    /// asigna a mano: ni la ingesta ni el reintento lo consultan, y la consulta manual lo rechaza.
    /// </summary>
    [Fact]
    public async Task HU13712_NitSinMarcar_SeAsignaAMano_YNoSeConsulta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var id = (await IngresarAsync(api, empresa, Item("K-8", placa: "BAN124")))["K8"];
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct))).EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.NoAplica);

        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var r = await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/contraventor/consultar", null, ct);
        r.IsSuccessStatusCode.Should().BeFalse();
        (await r.Content.ReadAsStringAsync(ct)).Should().Contain("NIT_SIN_API");
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?rapido=SinContraventor", ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numero").GetString()).Should().Equal(["K-8"], "va a la cola de asignación manual");

        await using (var scope = api.App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<ReintentoContraventor>().Single().EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ReintentoContraventor, Disparo.Manual, empresa), ct);
        var k8 = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == id, ct));
        (k8.EstadoConsultaContraventor, k8.IntentosConsultaContraventor).Should().Be((EstadoConsultaContraventor.NoAplica, 0));
    }

    /// <summary>
    /// HU #13712: dos interruptores del SuperAdmin. El general de la empresa manda (Mobilize: Manual y nada sale); apagarlo
    /// no borra la casilla del NIT, y al encenderlo de nuevo vuelve a quedar pendiente lo que corresponde.
    /// </summary>
    [Fact]
    public async Task HU13712_InterruptorGeneral_YCasillaDelNit_SoloSuperAdmin()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var id = (await IngresarAsync(api, empresa, Item("K-9", placa: "REN124")))["K9"];
        var nitId = await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(n => n.TenantId == empresa).Select(n => n.Id).SingleAsync(ct));
        Task<EstadoConsultaContraventor> Estado() => api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == id).Select(c => c.EstadoConsultaContraventor).SingleAsync(ct));

        var gestora = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoVer, Permisos.MonitoreoGestionar, Permisos.ConfiguracionVer]));
        (await gestora.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/nits/{nitId}/consulta-contrato", new { activo = true }, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "la casilla es del SuperAdmin");

        await ConsultaPorApiDeLosNitsAsync(api, empresa);
        (await Estado()).Should().Be(EstadoConsultaContraventor.Pendiente);
        (await gestora.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/monitoreo/nits", ct))[0].GetProperty("consultaContratoApi").GetBoolean().Should().BeTrue();

        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        object Capacidades(string integracion) => new
        {
            hasRightPetitionModule = false, allowsContravenerConsultation = true, allowsContravenerChange = false, hasOcrAccess = false, hasReportesModule = false,
            tipoIntegracionConductor = integracion, conjuntoPlantillas = "Flit", requiereRepresentanteLegal = true,
        };
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades("Manual"), ct)).EnsureSuccessStatusCode();
        (await Estado()).Should().Be(EstadoConsultaContraventor.NoAplica, "con el general en Manual no sale ninguna consulta");
        (await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.SingleAsync(n => n.Id == nitId, ct))).ConsultaContratoApi.Should().BeTrue("la casilla se conserva");

        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", Capacidades("ApiRenting"), ct)).EnsureSuccessStatusCode();
        (await Estado()).Should().Be(EstadoConsultaContraventor.Pendiente);

        await ConsultaPorApiDeLosNitsAsync(api, empresa, activo: false);
        (await Estado()).Should().Be(EstadoConsultaContraventor.NoAplica);
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.Where(a => a.TenantId == empresa && a.Entidad == "identificacion_monitoreada").Select(a => a.Cambios).ToListAsync(ct)))
            .Count(c => c.Contains("consulta_contrato_api", StringComparison.Ordinal)).Should().Be(2, "marcar y desmarcar quedan en el historial");
    }

    [Theory]
    [InlineData("811011779", true)]
    [InlineData("8110117798", true)]
    [InlineData("811011779-8", true)]
    [InlineData("890903938", false)]
    [InlineData(null, false)]
    public void HU13712_PorPlaca_ElPropietarioDecide(string? propietario, bool aplica)
    {
        var renting = new Flit.Comparendos.Api.Persistence.Modelo.IdentificacionMonitoreada { Id = Guid.NewGuid(), NumeroBase = "811011779", ConsultaContratoApi = true };
        var c = new Flit.Comparendos.Api.Persistence.Modelo.Comparendo { NumeroDocumentoInfractor = propietario };
        ConsultaPorApi.DeNitMarcado(c, [renting]).Should().Be(aplica);
        ConsultaPorApi.DeNitMarcado(new Flit.Comparendos.Api.Persistence.Modelo.Comparendo { IdentificacionMonitoreadaId = Guid.NewGuid(), NumeroDocumentoInfractor = propietario }, [renting])
            .Should().BeFalse("si llegó por un NIT, decide ese NIT y no el documento");
    }

    [Fact]
    public async Task SinCapacidadDeConsulta_Responde403()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-4")))["K4"];
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsync($"/api/v1/comparendos/comparendos/{id}/contraventor/consultar", null, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReintentoDiario_ConsultaLosPendientes()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var ids = await IngresarAsync(api, empresa, Item("K-5", placa: "QWE128"), Item("K-6", placa: "QWE120"));
        await ConsultaPorApiDeLosNitsAsync(api, empresa);

        await using (var scope = api.App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<ReintentoContraventor>().Single().EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ReintentoContraventor, Disparo.Manual, empresa), ct);

        var k5 = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K5"], ct));
        var k6 = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K6"], ct));
        k5.EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Encontrado);
        k6.EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Error);
        k6.IntentosConsultaContraventor.Should().Be(1);
    }

    [Fact]
    public async Task Documentos_LaNuevaCedulaReemplazaALaVigente()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-7")))["K7"];
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        for (var i = 0; i < 2; i++)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", $"cedula{i}.pdf" } };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo=Cedula", form, ct)).EnsureSuccessStatusCode();
        }

        var docs = (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct)).GetProperty("documentos");
        docs.GetArrayLength().Should().Be(2);
        docs.EnumerateArray().Count(d => d.GetProperty("vigente").GetBoolean()).Should().Be(1);
    }
}
