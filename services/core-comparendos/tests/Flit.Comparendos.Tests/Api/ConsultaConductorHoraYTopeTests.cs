using System.Net;
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
using Comparendo = Flit.Comparendos.Api.Persistence.Modelo.Comparendo;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC puntos 19, «sin hora» y 20: la hora de la infracción viaja a la API de contratos sin convertir; sin hora se
/// consulta a las 00:00 y a las 23:59; y la consulta tiene un tope de 3 intentos, con uno solo por día en el trabajo.
/// La API de contratos es la de prueba (<see cref="ApiConFuente.ContratosPorHora"/>, si no la simulada por placa:
/// terminada en 0 → error, impar → no encontrado, par → conductor).
/// </summary>
public sealed class ConsultaConductorHoraYTopeTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorConsultar, Permisos.ContraventorDocumentos];

    private static RespuestaContrato Conductor(string nombres, string correo) =>
        new(ResultadoContrato.Encontrado, new ConductorEncontrado(null, null, nombres, correo, null, null, null, null, null), null, null);

    private static readonly RespuestaContrato Vacia = new(ResultadoContrato.NoEncontrado, null, "Sin contrato.", null);

    private Task<Comparendo> Leer(Guid id) =>
        api.EnDbAsync(db => db.Comparendos.Include(c => c.Contraventor).SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken));

    /// <summary>Empresa con la API de Renting y un comparendo pendiente de consulta, con la hora (y la fecha) que diga la prueba.</summary>
    private async Task<(Guid Empresa, Guid Id, HttpClient Cliente)> PendienteAsync(string numero, string placa, TimeOnly? hora, bool sinFechaImposicion = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var id = (await IngresarAsync(api, empresa, Item(numero, placa: placa)))[numero.Replace("-", string.Empty, StringComparison.Ordinal)];
        await ConsultaPorApiDeLosNitsAsync(api, empresa);
        await api.EnDbAsync(async db =>
        {
            var c = await db.Comparendos.SingleAsync(x => x.Id == id, ct);
            c.HoraImposicion = hora;
            if (sinFechaImposicion)
                c.FechaImposicion = null;
            await db.SaveChangesAsync(ct);
        });
        return (empresa, id, api.Cliente(new UsuarioDePrueba(empresa, Operador)));
    }

    private (string Placa, DateOnly Fecha, TimeOnly? Hora)[] Consultas(string placa) => [.. api.ConsultasContratos.Where(x => x.Placa == placa)];

    private static Task<HttpResponseMessage> ConsultarAsync(HttpClient cliente, Guid id) =>
        cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/contraventor/consultar", null, TestContext.Current.CancellationToken);

    private async Task JobAsync(Guid empresa)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<ReintentoContraventor>().Single()
            .EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ReintentoContraventor, Disparo.Manual, empresa), TestContext.Current.CancellationToken);
    }

    // ---------- Punto 19: la hora real ----------

    [Fact]
    public async Task P19_ConHora_SeEnviaLaHoraDelComparendo_EnMinutos_YConLaFechaDeImposicion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, id, cliente) = await PendienteAsync("H-1", "HRA122", new TimeOnly(6, 17, 42));
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();

        Consultas("HRA122").Should().ContainSingle().Which.Should().Be(("HRA122", new DateOnly(2026, 9, 28), (TimeOnly?)new TimeOnly(6, 17)),
            "la hora va tal cual, sin segundos y sin convertir a otra zona");
    }

    [Fact]
    public async Task P19_FechaDeNotificacion_EsSinHora_AunqueHayaHora()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (_, id, cliente) = await PendienteAsync("H-2", "HRA124", new TimeOnly(6, 17), sinFechaImposicion: true);
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();

        Consultas("HRA124").Select(x => (x.Fecha, x.Hora)).Should().Equal(
            [(new DateOnly(2026, 10, 1), new TimeOnly(0, 0)), (new DateOnly(2026, 10, 1), new TimeOnly(23, 59))],
            "la hora solo acompaña a la fecha de imposición: con la de notificación se consulta todo el día");
    }

    /// <summary>Regresión QPN193 (7-oct-2026): contrato 1 hasta 08:11 y contrato 2 desde 11:29; el reloj se compara tal cual.</summary>
    [Theory]
    [InlineData("Q-1", "QPA192", 6, 17, "CLIENTE CONTRATO UNO")]
    [InlineData("Q-2", "QPB192", 8, 20, null)]
    [InlineData("Q-3", "QPC192", 11, 35, "CLIENTE CONTRATO DOS")]
    public async Task P19_Regresion_QPN193_AsignaElContratoDeEsaHora(string numero, string placa, int h, int m, string? esperado)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        // Renting falsa con la lógica de la real para esa placa y ese día, a la hora que llega.
        foreach (var (hh, mm) in new[] { (6, 17), (8, 20), (11, 35) })
        {
            var reloj = new TimeOnly(hh, mm);
            api.ContratosPorHora[$"{placa} {reloj:HH:mm}"] = reloj <= new TimeOnly(8, 11) ? Conductor("CLIENTE CONTRATO UNO", "uno@cliente.test")
                : reloj >= new TimeOnly(11, 29) ? Conductor("CLIENTE CONTRATO DOS", "dos@cliente.test") : Vacia;
        }

        var (_, id, cliente) = await PendienteAsync(numero, placa, new TimeOnly(h, m));
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();

        var c = await Leer(id);
        if (esperado is null)
            (c.EstadoConsultaContraventor, c.Contraventor).Should().Be((EstadoConsultaContraventor.NoEncontrado, (Flit.Comparendos.Api.Persistence.Modelo.Contraventor?)null));
        else
            c.Contraventor!.Nombres.Should().Be(esperado);
    }

    // ---------- Sin hora ----------

    [Fact]
    public async Task SinHora_MismoConductorTodoElDia_SeAsigna()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.ContratosPorHora["SHA122 00:00"] = Conductor("ANA PEREZ", "ana@cliente.test");
        api.ContratosPorHora["SHA122 23:59"] = Conductor("Ana Perez", "ANA@cliente.test");
        var (_, id, cliente) = await PendienteAsync("S-1", "SHA122", null);
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();

        Consultas("SHA122").Select(x => x.Hora).Should().Equal(new TimeOnly(0, 0), new TimeOnly(23, 59));
        var c = await Leer(id);
        (c.EstadoConsultaContraventor, c.Contraventor!.Nombres, c.IntentosConsultaContraventor).Should().Be((EstadoConsultaContraventor.Encontrado, "ANA PEREZ", (short)0));
    }

    [Theory]
    [InlineData("S-2", "SHB122", true)]
    [InlineData("S-3", "SHC122", false)]
    public async Task SinHora_ConductorDistinto_OUnaVacia_NoSeAsigna_YQuedaParaRevisar(string numero, string placa, bool distinto)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.ContratosPorHora[$"{placa} 00:00"] = Conductor("ANA PEREZ", "ana@cliente.test");
        api.ContratosPorHora[$"{placa} 23:59"] = distinto ? Conductor("LUIS GOMEZ", "luis@cliente.test") : Vacia;
        var (empresa, id, cliente) = await PendienteAsync(numero, placa, null);

        var r = await (await ConsultarAsync(cliente, id)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("resultado").GetString().Should().Be("SinHora");

        var c = await Leer(id);
        (c.EstadoConsultaContraventor, c.Contraventor, c.IntentosConsultaContraventor).Should()
            .Be((EstadoConsultaContraventor.SinHora, (Flit.Comparendos.Api.Persistence.Modelo.Contraventor?)null, (short)0), "no es un fallo: no gasta intentos");
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?rapido=SinContraventor", ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numero").GetString()).Should().Contain(numero, "queda en la bandeja para asignarlo a mano");
        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        detalle.GetProperty("estadoConsultaContraventor").GetString().Should().Be("SinHora");

        api.ConsultasContratos.Clear();
        api.Reloj.Ahora = api.Reloj.Ahora.AddDays(1);
        await JobAsync(empresa);
        Consultas(placa).Should().BeEmpty("el trabajo no vuelve a consultar lo que espera revisión de la gestora");
    }

    [Fact]
    public async Task SinHora_SiUnaFalla_EsUnSoloIntento()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.ContratosPorHora["SHD122 00:00"] = Conductor("ANA PEREZ", "ana@cliente.test");
        api.ContratosPorHora["SHD122 23:59"] = new RespuestaContrato(ResultadoContrato.Error, null, "Renting caído.", null);
        var (_, id, cliente) = await PendienteAsync("S-4", "SHD122", null);
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();

        var c = await Leer(id);
        (c.EstadoConsultaContraventor, c.IntentosConsultaContraventor).Should().Be((EstadoConsultaContraventor.Error, (short)1));
    }

    [Fact]
    public void SinHora_MismoConductor_ComparaDocumentoSoloSiLasDosLoTraen()
    {
        var a = new ConductorEncontrado(TipoDocumento.CC, "1", "ANA", "a@x.test", null, null, null, null, null);
        ServicioContraventor.MismoConductor(a, a with { TipoDocumento = null, NumeroDocumento = null, Nombres = " ana " }).Should().BeTrue();
        ServicioContraventor.MismoConductor(a, a with { NumeroDocumento = "2" }).Should().BeFalse();
        ServicioContraventor.MismoConductor(a, a with { Correo = "b@x.test" }).Should().BeFalse();
    }

    // ---------- Punto 20: tope de intentos ----------

    [Fact]
    public async Task P20_Manual_TresIntentos_YElCuartoSeRechaza_SinLlamarALaApi()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, id, cliente) = await PendienteAsync("T-1", "TPA120", new TimeOnly(10, 0));

        foreach (var (intento, estado) in new[] { (1, EstadoConsultaContraventor.Error), (2, EstadoConsultaContraventor.Error), (3, EstadoConsultaContraventor.Agotada) })
        {
            (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();
            var c = await Leer(id);
            (c.IntentosConsultaContraventor, c.EstadoConsultaContraventor).Should().Be(((short)intento, estado), $"intento {intento}");
        }

        var cuarto = await ConsultarAsync(cliente, id);
        cuarto.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cuarto.Content.ReadAsStringAsync(ct)).Should().Contain("CONSULTA_AGOTADA");
        Consultas("TPA120").Should().HaveCount(3, "el cuarto intento no sale a la API");
        (await Leer(id)).IntentosConsultaContraventor.Should().Be(3);
    }

    [Fact]
    public async Task P20_Job_CuatroVecesElMismoDia_GastaUnSoloIntento_YAlDiaSiguienteOtro()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, id, _) = await PendienteAsync("T-2", "TPB120", new TimeOnly(10, 0));
        api.Reloj.Hoy(new DateOnly(2026, 11, 3));

        for (var i = 0; i < 4; i++)
            await JobAsync(empresa);
        var c = await Leer(id);
        (c.IntentosConsultaContraventor, c.EstadoConsultaContraventor, c.UltimoIntentoConsultaContraventor).Should()
            .Be(((short)1, EstadoConsultaContraventor.Error, (DateOnly?)new DateOnly(2026, 11, 3)));

        api.Reloj.Hoy(new DateOnly(2026, 11, 4));
        await JobAsync(empresa);
        await JobAsync(empresa);
        (await Leer(id)).IntentosConsultaContraventor.Should().Be(2);
    }

    [Fact]
    public async Task P20_ManualHoy_ElJobYaNoIntentaHoy()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, id, cliente) = await PendienteAsync("T-3", "TPC120", new TimeOnly(10, 0));
        (await ConsultarAsync(cliente, id)).EnsureSuccessStatusCode();
        await JobAsync(empresa);
        (await Leer(id)).IntentosConsultaContraventor.Should().Be(1);
    }

    [Fact]
    public async Task P20_TresDiasDeFallos_QuedaAgotada_YElJobNoVuelveATomarla()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, id, _) = await PendienteAsync("T-4", "TPD120", new TimeOnly(10, 0));
        foreach (var dia in new[] { 10, 11, 12, 13, 14 })
        {
            api.Reloj.Hoy(new DateOnly(2026, 11, dia));
            await JobAsync(empresa);
        }

        var c = await Leer(id);
        (c.IntentosConsultaContraventor, c.EstadoConsultaContraventor).Should().Be(((short)3, EstadoConsultaContraventor.Agotada));
        Consultas("TPD120").Should().HaveCount(3, "después del tercer fallo no hay más llamadas");
        c.ErrorConsultaContraventor.Should().NotBeNullOrEmpty("el SuperAdmin sigue viendo el último error");
    }

    [Theory]
    [InlineData("N-1", "NSA122", EstadoConsultaContraventor.SinHora)]
    [InlineData("N-2", "NSB120", EstadoConsultaContraventor.Agotada)]
    public async Task AlDesmarcarElNit_SinHoraYAgotada_PasanANoAplica(string numero, string placa, EstadoConsultaContraventor estado)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, id, _) = await PendienteAsync(numero, placa, new TimeOnly(10, 0));
        await api.EnDbAsync(async db =>
        {
            var c = await db.Comparendos.SingleAsync(x => x.Id == id, ct);
            (c.EstadoConsultaContraventor, c.IntentosConsultaContraventor) = (estado, (short)(estado == EstadoConsultaContraventor.Agotada ? 3 : 0));
            await db.SaveChangesAsync(ct);
        });

        await ConsultaPorApiDeLosNitsAsync(api, empresa, activo: false);

        (await Leer(id)).EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.NoAplica, "sin consulta automática para el NIT, se asigna a mano como los demás");
    }
}
