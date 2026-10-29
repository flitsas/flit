using System.Net;
using Flit.PersonaNatural.Api.Consentimientos;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14041/#14047, revisión de seguridad (A3 parcial, B2) — la versión del texto de consentimiento se valida en el servidor
/// y las consultas públicas dejan una constancia mínima (solo huellas). Datos ficticios.
/// </summary>
public sealed class ConsentimientoTests
{
    private const string Vehiculo = $"{SesionEndpoints.Base}/consultas/vehiculo";
    private const string Persona = $"{SesionEndpoints.Base}/consultas/persona";
    private const string DocumentoPropietario = "1098765432";

    private readonly ConsultasRuntFalso _runt = new();

    private Task<PortalHost> IniciarAsync(params (string Clave, string Valor)[] ajustes) =>
        PortalHost.IniciarAsync("pncons", (servicios, _) => servicios.AddSingleton<IConsultasRunt>(_runt), ajustes);

    private static PiiHasher Hasher() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { [PiiHasher.ClaveConfig] = PortalHost.ClaveHmac }).Build());

    // ── Constancia de las consultas públicas ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaConsultaDeVehiculoPorPlaca_DejaLaConstanciaConLaHuellaDelPropietario()
    {
        await using var host = await IniciarAsync();
        _runt.Vehiculo = () => ResultadosFalsos.VehiculoCompleto();

        var r = await host.EnviarAsync(HttpMethod.Post, Vehiculo,
            new { placa = "ABC123", propietario = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true, consentimientoVersion = "v1" }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = host.NuevaDb();
        var fila = await db.Set<Consentimiento>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        fila.SolicitudId.Should().BeNull("todavía no hay solicitud");
        fila.DocumentoHash.Should().Equal(Hasher().HashCedula(DocumentoPropietario), "solo la huella del documento consultado");
        fila.TextoVersion.Should().Be("v1");
        fila.AceptadoEn.Should().Be(host.Reloj.GetUtcNow());
        fila.IpHash.Should().HaveCount(32);
    }

    [Fact]
    public async Task LaConsultaDeVehiculoPorVin_DejaLaConstanciaConLaHuellaDelVin()
    {
        await using var host = await IniciarAsync();

        var r = await host.EnviarAsync(HttpMethod.Post, Vehiculo, new { vin = "9BWZZZ377VT004251", consentimiento = true }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = host.NuevaDb();
        var fila = await db.Set<Consentimiento>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        fila.DocumentoHash.Should().Equal(Hasher().HashVehiculo("9BWZZZ377VT004251"));
        fila.TextoVersion.Should().Be("v1", "sin versión del cliente vale la vigente");
    }

    [Fact]
    public async Task LaConsultaDePersona_DejaLaConstanciaConLaHuellaDelDocumentoConsultado()
    {
        await using var host = await IniciarAsync();
        _runt.Conductor = () => ResultadosFalsos.Conductor();

        var r = await host.EnviarAsync(HttpMethod.Post, Persona,
            new { documento = new { tipo = "cc", numero = "1.098.765.432" }, consentimiento = true, consentimientoVersion = "v1" }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = host.NuevaDb();
        var fila = await db.Set<Consentimiento>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        fila.DocumentoHash.Should().Equal(Hasher().HashCedula(DocumentoPropietario));
        fila.SolicitudId.Should().BeNull();
    }

    [Fact]
    public async Task SinConsentimiento_NoSeGuardaNada()
    {
        await using var host = await IniciarAsync();

        var r = await host.EnviarAsync(HttpMethod.Post, Persona, new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = false }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = host.NuevaDb();
        (await db.Set<Consentimiento>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ── Versión validada en el servidor ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Vehiculo)]
    [InlineData(Persona)]
    public async Task UnaVersionDesconocida_EnUnaConsultaPublica_Responde422_SinConsultarNiGuardar(string ruta)
    {
        await using var host = await IniciarAsync();
        object cuerpo = ruta == Vehiculo
            ? new { vin = "9BWZZZ377VT004251", consentimiento = true, consentimientoVersion = "v9" }
            : new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true, consentimientoVersion = "v9" };

        var r = await host.EnviarAsync(HttpMethod.Post, ruta, cuerpo, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be(ConsentimientoVersiones.CodigoVersionDesconocida);
        _runt.LlamadasVehiculo.Should().BeEmpty();
        _runt.LlamadasConductor.Should().BeEmpty();
        await using var db = host.NuevaDb();
        (await db.Set<Consentimiento>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task UnaVersionDesconocida_AlAbrirElTramite_Responde422()
    {
        await using var host = await IniciarAsync();

        var r = await host.EnviarAsync(HttpMethod.Post, $"{SesionEndpoints.Base}/sesiones",
            new { tipoCodigo = "TRASPASO_STANDARD", organismoId = PortalHost.Organismo, cedula = PortalHost.Cedula, placa = "ABC123", consentimiento = true, consentimientoVersion = "texto-inventado" }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be(ConsentimientoVersiones.CodigoVersionDesconocida);
        await using var db = host.NuevaDb();
        (await db.Set<SolicitudTramite>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task LasVersionesVigentesSeConfiguran_YLaVersionEnBlancoTomaLaMasReciente()
    {
        await using var host = await IniciarAsync(("Consentimiento:Versiones:0", "v1"), ("Consentimiento:Versiones:1", "v2"));

        var v2 = await host.EnviarAsync(HttpMethod.Post, Persona, new { documento = new { tipo = "CC", numero = DocumentoPropietario }, consentimiento = true, consentimientoVersion = "V2" }, captcha: true);
        var enBlanco = await host.EnviarAsync(HttpMethod.Post, Persona, new { documento = new { tipo = "CC", numero = "1098765433" }, consentimiento = true }, captcha: true);
        var v3 = await host.EnviarAsync(HttpMethod.Post, Persona, new { documento = new { tipo = "CC", numero = "1098765434" }, consentimiento = true, consentimientoVersion = "v3" }, captcha: true);

        v2.StatusCode.Should().Be(HttpStatusCode.OK);
        enBlanco.StatusCode.Should().Be(HttpStatusCode.OK);
        v3.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await using var db = host.NuevaDb();
        (await db.Set<Consentimiento>().OrderBy(c => c.AceptadoEn).ThenBy(c => c.Id).Select(c => c.TextoVersion).ToListAsync(TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(["v2", "v2"]);
    }

    [Theory]
    [InlineData(null, new[] { "v1" }, "v1")]
    [InlineData("v1,v2", new[] { "v1", "v2" }, "v2")]
    [InlineData(" v1 , v2 ,v2", new[] { "v1", "v2" }, "v2")]
    [InlineData("", new[] { "v1" }, "v1")]
    public void LasVersionesVigentes_ConValoresPorDefectoYCsv(string? csv, string[] esperadas, string porDefecto)
    {
        var datos = new Dictionary<string, string?>();
        if (csv is not null)
            datos[ConsentimientoVersiones.SectionName] = csv;

        var versiones = new ConsentimientoVersiones(new ConfigurationBuilder().AddInMemoryCollection(datos).Build());

        versiones.Vigentes.Should().Equal(esperadas);
        versiones.PorDefecto.Should().Be(porDefecto);
        versiones.Normalizar("v9").Should().BeNull();
    }
}
