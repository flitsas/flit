using System.Net;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (revisión previa al PR: MEN-9 / security B-1) — además de <c>X-Flit-Delivery</c> (que no va firmado), el receptor
/// descarta un aviso repetido por (<c>consulta_id</c>, <c>attempt</c>) del cuerpo firmado: un replay del mismo cuerpo con otro
/// <c>X-Flit-Delivery</c> responde 200 y no se registra ni se aplica dos veces.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~DedupePorConsultaTests
/// </code>
/// </example>
public sealed class DedupePorConsultaTests
{
    [Fact]
    public async Task Un_replay_con_otro_X_Flit_Delivery_responde_200_y_no_se_registra_dos_veces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var cuerpo = R.Aviso(R.Consulta("c-1", attempt: 1), "d-cuerpo");

        using var original = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, R.Secreto, DateTimeOffset.UtcNow, "d-original"), ct);
        using var replay = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, R.Secreto, DateTimeOffset.UtcNow.AddSeconds(30), "d-replay"), ct);

        original.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "un duplicado se confirma sin repetir");
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
        (await R.AvisosAsync(api, item.Id, ct)).Should().ContainSingle("el par (consulta_id, attempt) ya estaba registrado")
            .Which.DeliveryId.Should().Be("d-original");
    }

    [Fact]
    public async Task Un_attempt_nuevo_de_la_misma_consulta_si_se_registra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var primero = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1", attempt: 1)), R.Secreto, DateTimeOffset.UtcNow, "d-1"), ct);
        using var segundo = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1", attempt: 2)), R.Secreto, DateTimeOffset.UtcNow, "d-2"), ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK);
        segundo.StatusCode.Should().Be(HttpStatusCode.OK);
        (await R.AvisosAsync(api, item.Id, ct)).Select(a => a.Attempt).Should().BeEquivalentTo(new int?[] { 1, 2 });
    }
}
