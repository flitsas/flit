using Flit.Infrastructure.Consultations.Avaluos;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// Regresión del bug de armado de URL de Fasecolda (Feature #10707): con <c>HttpClient.BaseAddress</c>
/// que incluye un path base (<c>/apifasecolda</c>), una ruta relativa con '/' inicial descartaba ese
/// path (<c>new Uri(base, "/token")</c> → <c>host/token</c>) → 404. El fix arma URLs absolutas.
/// </summary>
public sealed class FasecoldaUrlTests
{
    [Theory]
    [InlineData("https://guiadevalores.fasecolda.com/apifasecolda", "/token",
                "https://guiadevalores.fasecolda.com/apifasecolda/token")]
    [InlineData("https://guiadevalores.fasecolda.com/apifasecolda/", "/api/listacodigosid/consultabycodigo/08801052",
                "https://guiadevalores.fasecolda.com/apifasecolda/api/listacodigosid/consultabycodigo/08801052")]
    [InlineData("https://fasecoldaback.quantil.co", "/api/busquedaVin/ABC123",
                "https://fasecoldaback.quantil.co/api/busquedaVin/ABC123")]
    public void Absolute_preserva_el_path_base_y_normaliza_los_slashes(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, FasecoldaUrl.Absolute(baseUrl, path));
    }

    [Fact]
    public void Absolute_no_depende_del_slash_final_de_la_base_ni_del_inicial_de_la_ruta()
    {
        const string expected = "https://h/apifasecolda/token";
        Assert.Equal(expected, FasecoldaUrl.Absolute("https://h/apifasecolda", "token"));
        Assert.Equal(expected, FasecoldaUrl.Absolute("https://h/apifasecolda/", "token"));
        Assert.Equal(expected, FasecoldaUrl.Absolute("https://h/apifasecolda", "/token"));
        Assert.Equal(expected, FasecoldaUrl.Absolute("https://h/apifasecolda/", "/token"));
    }
}
