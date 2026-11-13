using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Persistencia;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Notificaciones;

/// <summary>
/// HU #14563 B8 (AC36, AC37, AC38) — bandeja de la campana: <c>GET /notificaciones</c> con ETag y 304, <c>POST …/{nid}/leida</c>
/// idempotente (204, nunca antes de creada_en) y <c>POST …/leer-todas</c>. Solo las del usuario del token y de su compañía; la de
/// otro usuario responde 404 al marcarla.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~NotificacionesEndpointsTests
/// </code>
/// </example>
public sealed class NotificacionesEndpointsTests
{
    private const string Ruta = "/api/v1/diagnostico/notificaciones";

    private static readonly string[] PermisosLectura = [Permisos.DiagnosticosVer];

    // AC36 + AC38 — lista solo las propias, no_leidas con la misma visibilidad, ETag y 304 con If-None-Match.
    [Fact]
    public async Task Bandeja_lista_solo_las_propias_con_ETag_y_304()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        var usuario = Guid.NewGuid();
        var (empresa, lote) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        var (otraEmpresa, loteOtra) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.ZipEvidenciasListo, "{}", ct, usuario);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{\"total\":3}", ct, usuario);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{}", ct);
        await EsquemaR8Tests.InsertarAsync(conexion, otraEmpresa, loteOtra, Notificacion.LoteTerminado, "{}", ct, usuario);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosLectura, sujeto: usuario.ToString()));

        var respuesta = await cliente.GetAsync(Ruta + "?limite=20", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement;
        cuerpo.GetProperty("no_leidas").GetInt32().Should().Be(2);
        var items = cuerpo.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("tipo").GetString()).Should().BeEquivalentTo([Notificacion.ZipEvidenciasListo, Notificacion.LoteTerminado]);
        items.Should().OnlyContain(i => i.GetProperty("diagnostico_id").GetGuid() == lote);
        items.Should().OnlyContain(i => i.GetProperty("titulo").GetString()!.Length > 0 && i.GetProperty("accion").GetProperty("href").GetString()!.Contains(lote.ToString()));
        var etag = respuesta.Headers.ETag;
        etag.Should().NotBeNull();
        etag!.IsWeak.Should().BeTrue();

        using var condicional = new HttpRequestMessage(HttpMethod.Get, Ruta + "?limite=20");
        condicional.Headers.IfNoneMatch.Add(etag);
        var noModificada = await cliente.SendAsync(condicional, ct);
        noModificada.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await noModificada.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
    }

    // AC37 + AC38 — leída dos veces 204 y una sola fecha >= creada_en; la de otro usuario 404; leer-todas deja no_leidas en 0.
    [Fact]
    public async Task Leida_idempotente_otra_ajena_404_y_leer_todas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        var usuario = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var (empresa, lote) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        var futuro = DateTimeOffset.UtcNow.AddMinutes(5);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.ZipEvidenciasListo, "{}", ct, usuario, creadaEn: futuro);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{}", ct, usuario);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{}", ct, otro);
        var propia = Guid.Parse((await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.notificaciones WHERE usuario_id = '{usuario}' AND tipo = 'ZIP_EVIDENCIAS_LISTO'", ct)).Single());
        var ajena = Guid.Parse((await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.notificaciones WHERE usuario_id = '{otro}'", ct)).Single());
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosLectura, sujeto: usuario.ToString()));

        (await cliente.PostAsync($"{Ruta}/{propia}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var primera = (await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT (leida_en >= creada_en)::text || '|' || leida_en::text FROM diagnostico.notificaciones WHERE id = '{propia}'", ct)).Single();
        (await cliente.PostAsync($"{Ruta}/{propia}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var segunda = (await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT (leida_en >= creada_en)::text || '|' || leida_en::text FROM diagnostico.notificaciones WHERE id = '{propia}'", ct)).Single();
        primera.Should().StartWith("true|");
        segunda.Should().Be(primera);

        (await cliente.PostAsync($"{Ruta}/{ajena}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync($"{Ruta}/leer-todas", null, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bandeja = JsonDocument.Parse(await (await cliente.GetAsync(Ruta, ct)).Content.ReadAsStringAsync(ct)).RootElement;
        bandeja.GetProperty("no_leidas").GetInt32().Should().Be(0);
        (await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.notificaciones WHERE usuario_id = '{otro}' AND leida_en IS NULL", ct)).Should().HaveCount(1);
    }

    // AC38 — visibilidad PROPIOS: la notificación propia sobre un lote que el usuario ya no ve (lo creó otro) no aparece ni cuenta
    // en no_leidas, y marcarla responde 404.
    [Fact]
    public async Task Lote_oculto_por_PROPIOS_no_aparece_ni_se_puede_marcar()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        var usuario = Guid.NewGuid();
        var (empresa, lote) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{}", ct, usuario);
        var nid = Guid.Parse((await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.notificaciones WHERE usuario_id = '{usuario}'", ct)).Single());
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa))
        {
            db.Add(new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
            await db.SaveChangesAsync(ct);
        }

        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: PermisosLectura, roles: ["usuario_diagnostico"], sujeto: usuario.ToString()));

        var bandeja = JsonDocument.Parse(await (await cliente.GetAsync(Ruta, ct)).Content.ReadAsStringAsync(ct)).RootElement;
        bandeja.GetProperty("no_leidas").GetInt32().Should().Be(0);
        bandeja.GetProperty("items").GetArrayLength().Should().Be(0);
        (await cliente.PostAsync($"{Ruta}/{nid}/leida", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.notificaciones WHERE id = '{nid}' AND leida_en IS NULL", ct)).Should().HaveCount(1);
    }
}
