using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (M10; F2b-CF3, plan §3.1) — marca <c>modo_respaldo_firma</c>: se enciende tras 5 firmas inválidas seguidas de tokens
/// conocidos en una hora (M09 deja entonces de enviar <c>callbackUrl</c>) y la apaga el primer webhook válido. Un token desconocido
/// no cuenta; un aviso válido en medio reinicia la cuenta; inválidas separadas por más de una hora no se suman.
/// </summary>
/// <example>
/// <code>
/// for (var i = 0; i &lt; 5; i++) await EnviarAsync(api, token, "whsec_otro", reloj.Ahora, ct); // 401
/// (await R.AprovisionamientoAsync(api, empresa, ct)).ModoRespaldoFirma; // true
/// </code>
/// </example>
public sealed class ModoRespaldoFirmaTests
{
    private const string SecretoEquivocado = "whsec_secreto-equivocado";

    [Fact]
    public async Task Cinco_firmas_invalidas_seguidas_encienden_el_modo_respaldo_y_cuatro_no()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct);

        for (var i = 0; i < 4; i++)
            (await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct)).Should().Be(HttpStatusCode.Unauthorized);
        var tras4 = await R.AprovisionamientoAsync(api, empresa, ct);
        tras4.FirmasInvalidasConsecutivas.Should().Be(4);
        tras4.ModoRespaldoFirma.Should().BeFalse();

        (await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct)).Should().Be(HttpStatusCode.Unauthorized);

        var tras5 = await R.AprovisionamientoAsync(api, empresa, ct);
        tras5.FirmasInvalidasConsecutivas.Should().Be(5);
        tras5.ModoRespaldoFirma.Should().BeTrue();
        tras5.Estado.Should().Be(EstadoAprovisionamiento.Listo, "la marca es ortogonal al estado");
    }

    [Fact]
    public async Task Un_webhook_valido_apaga_el_modo_respaldo_y_reinicia_la_cuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct, a =>
        {
            a.ModoRespaldoFirma = true;
            a.FirmasInvalidasConsecutivas = 7;
        });

        (await EnviarAsync(api, token, R.Secreto, reloj.Ahora, ct)).Should().Be(HttpStatusCode.OK);

        var fila = await R.AprovisionamientoAsync(api, empresa, ct);
        fila.ModoRespaldoFirma.Should().BeFalse();
        fila.FirmasInvalidasConsecutivas.Should().Be(0);
    }

    [Fact]
    public async Task Un_aviso_valido_en_medio_reinicia_la_cuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct);

        for (var i = 0; i < 3; i++)
            await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct);
        (await EnviarAsync(api, token, R.Secreto, reloj.Ahora, ct)).Should().Be(HttpStatusCode.OK);
        for (var i = 0; i < 3; i++)
            await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct);

        var fila = await R.AprovisionamientoAsync(api, empresa, ct);
        fila.FirmasInvalidasConsecutivas.Should().Be(3);
        fila.ModoRespaldoFirma.Should().BeFalse("las inválidas no fueron seguidas");
    }

    [Fact]
    public async Task Invalidas_separadas_por_mas_de_una_hora_no_se_suman()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, token) = await SembrarAsync(api, ct);

        for (var i = 0; i < 4; i++)
            await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct);
        reloj.Avanzar(TimeSpan.FromMinutes(61));
        await EnviarAsync(api, token, SecretoEquivocado, reloj.Ahora, ct);

        var fila = await R.AprovisionamientoAsync(api, empresa, ct);
        fila.FirmasInvalidasConsecutivas.Should().Be(1, "la ventana es de una hora");
        fila.ModoRespaldoFirma.Should().BeFalse();
    }

    [Fact]
    public async Task Tokens_desconocidos_no_cuentan()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, _) = await SembrarAsync(api, ct);

        for (var i = 0; i < 6; i++)
            (await EnviarAsync(api, R.NuevoToken(), SecretoEquivocado, reloj.Ahora, ct)).Should().Be(HttpStatusCode.Gone);

        var fila = await R.AprovisionamientoAsync(api, empresa, ct);
        fila.FirmasInvalidasConsecutivas.Should().Be(0);
        fila.ModoRespaldoFirma.Should().BeFalse();
    }

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
