using System.Text.Json;
using Flit.PersonaNatural.Api.Consultas;
using Flit.Platform.Sdk;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14040 (Feature #14022, Épica #14021) — consulta de conductor con proyección a dato mínimo, sobre un core-consultas
/// falso (sin red, sin proveedor, sin datos reales).
/// </summary>
public sealed class ConsultaConductorTests
{
    private static readonly DocumentoConsulta Documento = new("CC", "1098765432");

    // ── AC1: dato mínimo ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnDocumentoConLicencia_DevuelveExisteEstadoYVigencia_YNadaPersonal()
    {
        var runt = new ConsultasRuntFalso { Conductor = () => ResultadosFalsos.Conductor() };

        var conductor = await new ConsultaConductorService(runt).ConsultarAsync(Documento, null, TestContext.Current.CancellationToken);

        conductor.Should().Be(new ConductorMinimo(true, "ACTIVO", "vigente"));
        var json = JsonSerializer.Serialize(conductor, JsonSerializerOptions.Web);
        json.Should().NotContainAny("NOMBRE-FICTICIO", "APELLIDO-FICTICIO", "1990", "1098765432", "PYS-FICTICIO", "B1", "multas", "sancion");
        typeof(ConductorMinimo).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Existe", "EstadoLicencia", "Vigencia");
    }

    [Fact]
    public async Task UnaLicenciaNoActiva_ExisteConVigenciaNoVigente()
    {
        var runt = new ConsultasRuntFalso { Conductor = () => ResultadosFalsos.Conductor(licenciaActiva: false, estadoConductor: "SUSPENDIDO") };

        var conductor = await new ConsultaConductorService(runt).ConsultarAsync(Documento, null, TestContext.Current.CancellationToken);

        conductor.Should().Be(new ConductorMinimo(true, "SUSPENDIDO", "no_vigente"));
    }

    // ── AC2: sin licencia ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnDocumentoSinLicencia_ResponderExisteFalse_SinError()
    {
        var sinRegistro = new ConsultasRuntFalso { Conductor = ResultadosFalsos.ConductorInexistente };
        var personaSinLicencia = new ConsultasRuntFalso { Conductor = () => ResultadosFalsos.Conductor(licenciaActiva: false, estadoConductor: null, conMultas: false) };

        var a = await new ConsultaConductorService(sinRegistro).ConsultarAsync(Documento, null, TestContext.Current.CancellationToken);
        var b = await new ConsultaConductorService(personaSinLicencia).ConsultarAsync(Documento, null, TestContext.Current.CancellationToken);

        a.Should().Be(new ConductorMinimo(false, null, null));
        b.Should().Be(new ConductorMinimo(false, null, null));
    }

    [Fact]
    public async Task UnChequeoEnError_SeTraduceAProveedorNoDisponible_SinReintentar()
    {
        var runt = new ConsultasRuntFalso { Conductor = ResultadosFalsos.ProveedorCaido };

        var act = () => new ConsultaConductorService(runt).ConsultarAsync(Documento, null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProveedorNoDisponibleException>();
        runt.LlamadasConductor.Should().HaveCount(1);
    }

    // ── AC3: caché y medición (las resuelve core-consultas; el portal tiene que pedirlas bien) ────────────────────────

    [Fact]
    public async Task ElAdaptadorPideElConductorConConsentimientoYALaEmpresaPersonaNatural()
    {
        var cliente = new ClienteConsultasFalso { Responder = () => ResultadosFalsos.Conductor() };
        var adaptador = new ConsultasRuntGrpc(cliente, NullLogger<ConsultasRuntGrpc>.Instance);

        await adaptador.ConsultarConductorAsync(Documento, "ref-2", TestContext.Current.CancellationToken);

        var peticion = cliente.UltimaPeticionConductor!;
        peticion.Documento.Tipo.Should().Be("CC");
        peticion.Documento.Numero.Should().Be("1098765432");
        peticion.Opciones.ConsentimientoDatosPersonales.Should().BeTrue("sin consentimiento core-consultas no reutiliza la caché de una persona");
        peticion.Opciones.ForzarActualizacion.Should().BeFalse("forzar saltaría la caché por TTL");
        peticion.Opciones.Proveedor.Should().BeEmpty("la cadena la decide la configuración de la empresa");
        cliente.UltimasCabeceras!.GetValue("x-flit-tenant-id").Should().Be(PlatformTenants.PersonaNatural.ToString());
    }

    [Fact]
    public async Task UnaFallaGrpc_SeTraduceAProveedorNoDisponible_SinFiltrarElDocumento()
    {
        var cliente = new ClienteConsultasFalso { Falla = new RpcException(new Status(StatusCode.Unavailable, "documento 1098765432")) };
        var adaptador = new ConsultasRuntGrpc(cliente, NullLogger<ConsultasRuntGrpc>.Instance);

        var act = () => adaptador.ConsultarConductorAsync(Documento, null, TestContext.Current.CancellationToken);

        var error = (await act.Should().ThrowAsync<ProveedorNoDisponibleException>()).Which;
        error.Message.Should().NotContain("1098765432");
    }
}
