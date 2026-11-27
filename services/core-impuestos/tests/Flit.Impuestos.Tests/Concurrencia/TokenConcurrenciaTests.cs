using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Concurrencia;

/// <summary>
/// HU #14694 (revisión previa al PR: code review IMP-4 / DB B8) — <c>aprovisionamientos</c>, <c>credenciales_empresa</c>,
/// <c>configuracion_empresa</c> y <c>recibos</c> llevan token de concurrencia (<c>xmin</c>), como <c>liquidaciones</c> y
/// <c>liquidacion_items</c>: dos contextos que leen la misma fila y la guardan no se pisan; el segundo recibe
/// <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~TokenConcurrenciaTests
/// </code>
/// </example>
public sealed class TokenConcurrenciaTests
{
    [Theory]
    [InlineData("aprovisionamientos")]
    [InlineData("credenciales_empresa")]
    [InlineData("configuracion_empresa")]
    [InlineData("recibos")]
    public async Task Dos_escrituras_sobre_la_misma_lectura_no_se_pisan(string tabla)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        Guid? recibo = null;
        if (tabla == "configuracion_empresa")
        {
            await using var db = api.Contexto(empresa);
            db.Add(new ConfiguracionEmpresa { TenantId = empresa, ActualizadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        else if (tabla == "recibos")
        {
            var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
            var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Exitoso, ct);
            await using var db = api.Contexto(empresa);
            var fila = new Recibo { ItemId = item.Id, TenantId = empresa, FileIdApi = "f-1", Tipo = TipoRecibo.Vigencia };
            db.Add(fila);
            await db.SaveChangesAsync(ct);
            recibo = fila.Id;
        }

        await using var primero = api.Contexto(empresa);
        await using var segundo = api.Contexto(empresa);
        Modificar(tabla, await LeerAsync(primero, tabla, recibo, ct), 1);
        Modificar(tabla, await LeerAsync(segundo, tabla, recibo, ct), 2);

        await primero.SaveChangesAsync(ct);
        var pisar = () => segundo.SaveChangesAsync(ct);

        await pisar.Should().ThrowAsync<DbUpdateConcurrencyException>("la fila cambió después de leerla (xmin)");
    }

    private static async Task<object> LeerAsync(ImpuestosDb db, string tabla, Guid? recibo, CancellationToken ct) => tabla switch
    {
        "aprovisionamientos" => await db.Set<AprovisionamientoEmpresa>().SingleAsync(ct),
        "credenciales_empresa" => await db.Set<CredencialEmpresa>().SingleAsync(ct),
        "configuracion_empresa" => await db.Set<ConfiguracionEmpresa>().SingleAsync(ct),
        _ => await db.Set<Recibo>().SingleAsync(r => r.Id == recibo, ct),
    };

    private static void Modificar(string tabla, object fila, int n)
    {
        switch (tabla)
        {
            case "aprovisionamientos":
                ((AprovisionamientoEmpresa)fila).MotivoDetalle = $"escritura {n}";
                break;
            case "credenciales_empresa":
                ((CredencialEmpresa)fila).EmpresaApiId = $"empresa-{n}";
                break;
            case "configuracion_empresa":
                ((ConfiguracionEmpresa)fila).MaxRegistrosLoteOperativo = n;
                break;
            default:
                ((Recibo)fila).Intentos = n;
                break;
        }
    }
}
