using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// Épica #14644, Feature #14645 — contingencia de proveedor RUNT contra core-consultas real (Postgres efímero, tokens firmados con
/// una llave de prueba). HU #14647 (aplicarla en las consultas) y HU #14648 (activar, apagar y consultar por gRPC). Los proveedores
/// son falsos y anotan el orden en que los llamaron; la empresa de la prueba tiene configurado Kyverum de primario o Verifik
/// de primario según el caso.
/// </summary>
public sealed class ContingenciaRuntTests : IAsyncLifetime
{
    private const int GrpcPort = 5995;
    private const string Issuer = "https://hub.prueba/";
    private const string Admin = "platform.consultas.admin";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresaKyverum = Guid.NewGuid();
    private readonly Guid _empresaVerifik = Guid.NewGuid();
    private readonly ConcurrentQueue<string> _llamadas = new();
    private readonly ConcurrentDictionary<string, bool> _caidos = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_cont_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Servicio"] = _database,
                ["Platform:ServiceClient:ClientSecret"] = "secreto",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
                ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
                ["Platform:Auth:Issuers:0"] = Issuer,
                ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
                ["Consultations:VerifikVehicleMode"] = "mock",
                ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
        },
        // Después del registro de Program: los proveedores RUNT (y los de otros tipos) son falsos que anotan quién fue llamado.
        s => s.AddSingleton<IConsultationProviderRegistry>(new RegistroFalso(_llamadas, _caidos)));
        await Program.MigrateAsync(_app);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
            db.ConfiguracionEmpresas.Add(Config(_empresaKyverum,
                """{"vehicle_plate":{"primary":"kyverum_runt","fallback":[]},"vehicle_vin":{"primary":"kyverum_runt","fallback":[]},"conductor":{"primary":"kyverum_runt_conductor","fallback":[]}}"""));
            db.ConfiguracionEmpresas.Add(Config(_empresaVerifik,
                """{"vehicle_plate":{"primary":"verifik","fallback":["kyverum_runt"]},"vehicle_vin":{"primary":"verifik","fallback":["kyverum_runt"]},"conductor":{"primary":"verifik_conductor","fallback":["kyverum_runt_conductor"]}}"""));
            await db.SaveChangesAsync();
        }

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── HU #14647: aplicar la contingencia en las consultas ────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14647_AC1_ContingenciaActivaPorPlaca_LaConsultaDeTramitesVaPrimeroAVerifik()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");

        var respuesta = await ConsultarPlaca("svc-tramites", _empresaKyverum);

        respuesta.Resultado.Proveedor.Should().Be("verifik");
        Llamadas().Should().Equal("verifik");
    }

    [Fact]
    public async Task HU14647_AC2_VerifikFalla_CaeAlRestoDeLaCadenaDeLaEmpresaSinRepetirVerifik()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");
        _caidos["verifik"] = true;

        // La empresa trae verifik primero y kyverum_runt de respaldo: verifik no se repite.
        var respuesta = await ConsultarPlaca("svc-tramites", _empresaVerifik);

        respuesta.Resultado.Proveedor.Should().Be("kyverum_runt");
        Llamadas().Should().Equal("verifik", "kyverum_runt");
    }

    [Fact]
    public async Task HU14647_AC3_PortalPersonaNatural_ConductorVaPrimeroAVerifikConductor()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("conductor", "verifik_conductor");

        var respuesta = await Cliente(Token("platform.consultas", "svc-personanatural")).ConsultarConductorAsync(
            new ConsultarConductorRequest { Documento = new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = "CC", Numero = "123456" } },
            Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);

        respuesta.Resultado.Proveedor.Should().Be("verifik_conductor");
        Llamadas().Should().Equal("verifik_conductor");
    }

    [Fact]
    public async Task HU14647_AC4_IntegracionIct_VinVaPrimeroAVerifik()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_vin", "verifik");

        var respuesta = await Cliente(Token("platform.consultas", "svc-ict")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest { Vin = new Flit.Platform.Grpc.V1.Vin { Valor = "9BWZZZ377VT004251" } },
            Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);

        respuesta.Resultado.Proveedor.Should().Be("verifik");
        Llamadas().Should().Equal("verifik");
    }

    [Fact]
    public async Task HU14647_AC5_ProveedorFijoDeLaPlantilla_LaContingenciaVaPrimero()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "kyverum_runt");

        // La plantilla RUNT_VEHICLE fija verifik: con contingencia a kyverum_runt, este va primero y la cadena de la empresa
        // (verifik, kyverum_runt) queda de respaldo sin repetirlo.
        _caidos["kyverum_runt"] = true;
        var respuesta = await ConsultarPlaca("svc-tramites", _empresaVerifik, proveedorFijo: "verifik");

        respuesta.Resultado.Proveedor.Should().Be("verifik");
        Llamadas().Should().Equal("kyverum_runt", "verifik");
    }

    [Fact]
    public async Task HU14647_AC5b_ProveedorFijoQueNoEsRunt_NoSeCambia()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");

        var respuesta = await ConsultarPlaca("svc-tramites", _empresaKyverum, proveedorFijo: "intempo");

        respuesta.Resultado.Proveedor.Should().Be("intempo");
        Llamadas().Should().Equal("intempo");
    }

    [Theory]
    [InlineData("svc-diagnostico")]
    [InlineData("svc-comparendos")]
    public async Task HU14647_AC6_OtrosProductos_UsanLaCadenaDeSiempre(string producto)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");
        await Activar("vehicle_vin", "verifik");
        await Activar("conductor", "verifik_conductor");

        var respuesta = await ConsultarPlaca(producto, _empresaKyverum);
        var conductor = await Cliente(Token("platform.consultas", producto)).ConsultarConductorAsync(
            new ConsultarConductorRequest { Documento = new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = "CC", Numero = "123456" } },
            Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);

        respuesta.Resultado.Proveedor.Should().Be("kyverum_runt");
        conductor.Resultado.Proveedor.Should().Be("kyverum_runt_conductor");
        Llamadas().Should().Equal("kyverum_runt", "kyverum_runt_conductor");
    }

    [Fact]
    public async Task HU14647_AC7_OtrosTiposDeConsulta_ElProveedorNoCambia()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");
        await Activar("vehicle_vin", "verifik");
        await Activar("conductor", "verifik_conductor");
        var ct = TestContext.Current.CancellationToken;
        var cliente = Cliente(Token("platform.consultas", "svc-tramites"));

        var rnmc = await cliente.ConsultarRnmcAsync(
            new ConsultarRnmcRequest { Documento = new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = "CC", Numero = "123456" }, FechaExpedicionDocumento = "2000-01-01" },
            Empresa(_empresaKyverum), cancellationToken: ct);
        var rues = await cliente.ConsultarRuesAsync(new ConsultarRuesRequest { Nit = "900123456" }, Empresa(_empresaKyverum), cancellationToken: ct);
        var multas = await cliente.ConsultarMultasAsync(
            new ConsultarMultasRequest { Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" }, Opciones = new OpcionesConsulta { Proveedor = "verifik_simit" } },
            Empresa(_empresaKyverum), cancellationToken: ct);

        (rnmc.Resultado.Proveedor, rues.Resultado.Proveedor, multas.Resultado.Proveedor).Should().Be(("verifik_rnmc", "verifik_rues", "verifik_simit"));
        Llamadas().Should().Equal("verifik_rnmc", "verifik_rues", "verifik_simit");
    }

    [Fact]
    public async Task HU14647_AC8_SinContingencia_UsaLaCadenaDeLaEmpresa()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var respuesta = await ConsultarPlaca("svc-tramites", _empresaKyverum);

        respuesta.Resultado.Proveedor.Should().Be("kyverum_runt");
        Llamadas().Should().Equal("kyverum_runt");
    }

    [Fact]
    public async Task HU14647_AC9_ActivarYApagar_NoCambiaLaConfiguracionDeLaEmpresa()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var antes = await ConfiguracionDe(_empresaKyverum);

        await Activar("vehicle_plate", "verifik");
        await ConsultarPlaca("svc-tramites", _empresaKyverum);
        var durante = await ConfiguracionDe(_empresaKyverum);
        await Apagar("vehicle_plate");
        var despues = await ConfiguracionDe(_empresaKyverum);
        var vistaDeLaEmpresa = await AdminCliente().ObtenerConfiguracionEmpresaAsync(
            new ObtenerConfiguracionEmpresaRequest(), Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);

        durante.Should().BeEquivalentTo(antes);
        despues.Should().BeEquivalentTo(antes);
        vistaDeLaEmpresa.Configuracion.Cadenas["vehicle_plate"].Principal.Should().Be("kyverum_runt", "la empresa no se entera de la contingencia");
        vistaDeLaEmpresa.Configuracion.Cadenas["vehicle_plate"].Respaldo.Should().BeEmpty();
    }

    [Fact]
    public async Task HU14647_Apagar_LasConsultasVuelvenALaCadenaDeLaEmpresaEnLaSiguienteLlamada()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");
        (await ConsultarPlaca("svc-tramites", _empresaKyverum)).Resultado.Proveedor.Should().Be("verifik");

        await Apagar("vehicle_plate");

        (await ConsultarPlaca("svc-tramites", _empresaKyverum)).Resultado.Proveedor.Should().Be("kyverum_runt");
    }

    [Fact]
    public async Task HU14647_LaContingenciaDeUnTipoNoAfectaLosDemas()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "verifik");

        var porVin = await Cliente(Token("platform.consultas", "svc-tramites")).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest { Vin = new Flit.Platform.Grpc.V1.Vin { Valor = "9BWZZZ377VT004251" } },
            Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);

        porVin.Resultado.Proveedor.Should().Be("kyverum_runt");
    }

    // ── HU #14648: activar y apagar ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14648_AC1_ObtenerDevuelveLosTresTiposEnOrden_ConProveedorMotivoUsuarioYFecha()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var inicial = await AdminCliente().ObtenerContingenciaRuntAsync(new ObtenerContingenciaRuntRequest(), cancellationToken: ct);
        inicial.Tipos.Select(t => t.Tipo).Should().Equal("vehicle_vin", "vehicle_plate", "conductor");
        inicial.Tipos.Should().OnlyContain(t => t.Proveedor == "" && t.Motivo == "" && t.ActualizadoPor == "" && t.ActualizadoEn == null);

        await Activar("vehicle_plate", "verifik", "Kyverum caído desde las 9:10", "Ana Pérez");
        var tras = await AdminCliente().ObtenerContingenciaRuntAsync(new ObtenerContingenciaRuntRequest(), cancellationToken: ct);

        tras.Tipos.Select(t => t.Tipo).Should().Equal("vehicle_vin", "vehicle_plate", "conductor");
        tras.Tipos[0].Proveedor.Should().BeEmpty();
        tras.Tipos[1].Should().BeEquivalentTo(new { Tipo = "vehicle_plate", Proveedor = "verifik", Motivo = "Kyverum caído desde las 9:10", ActualizadoPor = "Ana Pérez" },
            o => o.ExcludingMissingMembers());
        tras.Tipos[1].ActualizadoEn.ToDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task HU14648_AC2_Activar_QuedaGuardadaYLaSiguienteConsultaLaUsa()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var guardada = await Activar("vehicle_plate", "verifik", "Caída de Kyverum");

        guardada.Should().BeEquivalentTo(new { Tipo = "vehicle_plate", Proveedor = "verifik", Motivo = "Caída de Kyverum" }, o => o.ExcludingMissingMembers());
        (await ConsultarPlaca("svc-tramites", _empresaKyverum)).Resultado.Proveedor.Should().Be("verifik");
    }

    [Fact]
    public async Task HU14648_AC3_SinMotivo_InvalidArgumentYNadaCambia()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        foreach (var motivo in new[] { "", "   ", new string('x', 501) })
        {
            var llamada = async () => await Guardar("vehicle_plate", "verifik", motivo);
            (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        }

        var estado = await AdminCliente().ObtenerContingenciaRuntAsync(new ObtenerContingenciaRuntRequest(), cancellationToken: ct);
        estado.Tipos.Should().OnlyContain(t => t.Proveedor == "");
        (await ConsultarPlaca("svc-tramites", _empresaKyverum)).Resultado.Proveedor.Should().Be("kyverum_runt");
    }

    [Theory]
    [InlineData("vehicle_plate", "verifik_conductor")]
    [InlineData("conductor", "verifik")]
    [InlineData("vehicle_vin", "intempo")]
    [InlineData("vehicle_plate", "verifik_simit")]
    [InlineData("tipo_inventado", "verifik")]
    public async Task HU14648_AC4_ProveedorQueNoEsDeRuntParaEseTipo_InvalidArgumentYNadaCambia(string tipo, string proveedor)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var llamada = async () => await Guardar(tipo, proveedor, "Motivo");

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        await using var scope = _app!.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ConsultasDb>().ContingenciasRunt.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task HU14648_AC5_Apagar_LasConsultasVuelvenALaCadenaDeCadaEmpresa_ConservandoElMotivo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("conductor", "verifik_conductor");

        var apagada = await Apagar("conductor", "Kyverum restablecido");

        apagada.Should().BeEquivalentTo(new { Tipo = "conductor", Proveedor = "", Motivo = "Kyverum restablecido" }, o => o.ExcludingMissingMembers());
        var conductor = await Cliente(Token("platform.consultas", "svc-tramites")).ConsultarConductorAsync(
            new ConsultarConductorRequest { Documento = new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = "CC", Numero = "123456" } },
            Empresa(_empresaKyverum), cancellationToken: TestContext.Current.CancellationToken);
        conductor.Resultado.Proveedor.Should().Be("kyverum_runt_conductor");
    }

    [Fact]
    public async Task HU14648_Global_NoExigeLaMetadataDeEmpresa_YExigeElScopeAdmin()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        // Sin x-flit-tenant-id responde (en las demás llamadas de administración esa metadata es obligatoria).
        (await AdminCliente().ObtenerContingenciaRuntAsync(new ObtenerContingenciaRuntRequest(), cancellationToken: ct)).Tipos.Should().HaveCount(3);

        var sinScope = async () => await new ConsultasAdminService.ConsultasAdminServiceClient(Canal(Token("platform.consultas", "svc-tramites")))
            .ObtenerContingenciaRuntAsync(new ObtenerContingenciaRuntRequest(), cancellationToken: ct);
        (await sinScope.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task HU14648_Migracion_LaTablaEsGlobalYSinColumnaDeEmpresa()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();

        var columnas = await db.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'consultas' AND table_name = 'runt_contingencies'")
            .ToListAsync(TestContext.Current.CancellationToken);

        columnas.Should().NotBeEmpty().And.NotContain("tenant_id");
    }

    // ── Ajustes de revisión ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revision3_DosPrimerasEscriturasConcurrentesDelMismoTipo_NoChocan_YQuedaUnaSolaFila()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Guardar("vehicle_plate", i % 2 == 0 ? "verifik" : "kyverum_runt", $"Cambio {i}")));

        resultados.Should().HaveCount(6);
        await using var scope = _app!.Services.CreateAsyncScope();
        var filas = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().ContingenciasRunt.AsNoTracking()
            .Where(c => c.Kind == "vehicle_plate").ToListAsync(TestContext.Current.CancellationToken);
        filas.Should().ContainSingle();
    }

    [Fact]
    public async Task Revision4_ProveedorExplicitoSeComparaSinDistinguirMayusculas()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await Activar("vehicle_plate", "kyverum_runt");

        var respuesta = await ConsultarPlaca("svc-tramites", _empresaVerifik, proveedorFijo: "VERIFIK");

        respuesta.Resultado.Proveedor.Should().Be("kyverum_runt");
        Llamadas().Should().Equal("kyverum_runt");
    }

    [Theory]
    [InlineData("svc-ict")]
    [InlineData("svc-personanatural")]
    [InlineData("svc-diagnostico")]
    public async Task Revision7_GuardarContingencia_ConOtroClienteQueNoEsCoreApi_PermissionDenied_YNadaCambia(string cliente)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var admin = new ConsultasAdminService.ConsultasAdminServiceClient(Canal(Token(Admin, cliente)));

        var llamada = async () => await admin.GuardarContingenciaRuntAsync(
            new GuardarContingenciaRuntRequest { Tipo = "vehicle_plate", Proveedor = "verifik", Motivo = "Intento", ActualizadoPor = "X" },
            cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        await using var scope = _app!.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ConsultasDb>().ContingenciasRunt.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Revision6_LaExencionDeEmpresaEsUnaListaCerrada_OtrosMetodosDeAdminSiLaExigen()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        // En la lista: sin empresa responde (ListarDepartamentosImpuesto es de ConsultasService).
        var departamentos = await Cliente(Token("platform.consultas", "svc-tramites")).ListarDepartamentosImpuestoAsync(new ListarDepartamentosImpuestoRequest(), cancellationToken: ct);
        departamentos.Should().NotBeNull();

        // Fuera de la lista (mismo servicio de administración): sin empresa, INVALID_ARGUMENT.
        var sinEmpresa = async () => await AdminCliente().ObtenerConsumoAsync(new ObtenerConsumoRequest
        {
            Desde = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)),
            Hasta = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        }, cancellationToken: ct);
        (await sinEmpresa.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ── Apoyo ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private string[] Llamadas() => [.. _llamadas];

    private Task<ConsultarVehiculoResponse> ConsultarPlaca(string producto, Guid empresa, string? proveedorFijo = null) =>
        Cliente(Token("platform.consultas", producto)).ConsultarVehiculoAsync(
            new ConsultarVehiculoRequest
            {
                Placa = new Flit.Platform.Grpc.V1.Placa { Valor = "ABC123" },
                Opciones = proveedorFijo is null ? null : new OpcionesConsulta { Proveedor = proveedorFijo },
            },
            Empresa(empresa), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    private Task<ContingenciaRunt> Activar(string tipo, string proveedor, string motivo = "Caída del proveedor", string actor = "Super Admin") =>
        Guardar(tipo, proveedor, motivo, actor);

    private Task<ContingenciaRunt> Apagar(string tipo, string motivo = "Restablecido") => Guardar(tipo, string.Empty, motivo);

    private Task<ContingenciaRunt> Guardar(string tipo, string proveedor, string motivo, string actor = "Super Admin") =>
        AdminCliente().GuardarContingenciaRuntAsync(
            new GuardarContingenciaRuntRequest
            {
                Tipo = tipo, Proveedor = proveedor, Motivo = motivo, ActualizadoPor = actor, ActualizadoPorId = Guid.NewGuid().ToString(),
            },
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    private async Task<(string? Cadenas, int? Failover, string Fuente, DateTimeOffset Actualizado)> ConfiguracionDe(Guid empresa)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var fila = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().ConfiguracionEmpresas.AsNoTracking()
            .SingleAsync(c => c.TenantId == empresa, TestContext.Current.CancellationToken);
        return (fila.CadenasJson, fila.FailoverTimeoutMs, fila.FuenteMultas, fila.ActualizadoEn);
    }

    private ConsultasAdminService.ConsultasAdminServiceClient AdminCliente() =>
        new(Canal(Token(Admin, "svc-tramites")));

    private ConsultasService.ConsultasServiceClient Cliente(string token) => new(Canal(token));

    private CallInvoker Canal(string token)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        });
    }

    private static Metadata Empresa(Guid tenant) => new() { { "x-flit-tenant-id", tenant.ToString() } };

    private static Api.Persistence.ConfiguracionEmpresa Config(Guid tenant, string cadenas) =>
        new() { TenantId = tenant, CadenasJson = cadenas, FuenteMultas = "external", ActualizadoEn = DateTimeOffset.UtcNow };

    private string Token(string scope, string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "consultas",
        Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", scope)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>Registro de proveedores falsos: cada llamada queda anotada y los marcados como caídos devuelven un chequeo de error.</summary>
    private sealed class RegistroFalso(ConcurrentQueue<string> llamadas, ConcurrentDictionary<string, bool> caidos) : IConsultationProviderRegistry
    {
        private static readonly string[] Claves =
        [
            "kyverum_runt", "verifik", "intempo", "kyverum_runt_conductor", "verifik_conductor", "verifik_rnmc", "verifik_rues", "verifik_simit",
        ];

        public IConsultationProvider? Resolve(string providerKey) =>
            Claves.Contains(providerKey, StringComparer.Ordinal) ? new Falso(providerKey, llamadas, caidos) : null;
    }

    private sealed class Falso(string key, ConcurrentQueue<string> llamadas, ConcurrentDictionary<string, bool> caidos) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            llamadas.Enqueue(key);
            var estado = caidos.ContainsKey(key) ? "error" : "ok";
            return Task.FromResult(new ConsultationResult(key, estado == "ok" ? "green" : "red",
                [new ConsultationCheck("soat", "SOAT", estado, key, null)], []));
        }
    }

    private sealed class Jwks(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] { jwk } })) });
        }
    }
}
