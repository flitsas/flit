using System.Security.Claims;
using System.Text.Encodings.Web;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Esquema de autenticación de prueba: arma el principal desde cabeceras <c>x-test-*</c> (sub, aud, scope) para ejercitar la
/// autorización real del gRPC (interceptor del SDK + cliente) sin firmar JWT. Sin <c>x-test-sub</c> no autentica.
/// </summary>
internal sealed class EsquemaDeAutenticacionDePrueba(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Nombre = "Prueba";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("x-test-sub", out var sub))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", sub.ToString()) };
        claims.AddRange(Request.Headers["x-test-aud"].Select(a => new Claim("aud", a!)));
        if (Request.Headers.TryGetValue("x-test-scope", out var scope))
            claims.Add(new Claim("scope", scope.ToString()));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Nombre)), Nombre);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// HU #14049 — <c>RadicacionService.ObtenerSolicitudParaRadicar</c>: lectura por pull de lo que core-api necesita para crear
/// el trámite. Sobre el host real (puerto gRPC interno, interceptor del SDK) y una base efímera; solo cambia la forma de
/// autenticar (cabeceras de prueba en vez de un JWT firmado). Sin motor de Postgres se omite en local y falla en CI.
/// </summary>
public sealed class RadicacionGrpcTests : IAsyncLifetime
{
    private const string Documento = "7001234567";
    private const string Nombre = "Persona de Prueba";
    private const string Correo = "vendedor.secreto@example.org";
    private const string Celular = "3001234567";
    private const string Direccion = "Calle 1 # 2-3 Secreta";
    private const string Archivo = "cedula-de-persona-de-prueba.pdf";

