using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Correos 2.0, de punta a punta: la autorización de cambio de infractor que <see cref="AdjuntosAviso"/> adjunta al aviso sale con
/// las bandas del membrete FLIT (vectoriales, con degradados: <c>/ShadingType</c>, en carta) para el conjunto FLIT, y con la imagen
/// de Renting de V1 (<c>renting.jpg</c>: <c>/DCTDecode</c>, en A4) para el conjunto Renting.
/// </summary>
public sealed class AutorizacionMembreteTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string FirmaPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public async Task La_autorizacion_FLIT_lleva_las_bandas_del_membrete()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var pdf = await AutorizacionAsync("Flit", "AUF-1");
        pdf.Should().Contain("/ShadingType", "las bandas FLIT son SVG con degradados").And.Contain("/MediaBox [0 0 612 792]", "carta");
        pdf.Should().NotContain("/DCTDecode", "no lleva la imagen de Renting");
    }

    [Fact]
    public async Task La_autorizacion_de_Renting_sigue_con_renting_jpg()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var pdf = await AutorizacionAsync("Renting", "AUR-1");
        pdf.Should().Contain("/DCTDecode", "renting.jpg de página completa").And.Contain("/MediaBox [0 0 595", "el membrete de V1 es A4");
        pdf.Should().NotContain("/ShadingType", "sin las bandas de FLIT");
    }

    /// <summary>El PDF de la autorización (como texto Latin-1, para buscar los diccionarios del PDF) que se adjunta al aviso 1.</summary>
    private async Task<string> AutorizacionAsync(string conjunto, string numero)
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, conjunto: conjunto);
        if (conjunto == "Renting")
        {
            // HU #13882: el conjunto sale del enrutamiento de Trámites; «API Renting cliente» es Renting (como en AvisosTests).
            api.Empresas.Agregar(empresa, "Renting Colombia S.A.S", NitGestora, CanalCorreo.EmpresaApi);
            api.App.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove(CanalDeTramites.ClaveCache);
        }

        var ids = await IngresarAsync(api, empresa, Item(numero));
        await RepresentanteConFirmaAsync(empresa);

        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var c = await db.Comparendos.AsNoTracking().SingleAsync(x => x.Id == ids.Values.Single(), ct);
        var config = await scope.ServiceProvider.GetRequiredService<Capacidades>().DeAsync(empresa, ct);
        config.ConjuntoPlantillas.ToString().Should().Be(conjunto);

        var adjuntos = await scope.ServiceProvider.GetRequiredService<AdjuntosAviso>().ParaAvisoAsync(c, config, TipoAviso.Aviso1, ct);
        adjuntos.Autorizacion.Should().BeTrue(string.Join("; ", adjuntos.Faltantes));
        var autorizacion = adjuntos.Archivos.Single(a => a.Nombre.StartsWith("autorizacion-cambio-infractor", StringComparison.Ordinal));
        autorizacion.Contenido.Take(4).Should().Equal("%PDF"u8.ToArray());
        return Encoding.Latin1.GetString(autorizacion.Contenido);
    }

    private async Task RepresentanteConFirmaAsync(Guid empresa)
    {
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.RepresentantesVer, Permisos.RepresentantesGestionar]));
        var rl = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
            permisoEnvioDp = true, permisoCambioContraventor = true,
        })).Content.ReadFromJsonAsync<JsonElement>();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Convert.FromBase64String(FirmaPng)) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" },
        };
        (await cliente.PostAsync($"/api/v1/comparendos/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", form)).EnsureSuccessStatusCode();
    }
}
