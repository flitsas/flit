using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13742 (F01-H2), escenarios 1 a 4 — <c>GET /tipos-tramite</c>: proxy del rpc <c>ListarTiposTramite</c> de Trámites
/// (cliente gRPC con el token de svc-diagnostico) con el permiso <c>diagnostico.diagnosticos.crear</c>. Filtra por código,
/// <c>fuentesObligatorias</c> vacía en D1, reintenta 2 veces ante Unavailable/DeadlineExceeded, guarda 5 minutos la última
/// respuesta correcta y, sin caché vigente, responde 503 con <c>Retry-After: 30</c>. El rpc es el doble
/// <see cref="Catalogos.CatalogosFalso"/> del arnés: no depende de core-api ni de la base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TiposTramiteEndpointsTests
/// </code>
/// </example>
public sealed class TiposTramiteEndpointsTests
{
    private const string Ruta = Program.Prefijo + "/tipos-tramite";

    // Escenario 1 — solo TRASPASO, TRASPASO UNILATERAL y TRANSFERENCIA DE DOMINIO, filtrados por código.
    [Fact]
    public async Task Devuelve_solo_los_tipos_de_la_etapa_filtrados_por_codigo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var tipos = JsonDocument.Parse(cuerpo).RootElement.GetProperty("tipos").EnumerateArray().ToList();
        tipos.Select(t => t.GetProperty("codigo").GetString())
            .Should().Equal("TRASPASO_STANDARD", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRASPASO_UNILATERAL");
        tipos.Select(t => t.GetProperty("nombre").GetString())
            .Should().Equal("TRASPASO", "TRANSFERENCIA DE DOMINIO", "TRASPASO UNILATERAL");
        cuerpo.Should().NotContain("TRASPASO_INDETERMINADA", "el mismo nombre con otro código no se ofrece");
        cuerpo.Should().NotContain("MATRICULA_INICIAL");
    }

    // Escenario 1 — sin diagnostico.diagnosticos.crear: 403.
    [Fact]
    public async Task Sin_permiso_de_crear_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(api.Token(permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionVer])).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        api.Catalogos.Llamadas.Should().Be(0, "sin permiso no se consulta a Trámites");
    }

    // Escenario 2 — fuentesObligatorias llega vacía hasta D6.
    [Fact]
    public async Task Cada_tipo_trae_fuentes_obligatorias_vacia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var tipos = JsonDocument.Parse(cuerpo).RootElement.GetProperty("tipos").EnumerateArray().ToList();
        tipos.Should().NotBeEmpty();
        tipos.Should().OnlyContain(t => t.GetProperty("fuentesObligatorias").ValueKind == JsonValueKind.Array
                                        && t.GetProperty("fuentesObligatorias").GetArrayLength() == 0);
    }

    // Escenario 3 — falla dos veces con Unavailable y responde a la tercera: 200.
    [Fact]
    public async Task Dos_fallas_transitorias_y_responde_a_la_tercera_devuelve_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Catalogos.FallarLasPrimeras(2, StatusCode.Unavailable);

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        JsonDocument.Parse(cuerpo).RootElement.GetProperty("tipos").GetArrayLength().Should().Be(3);
        api.Catalogos.Llamadas.Should().Be(3, "una llamada y dos reintentos");
    }

    // Escenario 3 — DeadlineExceeded también es transitorio.
    [Fact]
    public async Task DeadlineExceeded_se_reintenta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Catalogos.FallarLasPrimeras(1, StatusCode.DeadlineExceeded);

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        api.Catalogos.Llamadas.Should().Be(2);
    }

    // Escenario 4 — no responde tras los reintentos y no hay caché: 503 con Retry-After, sin lista fija.
    [Fact]
    public async Task Catalogo_caido_sin_cache_responde_503_con_retry_after_y_sin_lista()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Catalogos.FallaSiempre = StatusCode.Unavailable;

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, cuerpo);
        respuesta.Headers.RetryAfter.Should().NotBeNull();
        respuesta.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
        var json = JsonDocument.Parse(cuerpo).RootElement;
        json.GetProperty("code").GetString().Should().Be(ReglaException.CodigoCatalogoNoDisponible);
        json.TryGetProperty("tipos", out _).Should().BeFalse("no se sirve una lista fija");
        cuerpo.Should().NotContain("TRASPASO_STANDARD");
        api.Catalogos.Llamadas.Should().Be(3, "una llamada y dos reintentos");
    }

    // Escenario 4 — PermissionDenied es de configuración: 503 sin reintentar.
    [Fact]
    public async Task PermissionDenied_responde_503_sin_reintentar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.Catalogos.FallaSiempre = StatusCode.PermissionDenied;

        var respuesta = await api.Cliente(TokenCrear(api)).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        respuesta.Headers.RetryAfter.Should().NotBeNull();
        respuesta.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
        api.Catalogos.Llamadas.Should().Be(1, "PermissionDenied no se reintenta");
    }

    // Escenario 4 — con caché vigente no se llama al rpc, aunque Trámites esté caído.
    [Fact]
    public async Task Con_cache_vigente_no_llama_al_rpc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var cliente = api.Cliente(TokenCrear(api));

        var primera = await cliente.GetAsync(Ruta, ct);
        api.Catalogos.FallaSiempre = StatusCode.Unavailable;
        var segunda = await cliente.GetAsync(Ruta, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, "la copia de la última respuesta correcta sigue vigente");
        (await segunda.Content.ReadAsStringAsync(ct)).Should().Be(await primera.Content.ReadAsStringAsync(ct));
        api.Catalogos.Llamadas.Should().Be(1, "con caché vigente no se llama al rpc");
    }

    private static string TokenCrear(ApiDePrueba api) => api.Token(permisos: [Permisos.DiagnosticosCrear]);
}
