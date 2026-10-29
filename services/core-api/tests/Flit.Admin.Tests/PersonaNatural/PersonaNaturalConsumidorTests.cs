using System.Text.Json;
using Flit.Api.Identity;
using Flit.Api.PersonaNatural;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14050 (Feature #14024, Épica #14021) — el consumidor de core-api que crea el trámite de una solicitud radicada en el
/// portal: sin base ni red. Cubre el filtro por empresa, el evento de resultado (creada / rechazada con su motivo), la
/// traducción de los rechazos a motivos en lenguaje claro, el mapeo de partes y datos, el cliente gRPC y el registro apagado
/// por defecto. La creación contra Postgres está en <see cref="PersonaNaturalRadicacionPostgresTests"/>.
/// </summary>
public sealed class PersonaNaturalConsumidorTests
{
    private static readonly Guid Solicitud = Guid.Parse("11111111-2222-4333-8444-555555555555");

    // ── Consumidor ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnTrabajoDeOtraEmpresa_SeIgnora_SinTocarNada()
    {
        var radicador = new RadicadorFalso(ResultadoRadicacionPortal.Nueva(Guid.NewGuid(), "R-1", "entregado"));
        var outbox = new OutboxFalso();

        await Consumidor(radicador, outbox).HandleAsync(Sobre(Guid.NewGuid(), PersonaNaturalEventos.RadicacionSolicitada), Datos(), TestContext.Current.CancellationToken);

        radicador.Llamadas.Should().Be(0);
        outbox.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task UnTramiteCreado_PublicaSolicitudResueltaConElRadicadoYElId()
    {
        var id = Guid.NewGuid();
        var outbox = new OutboxFalso();

        await Consumidor(new RadicadorFalso(ResultadoRadicacionPortal.Nueva(id, "R-77", "entregado")), outbox)
            .HandleAsync(Sobre(PlatformTenants.PersonaNatural, PersonaNaturalEventos.RadicacionSolicitada), Datos(), TestContext.Current.CancellationToken);

        var evento = outbox.Eventos.Should().ContainSingle().Which;
        evento.Tipo.Should().Be(PersonaNaturalEventos.SolicitudResuelta);
        evento.Empresa.Should().Be(PlatformTenants.PersonaNatural);
        var datos = evento.Datos.Should().BeOfType<SolicitudResueltaDatos>().Which;
        datos.SolicitudId.Should().Be(Solicitud);
        datos.Resultado.Should().Be("creada");
        datos.ProcedureInstanceId.Should().Be(id);
        datos.NumeroRadicado.Should().Be("R-77");
        datos.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task UnRechazoDeNegocio_PublicaRechazadaConCodigoYMotivo_SinReintento()
    {
        var outbox = new OutboxFalso();
        var rechazo = ResultadoRadicacionPortal.Rechazo(MotivosDeRechazo.TipoNoDisponible, MotivosDeRechazo.Motivo(MotivosDeRechazo.TipoNoDisponible));

        await Consumidor(new RadicadorFalso(rechazo), outbox)
            .HandleAsync(Sobre(PlatformTenants.PersonaNatural, PersonaNaturalEventos.RadicacionSolicitada), Datos(), TestContext.Current.CancellationToken);

        var datos = outbox.Eventos.Should().ContainSingle().Which.Datos.Should().BeOfType<SolicitudResueltaDatos>().Which;
        datos.Resultado.Should().Be("rechazada");
        datos.Codigo.Should().Be("tipo_no_disponible");
        datos.Motivo.Should().NotBeNullOrWhiteSpace();
        datos.ProcedureInstanceId.Should().BeNull();
    }

    [Fact]
    public async Task UnaSolicitudQueYaNoSePuedeLeer_NoPublicaNada()
    {
        var outbox = new OutboxFalso();

        await Consumidor(new RadicadorFalso(ResultadoRadicacionPortal.SinEfecto), outbox)
            .HandleAsync(Sobre(PlatformTenants.PersonaNatural, PersonaNaturalEventos.RadicacionSolicitada), Datos(), TestContext.Current.CancellationToken);

        outbox.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task UnaFallaTecnica_SePropagaParaQueElSdkReintenteYTerminePorLaDlq()
    {
        var outbox = new OutboxFalso();
        var radicador = new RadicadorFalso(new InvalidOperationException("servicio del portal caído"));

        var acto = () => Consumidor(radicador, outbox).HandleAsync(Sobre(PlatformTenants.PersonaNatural, PersonaNaturalEventos.RadicacionSolicitada), Datos(), TestContext.Current.CancellationToken);

        await acto.Should().ThrowAsync<InvalidOperationException>();
        outbox.Eventos.Should().BeEmpty();
    }

    // ── Motivos ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tipo_no_disponible", "tipo de trámite")]
    [InlineData("organismo_no_habilitado", "organismo")]
    [InlineData("documentos_incompletos", "documentos")]
    [InlineData("identidad_no_aprobada", "identidad")]
    [InlineData("identidad_no_validada", "identidad")]
    public void LosRechazosConocidos_TienenUnMotivoEnLenguajeClaro(string codigo, string palabra) =>
        MotivosDeRechazo.Motivo(codigo).Should().Contain(palabra).And.NotContainAny("Exception", "SQL", "null", codigo);

    [Fact]
    public void UnCodigoDesconocido_CaeEnUnMotivoGenerico_YElCodigoSeLimpia()
    {
        MotivosDeRechazo.Motivo("algo_raro").Should().Contain("No fue posible crear el trámite");
        MotivosDeRechazo.Codigo("Código Raro!! <script>").Should().MatchRegex("^[a-z0-9_.:-]+$");
        MotivosDeRechazo.Codigo("   ").Should().Be(MotivosDeRechazo.Generico);
        MotivosDeRechazo.Codigo(new string('a', 300)).Length.Should().Be(80);
    }

    // ── Mapeo de partes y datos ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnaParteDelPortal_SeMapeaALosActoresDeFlit2()
    {
        var actor = PersonaNaturalCreador.MapearParte(new ParteSolicitudPortal(
            "Vendedor", 2, "cc", " 123456 ", " Ana Pérez ", "ana@correo.test", "3001112233", "Calle 1", Guid.NewGuid(), "aprobada", "Medellin"));

        actor.Rol.Should().Be("vendedor");
        actor.TipoDocumento.Should().Be("CC");
        actor.NumeroDocumento.Should().Be("123456");
        actor.NombreCompleto.Should().Be("Ana Pérez");
        actor.Email.Should().Be("ana@correo.test");
        actor.Telefono.Should().Be("3001112233");
        actor.Direccion.Should().Be("Calle 1");
        actor.Ciudad.Should().Be("Medellin");
        actor.Ordinal.Should().Be(2);
        actor.AutorizaReutilizacionDatos.Should().BeFalse("el ciudadano del portal no autoriza el reúso de sus datos entre trámites");
    }

    [Fact]
    public void LosDatosDelPortal_SeLeenConValorYPrenda_YUnJsonRotoEquivaleASinBloques()
    {
        var datos = DatosSolicitudPortal.Leer("""{"comercial":{"valor":45000000},"prenda":{"tiene":true,"acreedor":"Banco X","nitAcreedor":"900123"},"requisitos":{"transformaciones":[]}}""");

        datos.ValorComercial.Should().Be(45_000_000m);
        datos.Prenda.Should().Be(new PrendaPortal(true, "Banco X", "900123"));
        datos.Observaciones.Should().BeNull();
        DatosSolicitudPortal.Leer("""{"requisitos":{"observaciones":"  El color nuevo es rojo  "}}""").Observaciones.Should().Be("  El color nuevo es rojo  ");
        DatosSolicitudPortal.Leer("{no es json").Should().Be(DatosSolicitudPortal.Vacios);
        DatosSolicitudPortal.Leer(null).Should().Be(DatosSolicitudPortal.Vacios);
        DatosSolicitudPortal.Leer("{}").Prenda.Should().BeNull();
    }

    [Fact]
    public void ElTitularDeLaConsulta_EsElVendedorYSiNoHay_ElComprador()
    {
        var vendedor = Parte("vendedor");
        var comprador = Parte("comprador");
        PersonaNaturalCreador.TitularDeLaConsulta([comprador, vendedor]).Should().BeSameAs(vendedor);
        PersonaNaturalCreador.TitularDeLaConsulta([comprador]).Should().BeSameAs(comprador);
        PersonaNaturalCreador.TitularDeLaConsulta([]).Should().BeNull();
    }

    [Fact]
    public void LaConsultaDelVehiculo_SiembraLaFicha_SinPisarLoEscritoNiElOrganismo()
    {
        var ya = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "plate", "vehicle_color" };
        var campos = PersonaNaturalCreador.CamposDeLaConsulta(
            [
                new HydratedField("vehicle_brand", "TESLA", null),
                new HydratedField("vehicle_color", "ROJO", null),
                new HydratedField("vehicle_fuel", "ELECTRICO", null),
                new HydratedField("transit_office_name", "OT RUNT", null),
                new HydratedField("plate", "XXX123", null),
                new HydratedField("vacio", "  ", null),
            ],
            ya);

        campos.Select(c => c.FieldKey).Should().Equal("vehicle_brand", "vehicle_color_runt", "vehicle_fuel", "vehicle_fuel_runt");
        campos.Should().NotContain(c => c.FieldKey == "vehicle_color");
        campos.Should().NotContain(c => c.FieldKey.StartsWith("transit_office_", StringComparison.Ordinal));
    }

    private static ParteSolicitudPortal Parte(string rol) =>
        new(rol, 1, "CC", "123", "Ana", "ana@correo.test", "3001112233", "Calle 1", null, "sin_solicitar");

    [Fact]
    public void LaRespuestaDelRpc_SeMapeaALaSolicitudDelPortal()
    {
        var respuesta = new ObtenerSolicitudParaRadicarResponse
        {
            SolicitudId = Solicitud.ToString(), CodigoPublico = "PN-2026-000001", TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 3, DefinicionHuella = "abc", OrganismoId = Guid.NewGuid().ToString(), OrganismoNombre = "Tránsito",
            Placa = "ABC123", Vin = "", DatosJson = "{}", IdempotencyKey = "k", CorrelationId = Guid.NewGuid().ToString(),
        };
        respuesta.FieldValues["clase"] = "AUTOMOVIL";
        respuesta.Partes.Add(new ParteSolicitud { Rol = "comprador", Ordinal = 1, TipoDocumento = "CC", NumeroDocumento = "9", Nombre = "N", Correo = "n@x.co", ValidacionId = Guid.NewGuid().ToString(), ValidacionEstado = "aprobada" });
        respuesta.Adjuntos.Add(new AdjuntoSolicitud { Id = Guid.NewGuid().ToString(), Tipo = "cedulas", Referencia = "s3://x", Sha256 = "ff", TamanoBytes = 10, MimeType = "application/pdf", NombreArchivo = "a.pdf" });

        var s = GrpcPersonaNaturalPortalClient.Mapear(respuesta);

        s.SolicitudId.Should().Be(Solicitud);
        s.Placa.Should().Be("ABC123");
        s.Vin.Should().BeNull("el RPC manda vacío lo que el tipo no pide");
        s.Campos.Should().ContainKey("clase");
        s.Partes.Should().ContainSingle().Which.ValidacionEstado.Should().Be("aprobada");
        s.Adjuntos.Should().ContainSingle().Which.Referencia.Should().Be("s3://x");
    }

    // ── Cliente gRPC ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElCliente_TraduceNoEncontradaYNoRadicable_YPropagaCualquierOtraFalla()
    {
        var ct = TestContext.Current.CancellationToken;

        (await Cliente(Falla(StatusCode.NotFound, LecturaSolicitudPortal.NoEncontrada)).ObtenerSolicitudParaRadicarAsync(Solicitud, PlatformTenants.PersonaNatural, ct))
            .Razon.Should().Be(LecturaSolicitudPortal.NoEncontrada);
        (await Cliente(Falla(StatusCode.FailedPrecondition, LecturaSolicitudPortal.NoRadicable)).ObtenerSolicitudParaRadicarAsync(Solicitud, PlatformTenants.PersonaNatural, ct))
            .Razon.Should().Be(LecturaSolicitudPortal.NoRadicable);

        var caido = () => Cliente(Falla(StatusCode.Unavailable, "x")).ObtenerSolicitudParaRadicarAsync(Solicitud, PlatformTenants.PersonaNatural, ct);
        await caido.Should().ThrowAsync<RpcException>();
        var otraRazon = () => Cliente(Falla(StatusCode.NotFound, "otra_razon")).ObtenerSolicitudParaRadicarAsync(Solicitud, PlatformTenants.PersonaNatural, ct);
        await otraRazon.Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task ElCliente_PideLaSolicitudConLaEmpresaEnLaMetadata()
    {
        var invoker = new InvokerFalso(new ObtenerSolicitudParaRadicarResponse
        {
            SolicitudId = Solicitud.ToString(), OrganismoId = Guid.NewGuid().ToString(), CorrelationId = Guid.NewGuid().ToString(),
        });

        var lectura = await new GrpcPersonaNaturalPortalClient(new RadicacionService.RadicacionServiceClient(invoker))
            .ObtenerSolicitudParaRadicarAsync(Solicitud, PlatformTenants.PersonaNatural, TestContext.Current.CancellationToken);

        lectura.Solicitud.Should().NotBeNull();
        invoker.Peticion!.SolicitudId.Should().Be(Solicitud.ToString());
        invoker.Cabeceras!.GetValue(PlatformServiceCallInterceptor.TenantMetadata).Should().Be(PlatformTenants.PersonaNatural.ToString());
    }

    // ── Registro: apagado por defecto ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApagadoPorDefecto_NoRegistraNada()
    {
        var services = new ServiceCollection();

        services.AddPersonaNaturalConsumidor(Config([]));

        services.Should().BeEmpty();
    }

    [Fact]
    public void EncendidoSinBusOSinDireccion_FallaAlArrancar_ConUnMensajeClaro()
    {
        var sinBus = () => new ServiceCollection().AddPersonaNaturalConsumidor(Config(new() { [PersonaNaturalConsumidorExtensions.ClaveHabilitado] = "true" }));
        sinBus.Should().Throw<InvalidOperationException>().WithMessage("*Tramites:Bus:Habilitado*");

        var sinDireccion = () => new ServiceCollection().AddPersonaNaturalConsumidor(Config(new()
        {
            [PersonaNaturalConsumidorExtensions.ClaveHabilitado] = "true",
            ["Tramites:Bus:Habilitado"] = "true",
        }));
        sinDireccion.Should().Throw<InvalidOperationException>().WithMessage("*PersonaNatural:Portal:Address*");
    }

    [Fact]
    public void Encendido_RegistraElRadicadorYElConsumidorDeRadicacion()
    {
        var services = new ServiceCollection();

        services.AddPersonaNaturalConsumidor(Config(new()
        {
            [PersonaNaturalConsumidorExtensions.ClaveHabilitado] = "true",
            ["Tramites:Bus:Habilitado"] = "true",
            [PersonaNaturalConsumidorExtensions.ClaveDireccion] = "http://core-personanatural:8090",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://localhost:5672/flit",
            ["Platform:ServiceClient:ClientId"] = "svc-tramites",
            ["Platform:ServiceClient:ClientSecret"] = "x",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://localhost:4002/connect/token",
        }));

        services.Should().Contain(d => d.ServiceType == typeof(IPersonaNaturalRadicador));
        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ElScopeDeLecturaDelPortal_TieneLaAudienciaPersonaNatural()
    {
        OidcDefaults.ServiceScopeAudiences[PersonaNaturalConsumidorExtensions.ScopeLectura].Should().Be(ServiceAudiences.PersonaNatural);
        ServiceAudiences.PersonaNatural.Should().Be("personanatural");
        OidcDefaults.AudiencesForServiceScopes([PersonaNaturalConsumidorExtensions.ScopeLectura]).Should().Equal("personanatural");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static RadicacionSolicitadaConsumer Consumidor(IPersonaNaturalRadicador radicador, OutboxFalso outbox) =>
        new(radicador, outbox, TimeProvider.System, NullLogger<RadicacionSolicitadaConsumer>.Instance);

    private static RadicacionSolicitadaDatos Datos() =>
        new(Solicitud, "PN-2026-000001", "TRASPASO_STANDARD", 1, "huella", Guid.NewGuid(), [], null, "llave", Guid.NewGuid());

    private static EventEnvelope Sobre(Guid empresa, string tipo) =>
        new(Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, empresa, "personanatural", "corr", JsonSerializer.SerializeToElement(new { }));

    private static IConfiguration Config(Dictionary<string, string?> valores) => new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static GrpcPersonaNaturalPortalClient Cliente(RpcException ex) => new(new RadicacionService.RadicacionServiceClient(new InvokerFalso(ex)));

    private static RpcException Falla(StatusCode estado, string razon) => PlatformRpcErrors.ToRpcException(razon, "mensaje fijo", estado);

    private sealed class RadicadorFalso : IPersonaNaturalRadicador
    {
        private readonly ResultadoRadicacionPortal? _resultado;
        private readonly Exception? _falla;

        public RadicadorFalso(ResultadoRadicacionPortal resultado) => _resultado = resultado;

        public RadicadorFalso(Exception falla) => _falla = falla;

        public int Llamadas { get; private set; }

        public Task<ResultadoRadicacionPortal> ProcesarAsync(RadicacionSolicitadaDatos evento, CancellationToken ct)
        {
            Llamadas++;
            return _falla is not null ? Task.FromException<ResultadoRadicacionPortal>(_falla) : Task.FromResult(_resultado!);
        }
    }

    /// <summary>Invoker de unidad: responde con un mensaje o lanza el <see cref="RpcException"/> dado, y recuerda lo que recibió.</summary>
    internal sealed class InvokerFalso : CallInvoker
    {
        private readonly object? _respuesta;
        private readonly RpcException? _falla;

        public InvokerFalso(object respuesta) => _respuesta = respuesta;

        public InvokerFalso(RpcException falla) => _falla = falla;

        public ObtenerSolicitudParaRadicarRequest? Peticion { get; private set; }

        public Metadata? Cabeceras { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Peticion = request as ObtenerSolicitudParaRadicarRequest;
            Cabeceras = options.Headers;
            var tarea = _falla is not null ? Task.FromException<TResponse>(_falla) : Task.FromResult((TResponse)_respuesta!);
            return new AsyncUnaryCall<TResponse>(tarea, Task.FromResult(new Metadata()), () => _falla?.Status ?? Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}

/// <summary>Outbox de unidad: recuerda lo que se encola, sin base ni broker.</summary>
internal sealed class OutboxFalso : IPlatformOutbox
{
    public List<(string Tipo, int Version, Guid Empresa, object Datos)> Eventos { get; } = [];

    public EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data)
    {
        Eventos.Add((type, version, tenantId, data!));
        return new EventEnvelope(Guid.CreateVersion7(), type, version, DateTimeOffset.UtcNow, tenantId, "tramites", "corr", JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));
    }

    public EventEnvelope EnqueueJob<TData>(string type, int version, Guid tenantId, TData data) => Enqueue(type, version, tenantId, data);
}
