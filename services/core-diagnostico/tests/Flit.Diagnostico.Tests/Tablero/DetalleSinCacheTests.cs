using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563, hallazgo M2 de seguridad (D19) — el detalle de una celda trae <c>respuesta_proveedor</c>, <c>respuesta_fuente</c> y
/// <c>personas</c>, y no se puede guardar en cachés del navegador ni de intermediarios: responde <c>Cache-Control: no-store</c> y
/// <c>Pragma: no-cache</c>. Aplica al detalle de la compañía y a su lectura por celda del backoffice (formulario del llenado manual).
/// PostgreSQL real; placas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DetalleSinCacheTests
/// </code>
/// </example>
public sealed class DetalleSinCacheTests
{
    [Fact]
    public async Task HU14563_M2_detalle_de_la_celda_de_la_compania_no_se_guarda_en_cache()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST951", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaDetalle(lote.Id, lote.Vehiculos.Single().Id, "RUNT"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        SinCache(respuesta);
    }

    [Fact]
    public async Task HU14563_M2_lectura_de_la_celda_en_el_backoffice_no_se_guarda_en_cache()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos:
            [new("TST952", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.Avaluo, EstadoCelda.Error, 1)])]);
        var token = api.Token(
            empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

        var respuesta = await api.Cliente(token).GetAsync(
            $"{Program.Prefijo}/backoffice/consultas/{lote.Celda("TST952", Fuente.Avaluo).Id}/manual?empresa={empresa.Id}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        SinCache(respuesta);
    }

    private static void SinCache(HttpResponseMessage respuesta)
    {
        respuesta.Headers.CacheControl.Should().NotBeNull("el detalle trae datos personales");
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue();
        respuesta.Headers.Pragma.Select(p => p.Name).Should().Contain("no-cache");
    }
}
