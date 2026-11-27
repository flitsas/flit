using System.Net;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Aprovisionamiento;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (M07b, plan §5.8 punto 7; guía del proveedor §6 «Ciclo de vida del webhook_secret») — cuando se enciende
/// <c>modo_respaldo_firma</c> se encola un alta para refrescar el secreto (<see cref="ConsumidorCredenciales.TipoRefrescarSecreto"/>).
/// Si el alta trae un secreto distinto, el anterior se guarda cifrado en <c>webhook_secret_anterior_cifrado</c> durante
/// <see cref="AlmacenCredenciales.VentanaSecretoAnterior"/> (24 h) y el verificador de M10 prueba [vigente, anterior]; pasada la
/// ventana, solo el vigente. Un secreto nuevo apaga el modo respaldo; el mismo secreto no.
/// </summary>
/// <example>
/// <code>
/// await EntornoAprovisionamiento.EntregarInternoAsync(api, Refrescar(empresa), ct);
/// </code>
/// </example>
public sealed class SecretoAnteriorTests
{
    private const string SecretoNuevo = "whsec_secreto-nuevo-de-prueba-no-real";

    [Fact]
    public async Task El_alta_de_refresco_con_otro_secreto_guarda_el_anterior_24h_y_el_webhook_acepta_los_dos_dentro_de_la_ventana()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, secreto: SecretoNuevo));
        await using var api = await ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct, a =>
        {
            a.ModoRespaldoFirma = true;
            a.FirmasInvalidasConsecutivas = 5;
        });

        (await EntornoAprovisionamiento.EntregarInternoAsync(api, Refrescar(empresa), ct)).Should().BeTrue();

        var credencial = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        credencial!.WebhookSecret.Should().Be(SecretoNuevo);
        credencial.WebhookSecretAnterior.Should().Be(R.Secreto, "dentro de la ventana de 24 h");
        await using (var db = api.Contexto(empresa))
        {
            var fila = await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
            fila.WebhookSecretAnteriorCifrado.Should().NotBeNull();
            fila.WebhookSecretAnteriorHasta.Should().Be(reloj.Ahora + TimeSpan.FromHours(24));
        }

        var aprov = await R.AprovisionamientoAsync(api, empresa, ct);
        aprov.ModoRespaldoFirma.Should().BeFalse("un secreto nuevo apaga el modo respaldo (F2b-CF3)");
        aprov.Estado.Should().Be(EstadoAprovisionamiento.Listo);

        (await EnviarAsync(api, token, R.Secreto, reloj.Ahora, ct)).Should().Be(HttpStatusCode.OK, "un aviso en reintento firmado con el anterior");
        (await EnviarAsync(api, token, SecretoNuevo, reloj.Ahora, ct)).Should().Be(HttpStatusCode.OK);

        reloj.Avanzar(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        (await EnviarAsync(api, token, R.Secreto, reloj.Ahora, ct)).Should().Be(HttpStatusCode.Unauthorized, "pasada la ventana solo vale el vigente");
        (await EnviarAsync(api, token, SecretoNuevo, reloj.Ahora, ct)).Should().Be(HttpStatusCode.OK);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.WebhookSecretAnterior.Should().BeNull();
    }

    [Fact]
    public async Task El_alta_de_refresco_con_el_mismo_secreto_no_guarda_anterior_ni_apaga_el_modo_respaldo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.OK, EntornoAprovisionamiento.Alta(created: false, secreto: R.Secreto));
        await using var api = await ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, _) = await SembrarAsync(api, ct, a =>
        {
            a.ModoRespaldoFirma = true;
            a.FirmasInvalidasConsecutivas = 5;
        });

        (await EntornoAprovisionamiento.EntregarInternoAsync(api, Refrescar(empresa), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.WebhookSecretAnterior.Should().BeNull();
        (await R.AprovisionamientoAsync(api, empresa, ct)).ModoRespaldoFirma.Should().BeTrue("las firmas inválidas no vienen de una rotación");
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1);
    }

    [Fact]
    public async Task Cinco_firmas_invalidas_encienden_el_modo_respaldo_y_encolan_un_solo_alta_de_refresco()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await ConApiAsync(new ApiLiquidacionDePrueba(), reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct);

        for (var i = 0; i < 4; i++)
            await EnviarAsync(api, token, "whsec_secreto-equivocado", reloj.Ahora, ct);
        (await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoRefrescarSecreto, ct)).Should().BeEmpty();

        await EnviarAsync(api, token, "whsec_secreto-equivocado", reloj.Ahora, ct);
        await EnviarAsync(api, token, "whsec_secreto-equivocado", reloj.Ahora, ct);

        var mensaje = (await EntornoAprovisionamiento.OutboxAsync(api, empresa, ConsumidorCredenciales.TipoRefrescarSecreto, ct))
            .Should().ContainSingle("solo al encenderse, no en cada firma inválida siguiente").Subject;
        mensaje.Exchange.Should().Be("flit.impuestos");
        EntornoAprovisionamiento.Sobre(mensaje).TenantId.Should().Be(empresa);
    }

    private static Flit.Platform.Sdk.Messaging.EventEnvelope Refrescar(Guid empresa) =>
        EntornoAprovisionamiento.Sobre(ConsumidorCredenciales.TipoRefrescarSecreto, empresa, new { tenantId = empresa }, "impuestos");

    private static Task<ApiDePrueba> ConApiAsync(ApiLiquidacionDePrueba proveedor, RelojDePrueba reloj) =>
        ApiDePrueba.ConBaseAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
                ["Impuestos:Api:IntegrationToken"] = ClienteApiLiquidacionTests.Token,
                ["Impuestos:Conciliacion:TrabajadorActivo"] = "false",
            });
            b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => proveedor);
            b.Services.AddSingleton<IClienteIdentidadAlta>(new IdentidadDePrueba());
            b.Services.AddSingleton<TimeProvider>(reloj);
        });

    private static async Task<(Guid Empresa, string Token)> SembrarAsync(ApiDePrueba api, CancellationToken ct, Action<AprovisionamientoEmpresa>? ajustar = null)
    {
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct, a =>
        {
            a.FirmaConfirmada = true;
            ajustar?.Invoke(a);
        });
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (_, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        return (empresa, token);
    }

    private static async Task<HttpStatusCode> EnviarAsync(ApiDePrueba api, string token, string secreto, DateTimeOffset ahora, CancellationToken ct)
    {
        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), secreto, ahora), ct);
        return r.StatusCode;
    }
}
