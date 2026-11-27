using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (revisión previa al PR: MEN-8) — la ventana de una hora del contador de firmas inválidas se mide contra
/// <c>aprovisionamientos.ultima_firma_invalida_en</c>, no contra <c>actualizado_en</c>; contar una firma inválida no toca
/// <c>actualizado_en</c>. Otro cambio reciente de la fila no prolonga la cuenta.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~VentanaFirmaInvalidaTests
/// </code>
/// </example>
public sealed class VentanaFirmaInvalidaTests
{
    private const string SecretoEquivocado = "whsec_secreto-equivocado";

    [Fact]
    public async Task La_ventana_se_mide_desde_la_ultima_firma_invalida_y_no_toca_actualizado_en()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var actualizado = reloj.Ahora.AddMinutes(-1);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct, a =>
        {
            a.FirmaConfirmada = true;
            a.FirmasInvalidasConsecutivas = 4;
            a.ActualizadoEn = actualizado;
        });
        await using (var db = api.Contexto(empresa))
            await db.Database.ExecuteSqlAsync(
                $"UPDATE impuestos.aprovisionamientos SET ultima_firma_invalida_en = {reloj.Ahora.AddHours(-2)} WHERE tenant_id = {empresa}", ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (_, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), SecretoEquivocado, reloj.Ahora), ct);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fila = await R.AprovisionamientoAsync(api, empresa, ct);
        fila.FirmasInvalidasConsecutivas.Should().Be(1, "la anterior inválida fue hace más de una hora, aunque la fila cambió hace un minuto");
        fila.ModoRespaldoFirma.Should().BeFalse();
        fila.ActualizadoEn.Should().Be(actualizado, "contar una firma inválida no es un cambio del aprovisionamiento");
        await using var lectura = api.Contexto(empresa);
        var ultima = await lectura.Database
            .SqlQuery<DateTimeOffset?>($"SELECT ultima_firma_invalida_en AS \"Value\" FROM impuestos.aprovisionamientos WHERE tenant_id = {empresa}")
            .SingleAsync(ct);
        ultima.Should().Be(reloj.Ahora);
    }
}
