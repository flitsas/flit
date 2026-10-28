using System.Text.Json;
using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14044 y #14045 (Feature #14023, Épica #14021, ADR-0075) — la validación de identidad de las partes de una solicitud
/// del portal, detrás de la fachada gRPC y con el servidor OIDC real de los tests. Decisión del PO (2026-10-27): cada
/// solicitud pide una validación NUEVA; nada se reutiliza entre trámites ni vence por días. Requiere la base con las
/// migraciones aplicadas (DDL 152 y 160); el proveedor es el mock del ambiente de pruebas.
/// </summary>
public sealed class PersonaNaturalIdentidadGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";

    [Fact]
    public async Task Solicitar_CreaUnaValidacionNuevaDeEsaSolicitudYParte_EnPendiente_SinVigencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();

        var respuesta = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);

        respuesta.Estado.Should().Be("pendiente");
        respuesta.VigenteHasta.Should().BeNull("el portal no tiene vigencia en días");
        var fila = await FilaAsync(host, respuesta.ValidacionId, ct);
        fila.TenantId.Should().Be(PlatformTenants.PersonaNatural);
        fila.PortalSolicitudRef.Should().Be(solicitud);
        fila.PortalParte.Should().Be("vendedor");
        fila.PersonId.Should().BeNull("el ancla es la solicitud, no la persona del módulo Identidad");
        fila.ProcedureInstanceId.Should().BeNull();
    }

    [Fact]
    public async Task Solicitar_DosVecesLoMismo_DevuelveLaMismaValidacion_SinDuplicarElEnlace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();

        var primera = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "comprador", cedula), cancellationToken: ct);
        var segunda = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "comprador", cedula), cancellationToken: ct);

        segunda.ValidacionId.Should().Be(primera.ValidacionId);
        (await ContarAsync(host, solicitud, ct)).Should().Be(1, "una sola validación en curso por actor y solicitud");
    }

    [Fact]
    public async Task Solicitar_AunqueLaMismaCedulaYaEstaAprobadaEnOtraSolicitud_PideUnaNuevaYNoLaReutiliza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var cedula = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var ayer = await cliente.SolicitarValidacionIdentidadAsync(Pedido(NuevaSolicitud(), "vendedor", cedula), cancellationToken: ct);
        await MarcarAsync(host, ayer.ValidacionId, BiometricEstados.Aprobado, ct);

        var hoy = await cliente.SolicitarValidacionIdentidadAsync(Pedido(NuevaSolicitud(), "vendedor", cedula), cancellationToken: ct);

        hoy.ValidacionId.Should().NotBe(ayer.ValidacionId, "la validación de otro trámite no cuenta");
        hoy.Estado.Should().Be("pendiente");
    }

    [Fact]
    public async Task Solicitar_DosSolicitudesAbiertasALaVezConLaMismaCedula_NoSeBloquean()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var cedula = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";

        var a = await cliente.SolicitarValidacionIdentidadAsync(Pedido(NuevaSolicitud(), "vendedor", cedula), cancellationToken: ct);
        var b = await cliente.SolicitarValidacionIdentidadAsync(Pedido(NuevaSolicitud(), "vendedor", cedula), cancellationToken: ct);

        b.ValidacionId.Should().NotBe(a.ValidacionId);
    }

    [Fact]
    public async Task Solicitar_ConLaAnteriorRechazada_CreaOtraEnLaMismaSolicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();
        var primera = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);
        await MarcarAsync(host, primera.ValidacionId, BiometricEstados.Rechazado, ct);

        var segunda = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);

        segunda.ValidacionId.Should().NotBe(primera.ValidacionId);
        segunda.Estado.Should().Be("pendiente");
    }

    [Fact]
    public async Task Solicitar_ConLaAnteriorAprobadaEnLaMismaSolicitud_DevuelveEsaMisma()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();
        var primera = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);
        await MarcarAsync(host, primera.ValidacionId, BiometricEstados.Aprobado, ct);

        var otra = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);

        otra.ValidacionId.Should().Be(primera.ValidacionId);
        otra.Estado.Should().Be("aprobada");
    }

    [Fact]
    public async Task Solicitar_ConUnNit_RespondePersonaJuridicaNoSoportada_YNoCreaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var solicitud = NuevaSolicitud();
        var pedido = Pedido(solicitud, "comprador", "900123456");
        pedido.Persona.TipoDocumento = "NIT";

        var error = (await ((Func<Task>)(() => cliente.SolicitarValidacionIdentidadAsync(pedido, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;

        PlatformRpcErrors.Reason(error).Should().Be("persona_juridica_no_soportada");
        (await ContarAsync(host, solicitud, ct)).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-correo")]
    [InlineData("a@")]
    public async Task Solicitar_ConCorreoInvalido_RespondeContactoInvalido_YNoCreaNada(string correo)
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var solicitud = NuevaSolicitud();
        var pedido = Pedido(solicitud, "vendedor", "1012345678");
        pedido.Persona.Correo = correo;

        var error = (await ((Func<Task>)(() => cliente.SolicitarValidacionIdentidadAsync(pedido, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;

        error.StatusCode.Should().Be(StatusCode.InvalidArgument);
        (await ContarAsync(host, solicitud, ct)).Should().Be(0);
    }

    [Fact]
    public async Task Solicitar_SinSolicitudONiParte_EsUnaEntradaInvalida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);

        var sinSolicitud = (await ((Func<Task>)(() => cliente.SolicitarValidacionIdentidadAsync(Pedido("", "vendedor", "1"), cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
        var sinParte = (await ((Func<Task>)(() => cliente.SolicitarValidacionIdentidadAsync(Pedido(NuevaSolicitud(), "", "1"), cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;

        sinSolicitud.StatusCode.Should().Be(StatusCode.InvalidArgument);
        sinParte.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task ConOtraEmpresa_LosTresMetodosDeIdentidadRespondenInvalidTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var ajeno = await ClienteAsync(host, ct, Guid.NewGuid());
        var (solicitud, cedula) = Nueva();

        var llamadas = new Func<Task>[]
        {
            () => ajeno.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct).ResponseAsync,
            () => ajeno.ReenviarValidacionIdentidadAsync(new ReenviarValidacionIdentidadRequest { ValidacionId = Guid.NewGuid().ToString() }, cancellationToken: ct).ResponseAsync,
            () => ajeno.ObtenerEstadoValidacionIdentidadAsync(new ObtenerEstadoValidacionIdentidadRequest { ValidacionId = Guid.NewGuid().ToString() }, cancellationToken: ct).ResponseAsync,
        };
        foreach (var llamada in llamadas)
            PlatformRpcErrors.Reason((await llamada.Should().ThrowAsync<RpcException>()).Which).Should().Be("invalid_tenant");
        (await ContarAsync(host, solicitud, ct)).Should().Be(0);
    }

    // ── C2: reenviar y estado ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reenviar_SaleElEnlaceNuevoEnLaMismaFila_YSincronizaElCorreoNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();
        var creada = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "comprador", cedula), cancellationToken: ct);
        var antes = await FilaAsync(host, creada.ValidacionId, ct);

        var respuesta = await cliente.ReenviarValidacionIdentidadAsync(
            new ReenviarValidacionIdentidadRequest { ValidacionId = creada.ValidacionId, Correo = "corregido@ejemplo.test" }, cancellationToken: ct);

        respuesta.Estado.Should().Be("pendiente");
        respuesta.Expira.Should().NotBeNull();
        var despues = await FilaAsync(host, creada.ValidacionId, ct);
        despues.ResendCount.Should().Be(antes.ResendCount + 1);
        despues.TokenHash.Should().NotBe(antes.TokenHash, "el enlace anterior deja de ser válido");
        despues.Email.Should().Be("corregido@ejemplo.test");
        despues.RegisteredEmail.Should().Be(antes.RegisteredEmail, "el correo original es inmutable");
        (await ContarAsync(host, solicitud, ct)).Should().Be(1, "reenviar no crea otra validación");
    }

    [Fact]
    public async Task Reenviar_UnaYaAprobada_NoSeReenvia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();
        var creada = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);
        await MarcarAsync(host, creada.ValidacionId, BiometricEstados.Aprobado, ct);

        var error = (await ((Func<Task>)(() => cliente.ReenviarValidacionIdentidadAsync(new ReenviarValidacionIdentidadRequest { ValidacionId = creada.ValidacionId }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;

        PlatformRpcErrors.Reason(error).Should().Be("identidad_aprobada");
    }

    [Fact]
    public async Task Reenviar_UnaVencida_LaReabreConEnlaceNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var (solicitud, cedula) = Nueva();
        var creada = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);
        await MarcarAsync(host, creada.ValidacionId, BiometricEstados.Expirado, ct);

        var respuesta = await cliente.ReenviarValidacionIdentidadAsync(new ReenviarValidacionIdentidadRequest { ValidacionId = creada.ValidacionId }, cancellationToken: ct);

        respuesta.Estado.Should().Be("pendiente");
    }

    [Fact]
    public async Task ReenviarYEstado_ConUnIdInexistenteODeFlit2_RespondenNoEncontrada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var deFlit2 = await SembrarValidacionDeFlit2Async(host, ct);

        foreach (var id in new[] { Guid.NewGuid().ToString(), deFlit2.ToString() })
        {
            var reenvio = (await ((Func<Task>)(() => cliente.ReenviarValidacionIdentidadAsync(new ReenviarValidacionIdentidadRequest { ValidacionId = id }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
            var estado = (await ((Func<Task>)(() => cliente.ObtenerEstadoValidacionIdentidadAsync(new ObtenerEstadoValidacionIdentidadRequest { ValidacionId = id }, cancellationToken: ct).ResponseAsync)).Should().ThrowAsync<RpcException>()).Which;
            reenvio.StatusCode.Should().Be(StatusCode.NotFound);
            estado.StatusCode.Should().Be(StatusCode.NotFound);
        }
    }

    [Fact]
    public async Task ObtenerEstado_TraduceLosEstadosDeFlit2_AlVocabularioDelPortal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var cliente = await ClienteAsync(host, ct);
        var casos = new (string Estado, string Esperado)[]
        {
            (BiometricEstados.Enviado, "pendiente"),
            (BiometricEstados.EnProceso, "pendiente"),
            (BiometricEstados.Aprobado, "aprobada"),
            (BiometricEstados.Rechazado, "rechazada"),
            (BiometricEstados.Expirado, "vencida"),
        };
        foreach (var (estado, esperado) in casos)
        {
            var (solicitud, cedula) = Nueva();
            var creada = await cliente.SolicitarValidacionIdentidadAsync(Pedido(solicitud, "vendedor", cedula), cancellationToken: ct);
            await MarcarAsync(host, creada.ValidacionId, estado, ct);

            var respuesta = await cliente.ObtenerEstadoValidacionIdentidadAsync(new ObtenerEstadoValidacionIdentidadRequest { ValidacionId = creada.ValidacionId }, cancellationToken: ct);

            respuesta.Estado.Should().Be(esperado, $"el estado {estado} de FLIT 2");
            respuesta.VigenteHasta.Should().BeNull();
        }
    }

    [Fact]
    public void UnEnlaceAbiertoCuyoPlazoPaso_EsVencida_YUnoVigente_Pendiente()
    {
        var ahora = DateTimeOffset.UtcNow;

        PersonaNaturalIdentidadService.EstadoDelPortal(BiometricEstados.Enviado, ahora.AddHours(-1), ahora).Should().Be("vencida");
        PersonaNaturalIdentidadService.EstadoDelPortal(BiometricEstados.EnProceso, ahora.AddHours(1), ahora).Should().Be("pendiente");
        PersonaNaturalIdentidadService.EstadoDelPortal(BiometricEstados.PendienteEnvio, ahora.AddHours(-1), ahora).Should().Be("pendiente");
        PersonaNaturalIdentidadService.EstadoDelPortal(BiometricEstados.Aprobado, ahora.AddHours(-1), ahora).Should().Be("aprobada", "una aprobada no vence por plazo");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static (string Solicitud, string Cedula) Nueva() => (NuevaSolicitud(), $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}");

    private static string NuevaSolicitud() => $"pn-id-{Guid.NewGuid():N}";

    private static SolicitarValidacionIdentidadRequest Pedido(string solicitud, string parte, string cedula) => new()
    {
        SolicitudId = solicitud,
        Parte = parte,
        Persona = new PersonaIdentidad { TipoDocumento = "CC", NumeroDocumento = cedula, Nombre = "Persona de Prueba", Correo = "prueba@ejemplo.test", Celular = "3000000000" },
    };

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
        }).ConEmpresaPersonaNatural();

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, CancellationToken ct, Guid? empresa = null)
    {
        var token = await TokenAsync(host, ct);
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", (empresa ?? PlatformTenants.PersonaNatural).ToString());
            return metadata;
        }));
    }

    private static async Task<string> TokenAsync(WebApplicationFactory<Program> host, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Portal,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<ProcedureInstanceBiometricValidation> FilaAsync(WebApplicationFactory<Program> host, string id, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(v => v.Id == Guid.Parse(id), ct);
    }

    private static async Task<int> ContarAsync(WebApplicationFactory<Program> host, string solicitud, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        return await db.ProcedureInstanceBiometricValidations.AsNoTracking().CountAsync(v => v.PortalSolicitudRef == solicitud, ct);
    }

    /// <summary>Lleva la validación al estado que dejaría el proveedor (aprobada con su vigencia de 30 días, rechazada, vencida…).</summary>
    private static async Task MarcarAsync(WebApplicationFactory<Program> host, string id, string estado, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var validacion = Guid.Parse(id);
        var ahora = DateTimeOffset.UtcNow;
        await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == validacion).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.Status, estado)
            .SetProperty(v => v.ValidatedAt, estado == BiometricEstados.Aprobado ? ahora : (DateTimeOffset?)null)
            .SetProperty(v => v.ValidUntil, estado == BiometricEstados.Aprobado ? ahora.AddDays(30) : (DateTimeOffset?)null), ct);
    }

    /// <summary>Una validación de FLIT 2 (de un usuario de otra empresa, sin ancla de portal): la fachada no debe verla.</summary>
    private static async Task<Guid> SembrarValidacionDeFlit2Async(WebApplicationFactory<Program> host, CancellationToken ct)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var tenant = await db.Tenants.Where(t => t.Id != PlatformTenants.PersonaNatural).OrderBy(t => t.Code).Select(t => t.Id).FirstAsync(ct);
        var persona = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = $"8{Random.Shared.NextInt64(10_000_000, 99_999_999)}",
            FullName = "Persona de FLIT 2",
            Email = "flit2@ejemplo.test",
        };
        var validacion = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            PersonId = persona.Id,
            Name = persona.FullName,
            DocumentType = persona.DocumentType,
            DocumentNumber = persona.DocumentNumber,
            Email = persona.Email,
            RegisteredEmail = persona.Email,
            Status = BiometricEstados.Enviado,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(persona);
        db.Add(validacion);
        await db.SaveChangesAsync(ct);
        return validacion.Id;
    }
}
