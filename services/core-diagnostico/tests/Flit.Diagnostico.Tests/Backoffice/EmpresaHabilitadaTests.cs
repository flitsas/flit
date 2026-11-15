using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Backoffice;

/// <summary>
/// HU #14579, BE-0 (SP-3, contrato-api §0) — <c>.SoloEmpresaHabilitada()</c> corre antes de <c>ConEmpresaExplicita</c> en las rutas del
/// backoffice con <c>?empresa=</c>: una compañía sin Diagnóstico activo y un uuid inexistente responden el mismo 404
/// <c>EMPRESA_NO_ENCONTRADA</c> sin fila en <c>lecturas_backoffice</c>; Identidad caída responde 503 <c>IDENTIDAD_NO_DISPONIBLE</c> con
/// <c>Retry-After</c>, nunca «leer igual». La ruta es una sonda (las reales las mapean BE-1 y BE-4 con el mismo filtro). Postgres real
/// para contar la auditoría. Empresas ficticias (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EmpresaHabilitadaTests
/// </code>
/// </example>
public sealed class EmpresaHabilitadaTests
{
    private const string Sonda = Program.Prefijo + BackofficeEndpoints.Grupo + "/prueba-be0/empresa-habilitada";

    // SP-3 (a), (b) y (c) con la lista de Identidad: apagada y aleatoria dan el mismo 404 sin auditar; la habilitada, 200 y una fila.
    [Fact]
    public async Task BE0_empresa_apagada_o_inexistente_404_identico_sin_auditoria_y_habilitada_200_con_una_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        var habilitada = Guid.NewGuid();
        var apagada = Guid.NewGuid();
        var identidad = new EmpresasFijas([new EmpresaPlataforma(habilitada, "Renting Ficticio S.A.S.", "900000001")]);
        await using var api = await ApiDePrueba.ConBaseAsync(mapear: MapearSonda, configurar: b => b.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Productos.Encender(apagada);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(TokenBackoffice(api, flit));

        var deApagada = await cliente.GetAsync($"{Sonda}?empresa={apagada}", ct);
        var deInexistente = await cliente.GetAsync($"{Sonda}?empresa={Guid.NewGuid()}", ct);

        deApagada.StatusCode.Should().Be(HttpStatusCode.NotFound, await deApagada.Content.ReadAsStringAsync(ct));
        deInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cuerpoApagada = await deApagada.Content.ReadAsStringAsync(ct);
        cuerpoApagada.Should().Be(await deInexistente.Content.ReadAsStringAsync(ct), "no se distingue «existe pero apagada» de «no existe»");
        JsonElement.Parse(cuerpoApagada).GetProperty("code").GetString().Should().Be(EmpresaHabilitada.CodigoNoEncontrada);
        cuerpoApagada.Should().NotContain(apagada.ToString());
        (await Auditoria(api, flit, ct)).Should().BeEmpty("SP-3: sin fila de auditoría para una compañía no habilitada");

        var deHabilitada = await cliente.GetAsync($"{Sonda}?empresa={habilitada}", ct);

        deHabilitada.StatusCode.Should().Be(HttpStatusCode.OK, await deHabilitada.Content.ReadAsStringAsync(ct));
        var cuerpo = await deHabilitada.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("empresa").GetGuid().Should().Be(habilitada);
        cuerpo.GetProperty("nombre").GetString().Should().Be("Renting Ficticio S.A.S.", "el nombre sale de Identidad para el ámbito de E3");
        (await Auditoria(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Equal(habilitada);
    }

    // Sin empresa: el filtro no responde por ConEmpresaExplicita, que sigue dando 400 EMPRESA_REQUERIDA sin preguntar a Identidad.
    [Fact]
    public async Task BE0_sin_empresa_sigue_respondiendo_400_EMPRESA_REQUERIDA_sin_preguntar_a_Identidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFijas([]);
        await using var api = await ApiDePrueba.SinBaseAsync(mapear: MapearSonda, configurar: b => b.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        using var cliente = api.Cliente(TokenBackoffice(api, Guid.NewGuid()));

        foreach (var url in new[] { Sonda, $"{Sonda}?empresa=no-es-uuid", $"{Sonda}?empresa={Guid.NewGuid()}&empresa={Guid.NewGuid()}" })
        {
            var respuesta = await cliente.GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(EmpresaExplicita.CodigoRequerida);
        }

        identidad.Llamadas.Should().Be(0);
    }

    // SP-3 — Identidad no responde: 503 IDENTIDAD_NO_DISPONIBLE con Retry-After, sin llegar a ConEmpresaExplicita (la base es
    // inalcanzable: si el filtro dejara pasar, la auditoría fallaría con 500).
    [Fact]
    public async Task BE0_Identidad_caida_responde_503_IDENTIDAD_NO_DISPONIBLE_con_Retry_After()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFijas([]) { Falla = new RpcException(new Status(StatusCode.Unavailable, "falla de prueba")) };
        await using var api = await ApiDePrueba.SinBaseAsync(mapear: MapearSonda, configurar: b => b.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid())).GetAsync($"{Sonda}?empresa={Guid.NewGuid()}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("IDENTIDAD_NO_DISPONIBLE");
        respuesta.Headers.RetryAfter.Should().NotBeNull();
    }

    // Sin Identidad configurada (local): decide la suscripción de la compañía (fail-closed); apagada, 404 sin auditar.
    [Fact]
    public async Task BE0_sin_Identidad_configurada_decide_la_suscripcion_y_apagada_responde_404_sin_auditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(mapear: MapearSonda);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var apagada = Guid.NewGuid();
        var activa = Guid.NewGuid();
        api.Productos.Apagar(apagada);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(TokenBackoffice(api, flit));

        var deApagada = await cliente.GetAsync($"{Sonda}?empresa={apagada}", ct);
        var deActiva = await cliente.GetAsync($"{Sonda}?empresa={activa}", ct);

        deApagada.StatusCode.Should().Be(HttpStatusCode.NotFound, await deApagada.Content.ReadAsStringAsync(ct));
        (await deApagada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(EmpresaHabilitada.CodigoNoEncontrada);
        deActiva.StatusCode.Should().Be(HttpStatusCode.OK, await deActiva.Content.ReadAsStringAsync(ct));
        (await Auditoria(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Equal(activa);
    }

    private static void MapearSonda(WebApplication app) =>
        app.MapGroup(Sonda).RequireAuthorization(PoliticaBackoffice.Nombre).SoloEmpresaHabilitada().ConEmpresaExplicita()
            .MapGet(string.Empty, (HttpContext http) => Results.Ok(new { empresa = EmpresaExplicita.De(http), nombre = EmpresaHabilitada.Nombre(http) }));

    private static string TokenBackoffice(ApiDePrueba api, Guid flit) =>
        api.Token(empresa: flit, permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer], roles: ["operador"], tipoEmpresa: "FLIT");

    private static async Task<List<LecturaBackoffice>> Auditoria(ApiDePrueba api, Guid flit, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, flit);
        return await db.Set<LecturaBackoffice>().AsNoTracking().OrderBy(l => l.Fecha).ThenBy(l => l.Id).ToListAsync(ct);
    }

    /// <summary>Doble de la lista de empresas de Identidad: responde <c>empresas</c> o lanza <see cref="Falla"/>.</summary>
    private sealed class EmpresasFijas(IReadOnlyList<EmpresaPlataforma> empresas) : IEmpresasPlataforma
    {
        private int _llamadas;

        public Exception? Falla { get; init; }

        public int Llamadas => _llamadas;

        public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            return Falla is not null ? Task.FromException<IReadOnlyList<EmpresaPlataforma>?>(Falla) : Task.FromResult<IReadOnlyList<EmpresaPlataforma>?>(empresas);
        }
    }
}
