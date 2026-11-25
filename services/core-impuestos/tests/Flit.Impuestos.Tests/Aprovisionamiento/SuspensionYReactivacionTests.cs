using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07; D-DESACT, F1b-CF5/CF6/CF7/CF8) — apagar Impuestos es una suspensión reversible: el aprovisionamiento pasa a
/// <c>SUSPENDIDO</c> guardando el estado previo, la api_key se conserva cifrada y no se llama al API (la empresa no se toca allá).
/// Reactivar con un previo <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c> lo recupera sin alta; con otro previo vuelve a
/// <c>PENDIENTE</c> y se evalúa de nuevo. Un alta que responde 201 mientras la compañía se suspendió guarda su credencial y deja
/// <c>LISTO</c> como previo.
/// </summary>
/// <example>
/// <code>
/// await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, encendido: false), ct);
/// </code>
/// </example>
public sealed class SuspensionYReactivacionTests
{
    private const string ApiKey = ClienteApiLiquidacionTests.ApiKey;
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;

    [Fact]
    public async Task Apagar_una_compania_LISTA_la_suspende_guarda_el_previo_conserva_la_llave_y_no_llama_al_API()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct, f => f.FirmaConfirmada = true);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, Secreto, OrigenCredencial.Alta, ct);
        var cifradaAntes = await CifradaAsync(api, empresa, ct);

        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido);
        fila.EstadoPrevio.Should().Be(E.Listo);
        fila.MotivoCodigo.Should().Be("PRODUCTO_DESACTIVADO");
        fila.Version.Should().Be(2);
        (await CifradaAsync(api, empresa, ct)).Should().Equal(cifradaAntes, "la api_key se conserva cifrada, sin tocarla");
        proveedor.Pedidos.Should().BeEmpty("la empresa no se toca en el API");
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().Be(E.Listo);
        paso.Hacia.Should().Be(E.Suspendido);
        paso.ActorTipo.Should().Be("PLATAFORMA");
    }

    [Theory]
    [InlineData(E.Listo, null)]
    [InlineData(E.ListoSinWebhook, "SIN_WEBHOOK_SECRET")]
    public async Task Reactivar_con_previo_listo_lo_recupera_sin_alta_ni_validacion(E previo, string? motivo)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, previo, ct, f => f.FirmaConfirmada = previo == E.Listo);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, ApiKey, previo == E.Listo ? Secreto : null, OrigenCredencial.Alta, ct);
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);

        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(previo);
        fila.EstadoPrevio.Should().BeNull();
        fila.MotivoCodigo.Should().Be(motivo);
        fila.FirmaConfirmada.Should().Be(previo == E.Listo, "la firma ya confirmada no se pierde por suspender");
        proveedor.Pedidos.Should().BeEmpty("reactivar reutiliza la api_key sin re-aprovisionar");
        identidad.LlamadasDatos.Should().Be(0);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(ApiKey);
    }

    [Fact]
    public async Task Reactivar_con_previo_no_listo_vuelve_a_PENDIENTE_y_se_evalua()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Error, ct, f => f.ErrorTipo = TipoErrorAprovisionamiento.Datos);
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);

        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct);
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Pendiente);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Fact]
    public async Task Apagar_dos_veces_no_es_otra_transicion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Listo, ct);

        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido);
        fila.EstadoPrevio.Should().Be(E.Listo, "el segundo apagado no pisa el previo con SUSPENDIDO");
        fila.Version.Should().Be(2);
    }

    [Fact]
    public async Task Suspender_una_PENDIENTE_cancela_su_reintento_programado()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct, f =>
        {
            f.IntentosAutomaticos = 2;
            f.ProximoIntentoEn = DateTimeOffset.UtcNow.AddMinutes(5);
        });

        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido);
        fila.ProximoIntentoEn.Should().BeNull();
        (await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct)).Should().BeFalse();
        proveedor.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_alta_201_que_llega_con_la_compania_ya_suspendida_guarda_la_credencial_y_deja_LISTO_como_previo()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        Api.ApiDePrueba? api = null;
        var proveedor = new ApiLiquidacionDePrueba();
        proveedor.Responder = r =>
        {
            if (r.RequestUri!.AbsolutePath == EntornoAprovisionamiento.RutaAlta)
            {
                // Mientras el alta está en el aire, la plataforma apaga el producto.
                EntornoAprovisionamiento.EntregarAsync(api!, EntornoAprovisionamiento.Cambio(empresa, false), CancellationToken.None).GetAwaiter().GetResult();
                return ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
            }

            return ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200);
        };
        await using var entorno = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        api = entorno;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Suspendido);
        fila.EstadoPrevio.Should().Be(E.Listo, "al reactivar se recupera sin otra alta (que daría 409)");
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(ApiKey, "la llave del 201 solo llega una vez");
    }

    private static async Task<byte[]> CifradaAsync(Api.ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return (await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct)).ApiKeyCifrada;
    }
}
