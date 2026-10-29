using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Consultas;
using Flit.Platform.Sdk;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14039 (Feature #14022, Épica #14021) — consulta de vehículo con proyección a dato mínimo, sobre un core-consultas
/// falso (sin red, sin proveedor, sin datos reales).
/// </summary>
public sealed class ConsultaVehiculoTests
{
    // ── AC1: dato mínimo ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaPlacaRegistrada_DevuelveElDatoMinimo_YNadaDelPropietario()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = () => ResultadosFalsos.VehiculoCompleto(prenda: true) };

        var vehiculo = await new ConsultaVehiculoService(runt).ConsultarAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        vehiculo.Should().BeEquivalentTo(new VehiculoMinimo(
            true, "ABC123", "AUTOMOVIL", "MARCA-FICTICIA", "LINEA-FICTICIA", "2018", "PARTICULAR", "ACTIVO",
            TienePrenda: true, TieneLimitacion: false, TieneRestricciones: true));
        var json = JsonSerializer.Serialize(vehiculo, JsonSerializerOptions.Web);
        json.Should().NotContainAny("1098765432", "PROPIETARIO", "FICTICIO PEREZ", "CALLE FICTICIA", "3000000000", "BANCO FICTICIO", "PERSONA FICTICIA",
            "9BWZZZ377VT004251", "MOTOR-FICTICIO", "SERIE-FICTICIA", "ROJO", "owner", "soat");
    }

    [Fact]
    public void LaConsultaDelTramite_TraeLosAtributosDeFlit2_YElPropietarioCompleto()
    {
        var vehiculo = ProyeccionMinima.DelTramite(ResultadosFalsos.VehiculoCompleto(prenda: true), "ABC123");

        vehiculo.Existe.Should().BeTrue();
        vehiculo.Vin.Should().Be("9BWZZZ377VT004251");
        vehiculo.Color.Should().Be("ROJO");
        vehiculo.Combustible.Should().Be("GASOLINA");
        vehiculo.Cilindraje.Should().Be("1600");
        vehiculo.NumeroMotor.Should().Be("MOTOR-FICTICIO-1");
        vehiculo.NumeroSerie.Should().Be("SERIE-FICTICIA-1");
        vehiculo.PropietarioNombre.Should().Be("PROPIETARIO FICTICIO PEREZ");
        vehiculo.PropietarioDocumento.Should().Be("CC 1098765432");
        var json = JsonSerializer.Serialize(vehiculo, JsonSerializerOptions.Web);
        json.Should().Contain("ROJO").And.Contain("9BWZZZ377VT004251").And.Contain("PROPIETARIO FICTICIO PEREZ").And.Contain("1098765432");
        json.Should().NotContainAny("CALLE FICTICIA", "3000000000", "BANCO FICTICIO", "soat");
    }

    [Fact]
    public void ElDto_NoTienePropiedadesDePropietarioNiDeDocumento()
    {
        var propiedades = typeof(VehiculoMinimo).GetProperties().Select(p => p.Name.ToLowerInvariant());

        propiedades.Should().NotContain(n => n.Contains("propiet") || n.Contains("document") || n.Contains("nombre")
            || n.Contains("direccion") || n.Contains("telefono") || n.Contains("sancion") || n.Contains("owner"));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    public async Task LasRestriccionesSeResumenEnBanderas_SinDecirDeQuienNiAFavorDeQuien(bool prenda, bool limitacion, bool esperaPrenda, bool esperaRestricciones)
    {
        var runt = new ConsultasRuntFalso { Vehiculo = () => ResultadosFalsos.VehiculoCompleto(prenda, limitacion) };

        var v = await new ConsultaVehiculoService(runt).ConsultarAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        v.TienePrenda.Should().Be(esperaPrenda);
        v.TieneLimitacion.Should().Be(limitacion);
        v.TieneRestricciones.Should().Be(esperaRestricciones);
    }

    // ── AC2: placa inexistente ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaPlacaQueNoExiste_ResponderExisteFalse_SinError()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.VehiculoInexistente };

        var v = await new ConsultaVehiculoService(runt).ConsultarAsync("ZZZ999", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        v.Should().BeEquivalentTo(new VehiculoMinimo(false, "ZZZ999", null, null, null, null, null, null, false, false, false));
    }

    // ── AC5: proveedor caído ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnChequeoEnError_SeTraduceAProveedorNoDisponible()
    {
        var runt = new ConsultasRuntFalso { Vehiculo = ResultadosFalsos.ProveedorCaido };

        var act = () => new ConsultaVehiculoService(runt).ConsultarAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProveedorNoDisponibleException>();
        runt.LlamadasVehiculo.Should().HaveCount(1, "no se reintenta en bucle");
    }

    // ── Adaptador gRPC ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElAdaptadorLlamaConLaEmpresaPersonaNaturalYElConsentimiento_PorPlacaConElPropietario()
    {
        var cliente = new ClienteConsultasFalso { Responder = () => ResultadosFalsos.VehiculoCompleto() };
        var adaptador = new ConsultasRuntGrpc(cliente, NullLogger<ConsultasRuntGrpc>.Instance);

        await adaptador.ConsultarVehiculoAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), "ref-1", TestContext.Current.CancellationToken);

        var peticion = cliente.UltimaPeticionVehiculo!;
        peticion.Placa.Valor.Should().Be("ABC123");
        peticion.Propietario.Tipo.Should().Be("CC");
        peticion.Propietario.Numero.Should().Be("1098765432");
        peticion.Vin.Should().BeNull();
        peticion.Opciones.Referencia.Should().Be("ref-1");
        peticion.Opciones.ConsentimientoDatosPersonales.Should().BeTrue();
        cliente.UltimasCabeceras!.GetValue("x-flit-tenant-id").Should().Be(PlatformTenants.PersonaNatural.ToString());
    }

    [Fact]
    public async Task ElAdaptadorConsultaPorVinSinPropietario()
    {
        var cliente = new ClienteConsultasFalso { Responder = () => ResultadosFalsos.VehiculoCompleto() };
        var adaptador = new ConsultasRuntGrpc(cliente, NullLogger<ConsultasRuntGrpc>.Instance);

        await adaptador.ConsultarVehiculoAsync(null, "9BWZZZ377VT004251", null, null, TestContext.Current.CancellationToken);

        cliente.UltimaPeticionVehiculo!.Vin.Valor.Should().Be("9BWZZZ377VT004251");
        cliente.UltimaPeticionVehiculo.Placa.Should().BeNull();
        cliente.UltimaPeticionVehiculo.Propietario.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Internal)]
    public async Task UnaFallaGrpc_SeTraduceAProveedorNoDisponible_SinFiltrarElDetalleNiLosDatos(StatusCode estado)
    {
        var cliente = new ClienteConsultasFalso { Falla = new RpcException(new Status(estado, "detalle con placa ABC123 y cédula 1098765432")) };
        var adaptador = new ConsultasRuntGrpc(cliente, NullLogger<ConsultasRuntGrpc>.Instance);

        var act = () => adaptador.ConsultarVehiculoAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        var error = (await act.Should().ThrowAsync<ProveedorNoDisponibleException>()).Which;
        error.Message.Should().NotContain("ABC123").And.NotContain("1098765432");
        error.InnerException.Should().BeNull("la excepción original trae el detalle del servidor");
    }

    // ── AC3 y AC4: la cadena y la medición son de core-consultas, por empresa y producto ─────────────────────────────

    [Fact]
    public void ConDireccionDeConsultas_ElPortalRegistraElClienteGrpcConElScopeDeConsultas()
    {
        using var app = Program.Build([], b => b.Configuration.AddInMemoryCollection(ConfigMinima("http://core-consultas:8084")));

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IConsultasRunt>().Should().BeOfType<ConsultasRuntGrpc>();
        ConsultasRuntExtensions.Scope.Should().Be("platform.consultas", "es el scope que core-consultas exige y que svc-personanatural tiene en Identidad");
    }

    [Fact]
    public async Task SinDireccionDeConsultas_ElPortalArrancaYResponderProveedorNoDisponible()
    {
        using var app = Program.Build([], b => b.Configuration.AddInMemoryCollection(ConfigMinima(null)));

        using var scope = app.Services.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<ConsultaVehiculoService>()
            .ConsultarAsync("ABC123", null, new DocumentoConsulta("CC", "1098765432"), null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProveedorNoDisponibleException>();
    }

    // ── Formato de la entrada ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc123", "ABC123")]
    [InlineData(" ABC-12D ", "ABC12D")]
    [InlineData("r12345", "R12345")]
    [InlineData("S12345", "S12345")]
    [InlineData("AB1234", null)]
    [InlineData("ABC1234", null)]
    [InlineData("123ABC", null)]
    [InlineData("ABC12!", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LaPlacaSeValidaYSeNormaliza(string? entrada, string? esperada) => EntradaConsulta.Placa(entrada).Should().Be(esperada);

    [Theory]
    [InlineData("9bwzzz377vt004251", "9BWZZZ377VT004251")]
    [InlineData("9BWZZZ377VT00425", null)]
    [InlineData("9BWZZZ377VT00425I", null)]
    [InlineData("", null)]
    public void ElVinSeValidaYSeNormaliza(string entrada, string? esperado) => EntradaConsulta.Vin(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("cc", "1.098.765.432", "CC", "1098765432")]
    [InlineData("CE", "123 456", "CE", "123456")]
    [InlineData("pas", "ab-12345", "PAS", "AB12345")]
    [InlineData("CC", "12", null, null)]
    [InlineData("CC", "10987654321234", null, null)]
    [InlineData("CC", "10A87654", null, null)]
    [InlineData("NIT", "900123456", null, null)]
    [InlineData("", "1098765432", null, null)]
    [InlineData("CC", null, null, null)]
    public void ElDocumentoSeValidaPorTipo(string tipo, string? numero, string? tipoEsperado, string? numeroEsperado)
    {
        var documento = EntradaConsulta.Documento(tipo, numero);

        documento?.Tipo.Should().Be(tipoEsperado);
        documento?.Numero.Should().Be(numeroEsperado);
        (documento is null).Should().Be(tipoEsperado is null);
    }

    private static Dictionary<string, string?> ConfigMinima(string? consultas) => new()
    {
        ["ConnectionStrings:Servicio"] = "Host=127.0.0.1;Database=no_se_usa",
        ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
        ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
        ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
        [ServicioSettings.GrpcPortKey] = "5996",
        ["Pii:ClaveHmac"] = "clave-de-prueba-de-al-menos-32-caracteres!",
        ["Captcha:SecretKey"] = "secreto-de-captcha",
        ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["Consultas:Remoto:Address"] = consultas,
    };
}
