using System.Net;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07; F1a-CF3, D-ACT, M03) — el consumidor de la cola <c>impuestos.plataforma</c> atiende los dos trabajos de la
/// plataforma. <c>impuestos.producto.cambiado</c> v1 <c>{tenantId, productCode, enabled, changedAt}</c>: encender sin fila deja
/// <c>PENDIENTE</c> (versión 1, historial de la plataforma) para que se evalúe; dos entregas del mismo trabajo dan una sola
/// transición y un solo alta (bandeja); un encendido repetido (cambio solo de notas) y un apagado sin fila no hacen nada.
/// <c>impuestos.aprovisionamiento.reintentar</c> v1 <c>{tenantId, solicitadoPor}</c> pasa a <c>PENDIENTE</c> solo desde
/// <c>ERROR</c>, <c>INCIERTO</c> o <c>REQUIERE_REVISION_NIT</c>; en otro estado se confirma sin efecto.
/// </summary>
/// <example>
/// <code>
/// await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, encendido: true), ct);
/// </code>
/// </example>
public sealed class ConsumidorCambioProductoTests
{
    [Fact]
    public async Task Encender_sin_fila_deja_PENDIENTE_version_1_con_historial_de_la_plataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sobre = EntornoAprovisionamiento.Cambio(empresa, true);

        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeTrue();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.Version.Should().Be(1);
        fila.UltimoCambioPlataformaId.Should().Be(sobre.EventId);
        fila.ProximoIntentoEn.Should().BeNull("se evalúa en la siguiente ronda, sin espera");
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().BeNull();
        paso.Hacia.Should().Be(E.Pendiente);
        paso.ActorTipo.Should().Be("PLATAFORMA");
    }

    [Fact]
    public async Task Dos_entregas_del_mismo_trabajo_dan_una_transicion_y_un_solo_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sobre = EntornoAprovisionamiento.Cambio(empresa, true);

        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeFalse("la bandeja ya tiene ese eventId");
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeFalse();
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Listo);
        (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().HaveCount(2, "PENDIENTE y LISTO, una vez cada uno");
    }

    [Fact]
    public async Task Encendido_repetido_con_otro_trabajo_no_cambia_nada_ni_vuelve_a_dar_de_alta()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct);
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);
        var version = (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Version;

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true), ct)).Should().BeTrue("otro eventId: se confirma");
        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Listo);
        fila.Version.Should().Be(version, "un encendido que ya estaba encendido (cambio solo de notas) no es una transición");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Fact]
    public async Task Apagado_sin_fila_se_confirma_sin_crear_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, false), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct)).Should().BeNull();
        (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Otro_producto_se_confirma_sin_efecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(empresa, true, producto: "diagnostico"), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct)).Should().BeNull();
    }

    [Fact]
    public async Task La_empresa_de_los_datos_distinta_de_la_del_sobre_va_a_la_DLQ_sin_reintento()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var otra = Guid.NewGuid();
        var sobre = EntornoAprovisionamiento.Sobre(ConsumidorCambioProducto.TipoCambio, empresa,
            new { tenantId = otra, productCode = "impuestos", enabled = true, changedAt = DateTimeOffset.UtcNow });

        var act = () => EntornoAprovisionamiento.EntregarAsync(api, sobre, ct);

        await act.Should().ThrowAsync<SinReintentoAutomaticoException>();
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct)).Should().BeNull();
        (await EntornoAprovisionamiento.FilaAsync(api, otra, ct)).Should().BeNull();
    }

    [Theory]
    [InlineData(E.Error)]
    [InlineData(E.Incierto)]
    [InlineData(E.RequiereRevisionNit)]
    [InlineData(E.ListoSinWebhook)]
    public async Task Reintentar_desde_un_estado_reintentable_pasa_a_PENDIENTE_con_el_Super_Admin_en_el_historial(E desde)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, desde, ct, f =>
        {
            f.MotivoCodigo = "ALTA_SIN_CONFIRMAR";
            f.IntentosAutomaticos = 5;
        });

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Reintentar(empresa, sa), ct)).Should().BeTrue();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(E.Pendiente);
        fila.IntentosAutomaticos.Should().Be(0);
        fila.MotivoCodigo.Should().BeNull();
        fila.ProximoIntentoEn.Should().BeNull();
        fila.Version.Should().Be(2);
        var paso = (await EntornoAprovisionamiento.HistorialAsync(api, empresa, ct)).Should().ContainSingle().Subject;
        paso.Desde.Should().Be(desde);
        paso.ActorTipo.Should().Be("SUPER_ADMIN");
        paso.ActorId.Should().Be(sa);
    }

    [Theory]
    [InlineData(E.Listo)]
    [InlineData(E.Pendiente)]
    [InlineData(E.RequiereCredenciales)]
    [InlineData(E.Suspendido)]
    public async Task Reintentar_en_otro_estado_se_confirma_sin_efecto(E estado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, estado, ct);

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Reintentar(empresa, Guid.NewGuid()), ct)).Should().BeTrue();

        var fila = await EntornoAprovisionamiento.FilaAsync(api, empresa, ct);
        fila!.Estado.Should().Be(estado);
        fila.Version.Should().Be(1);
    }

    [Fact]
    public async Task Reintentar_sin_fila_se_confirma_sin_crear_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();

        (await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Reintentar(empresa, Guid.NewGuid()), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct)).Should().BeNull();
    }

    [Fact]
    public void La_cola_se_suscribe_a_los_dos_trabajos_del_exchange_de_impuestos_y_solo_acepta_a_la_plataforma()
    {
        ConsumidorCambioProducto.Cola.Should().Be("impuestos.plataforma");
        ConsumidorCambioProducto.Exchange.Should().Be("impuestos");
        ConsumidorCambioProducto.TipoCambio.Should().Be("impuestos.producto.cambiado");
        ConsumidorCambioProducto.TipoReintentar.Should().Be("impuestos.aprovisionamiento.reintentar");
        ConsumidorCambioProducto.Productores.Should().BeEquivalentTo(["tramites", "plataforma"]);
    }

    [Fact]
    public async Task Ronda_del_trabajador_evalua_las_pendientes_vencidas_y_reporta_lo_no_reportado()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var vencida = Guid.NewGuid();
        var futura = Guid.NewGuid();
        await EntornoAprovisionamiento.EntregarAsync(api, EntornoAprovisionamiento.Cambio(vencida, true), ct);
        await EntornoAprovisionamiento.SembrarAsync(api, futura, E.Pendiente, ct, f => f.ProximoIntentoEn = DateTimeOffset.UtcNow.AddHours(1));

        var trabajador = api.App.Services.GetServices<IHostedService>().OfType<TrabajadorReintentoAlta>().SingleOrDefault()
            ?? ActivatorUtilities.CreateInstance<TrabajadorReintentoAlta>(api.App.Services);
        await trabajador.EjecutarRondaAsync(ct);

        (await EntornoAprovisionamiento.FilaAsync(api, vencida, ct))!.Estado.Should().Be(E.Listo);
        (await EntornoAprovisionamiento.FilaAsync(api, futura, ct))!.Estado.Should().Be(E.Pendiente);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
        identidad.Reportes.Where(r => r.Empresa == vencida).Select(r => r.Reporte.Estado).Should().Contain("LISTO");
        (await EntornoAprovisionamiento.FilaAsync(api, vencida, ct))!.ReportadoVersion.Should().Be(2);
    }
}