    private PortalHost _host = null!;
    private bool _iniciado;
    private int _secuencia;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync()
    {
        _host = await PortalHost.IniciarAsync("pngrpc", (servicios, _) =>
        {
            servicios.AddAuthentication().AddScheme<AuthenticationSchemeOptions, EsquemaDeAutenticacionDePrueba>(EsquemaDeAutenticacionDePrueba.Nombre, _ => { });
            servicios.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = EsquemaDeAutenticacionDePrueba.Nombre;
                o.DefaultChallengeScheme = EsquemaDeAutenticacionDePrueba.Nombre;
            });
        });
        _iniciado = true;
    }

    // ── Autorización ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinToken_EsUnauthenticated_YNoSeAudita()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var ex = await Fallo(id, Cabeceras(sub: null));

        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task OtroClienteConElScope_EsPermissionDenied()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var ex = await Fallo(id, Cabeceras(sub: "svc-notificaciones"));

        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task ElClienteCorrectoSinElScope_EsPermissionDenied()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var sinScope = await Fallo(id, Cabeceras(scope: null));
        var otroScope = await Fallo(id, Cabeceras(scope: "platform.consultas platform.identidad.read"));

        sinScope.StatusCode.Should().Be(StatusCode.PermissionDenied);
        otroScope.StatusCode.Should().Be(StatusCode.PermissionDenied);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task ConOtraAudiencia_EsPermissionDenied()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var ex = await Fallo(id, Cabeceras(aud: "tramites"));

        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task UnTokenDeUsuario_EsUnauthenticated()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var ex = await Fallo(id, Cabeceras(sub: Guid.NewGuid().ToString()));

        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task SinEmpresa_EsInvalidArgument_YConOtraEmpresa_NotFound()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var sinEmpresa = await Fallo(id, Cabeceras(tenant: null));
        var otraEmpresa = await Fallo(id, Cabeceras(tenant: Guid.NewGuid().ToString()));

        sinEmpresa.StatusCode.Should().Be(StatusCode.InvalidArgument);
        otraEmpresa.StatusCode.Should().Be(StatusCode.NotFound, "una solicitud de otra empresa no existe para quien llama");
        PlatformRpcErrors.Reason(otraEmpresa).Should().Be("solicitud_no_encontrada");
        await SinLecturasAuditadasAsync();
    }

    // ── Estado y existencia ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EstadoSolicitud.Borrador)]
    [InlineData(EstadoSolicitud.PendienteValidacion)]
    [InlineData(EstadoSolicitud.Creada)]
    [InlineData(EstadoSolicitud.RechazadaCreacion)]
    [InlineData(EstadoSolicitud.Cancelada)]
    public async Task UnaSolicitudQueNoEstaEnCola_EsFailedPrecondition_SinLeerNiAuditarDatosPersonales(string estado)
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(estado);

        var ex = await Fallo(id, Cabeceras());

        ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        PlatformRpcErrors.Reason(ex).Should().Be("solicitud_no_radicable");
        SinDatosPersonales(ex.Status.Detail);
        await SinLecturasAuditadasAsync();
    }

    [Fact]
    public async Task UnaSolicitudInexistenteOUnIdInvalido_NoLeeNada()
    {
        await IniciarAsync();

        var inexistente = await Fallo(Guid.NewGuid().ToString(), Cabeceras());
        var invalido = await Fallo("no-es-un-guid", Cabeceras());

        inexistente.StatusCode.Should().Be(StatusCode.NotFound);
        PlatformRpcErrors.Reason(inexistente).Should().Be("solicitud_no_encontrada");
        invalido.StatusCode.Should().Be(StatusCode.InvalidArgument);
        await SinLecturasAuditadasAsync();
    }

    // ── Contenido, auditoría y logs ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnCola_DevuelveLoQueCoreApiNecesitaParaCrearElTramite()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);

        var r = await Llamar(id, Cabeceras());

        r.SolicitudId.Should().Be(id.ToString());
        r.CodigoPublico.Should().Be("PN-2026-000001");
        r.TipoTramiteCodigo.Should().Be("TRASPASO_STANDARD");
        r.DefinicionVersion.Should().Be(1);
        r.DefinicionHuella.Should().Be("h1");
        r.OrganismoId.Should().Be(PortalHost.Organismo);
        r.OrganismoNombre.Should().Be("Organismo de Prueba");
        r.Placa.Should().Be("ABC123");
        r.Vin.Should().BeEmpty();
        r.FieldValues.Should().ContainKey("km").WhoseValue.Should().Be("85000");
        r.DatosJson.Should().Contain("45000000");
        r.IdempotencyKey.Should().Be("k-1");
        r.CorrelationId.Should().NotBeNullOrEmpty();

        r.Partes.Should().HaveCount(2);
        var vendedor = r.Partes.Single(p => p.Rol == "vendedor");
        vendedor.Ordinal.Should().Be(1);
        vendedor.TipoDocumento.Should().Be("CC");
        vendedor.NumeroDocumento.Should().Be(Documento);
        vendedor.Nombre.Should().Be(Nombre);
        vendedor.Correo.Should().Be(Correo);
        vendedor.Celular.Should().Be(Celular);
        vendedor.Direccion.Should().Be(Direccion);
        vendedor.ValidacionId.Should().NotBeNullOrEmpty();
        vendedor.ValidacionEstado.Should().Be("aprobada");
        r.Partes.Single(p => p.Rol == "comprador").NumeroDocumento.Should().Be("7009876543");

        r.Adjuntos.Should().HaveCount(2);
        var comprobante = r.Adjuntos.Single(a => a.Tipo == "comprobante_pago");
        comprobante.Referencia.Should().Be("ref-pago");
        comprobante.Sha256.Should().Be(new string('a', 64));
        comprobante.TamanoBytes.Should().Be(1234);
        comprobante.MimeType.Should().Be("image/png");
        r.ComprobantePagoId.Should().Be(comprobante.Id);
        r.Adjuntos.Single(a => a.Tipo == "compraventa").NombreArchivo.Should().Be(Archivo);
    }

    [Fact]
    public async Task CadaLecturaQuedaAuditada_ConActorAccionSolicitudYHora_SinLosDatos()
    {
        await IniciarAsync();
        var id = await CrearSolicitudAsync(EstadoSolicitud.EnCola);
        _host.Reloj.Avanzar(TimeSpan.FromMinutes(5));

        await Llamar(id, Cabeceras());
        await Llamar(id, Cabeceras());

        await using var db = _host.NuevaDb();
        var filas = await db.Set<AccesoPiiLog>().Where(a => a.Accion == "lectura_para_radicar").ToListAsync(TestContext.Current.CancellationToken);
        filas.Should().HaveCount(2, "cada llamada deja su constancia");
        filas.Should().OnlyContain(a => a.Actor == "core-api" && a.SolicitudId == id && a.OcurrioEn == _host.Reloj.GetUtcNow());
        foreach (var fila in filas)
            SinDatosPersonales($"{fila.Actor}|{fila.Accion}|{fila.SolicitudId}");
    }

    [Fact]
    public async Task LeerParaRadicar_NoDejaDatosPersonalesEnLosLogs_NiEnLosErrores()
    {
        await IniciarAsync();
        var enCola = await CrearSolicitudAsync(EstadoSolicitud.EnCola);
        var borrador = await CrearSolicitudAsync(EstadoSolicitud.Borrador);

        await Llamar(enCola, Cabeceras());
        var error = await Fallo(borrador, Cabeceras());
        var ajeno = await Fallo(enCola.ToString(), Cabeceras(tenant: Guid.NewGuid().ToString()));

        SinDatosPersonales(error.Message + error.Status.Detail + ajeno.Message + ajeno.Status.Detail);
        lock (_host.Logs.Mensajes)
            _host.Logs.Mensajes.Should().NotContain(m => m.Contains(Documento) || m.Contains(Nombre) || m.Contains("secreto") || m.Contains(Celular) || m.Contains(Archivo));
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void SinDatosPersonales(string texto) =>
        texto.Should().NotContain(Documento).And.NotContain(Nombre).And.NotContain("secreto").And.NotContain(Celular).And.NotContain(Archivo);

    private async Task SinLecturasAuditadasAsync()
    {
        await using var db = _host.NuevaDb();
        (await db.Set<AccesoPiiLog>().CountAsync(a => a.Accion == "lectura_para_radicar", TestContext.Current.CancellationToken))
            .Should().Be(0, "una llamada denegada no lee datos personales, así que no deja constancia de lectura");
    }

    /// <summary>Cabeceras del token de prueba: por defecto el cliente, la audiencia, el scope y la empresa correctos.</summary>
    private static Metadata Cabeceras(string? sub = "svc-tramites", string? aud = "personanatural", string? scope = RadicacionGrpcService.Scope, string? tenant = "")
    {
        var m = new Metadata();
        if (sub is not null)
            m.Add("x-test-sub", sub);
        if (aud is not null)
            m.Add("x-test-aud", aud);
        if (scope is not null)
            m.Add("x-test-scope", scope);
        var empresa = tenant == string.Empty ? PlatformTenants.PersonaNatural.ToString() : tenant;
        if (empresa is not null)
            m.Add("x-flit-tenant-id", empresa);
        return m;
    }

    private Flit.PersonaNatural.Grpc.V1.RadicacionService.RadicacionServiceClient Cliente()
    {
        var canal = GrpcChannel.ForAddress("http://localhost:5995", new GrpcChannelOptions { HttpHandler = _host.App.GetTestServer().CreateHandler() });
        return new Flit.PersonaNatural.Grpc.V1.RadicacionService.RadicacionServiceClient(canal);
    }

    private async Task<ObtenerSolicitudParaRadicarResponse> Llamar(Guid id, Metadata cabeceras) =>
        await Cliente().ObtenerSolicitudParaRadicarAsync(new ObtenerSolicitudParaRadicarRequest { SolicitudId = id.ToString() }, cabeceras, cancellationToken: TestContext.Current.CancellationToken);

    private async Task<RpcException> Fallo(Guid id, Metadata cabeceras) => await Fallo(id.ToString(), cabeceras);

    private async Task<RpcException> Fallo(string id, Metadata cabeceras)
    {
        var llamada = async () => await Cliente().ObtenerSolicitudParaRadicarAsync(new ObtenerSolicitudParaRadicarRequest { SolicitudId = id }, cabeceras, cancellationToken: TestContext.Current.CancellationToken);
        return (await llamada.Should().ThrowAsync<RpcException>()).Which;
    }

    /// <summary>Una solicitud completa en el estado dado, con las partes cifradas por <see cref="PersonaService"/> y dos adjuntos.</summary>
    private async Task<Guid> CrearSolicitudAsync(string estado)
    {
        var id = Guid.CreateVersion7();
        var ahora = _host.Reloj.GetUtcNow();
        var codigo = $"PN-2026-{++_secuencia:D6}";

        await using (var db = _host.NuevaDb())
        {
            db.Add(new SolicitudTramite
            {
                Id = id,
                TenantId = PlatformTenants.PersonaNatural,
                CodigoPublico = codigo,
                TipoTramiteCodigo = "TRASPASO_STANDARD",
                DefinicionVersion = 1,
                DefinicionSnapshot = DefinicionesService.Serializar(DefinicionesDePrueba.Traspaso()),
                OrganismoId = Guid.Parse(PortalHost.Organismo),
                OrganismoNombre = "Organismo de Prueba",
                Placa = "ABC123",
                Estado = estado,
                CedulaSolicitanteHash = new byte[32],
                FieldValues = "{\"km\":\"85000\"}",
                Datos = "{\"comercial\":{\"valor\":45000000}}",
                IdempotencyKey = $"k-{_secuencia}",
                CorrelationId = Guid.CreateVersion7(),
                CreatedAt = ahora,
                UpdatedAt = ahora,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _host.EnScopeAsync(async sp =>
        {
            var personas = sp.GetRequiredService<PersonaService>();
            await personas.GuardarAsync(id, new DatosPersona("vendedor", 1, "CC", Documento, Nombre, Correo, Celular, Direccion), TestContext.Current.CancellationToken);
            await personas.GuardarAsync(id, new DatosPersona("comprador", 1, "CC", "7009876543", Nombre, "comprador@example.org", "3009876543", null), TestContext.Current.CancellationToken);
            return 0;
        });

        await using (var db = _host.NuevaDb())
        {
            await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.ValidacionEstado, "aprobada").SetProperty(p => p.ValidacionId, Guid.NewGuid()), TestContext.Current.CancellationToken);
            db.Add(new Adjunto { Id = Guid.CreateVersion7(), SolicitudId = id, Tipo = "compraventa", NombreArchivo = Archivo, MimeType = "application/pdf", TamanoBytes = 99, Sha256 = new string('b', 64), StoragePath = "ref-compraventa", CreatedAt = ahora });
            db.Add(new Adjunto { Id = Guid.CreateVersion7(), SolicitudId = id, Tipo = "comprobante_pago", NombreArchivo = "pago.png", MimeType = "image/png", TamanoBytes = 1234, Sha256 = new string('a', 64), StoragePath = "ref-pago", CreatedAt = ahora.AddSeconds(1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return id;
    }
}
