using System.Reflection;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// El pedido que puede llegar al modelo no tiene documentos ni nombres. Si alguien agrega una propiedad, la prueba la nombra.
/// </summary>
public sealed class RedactorArquitecturaTests
{
    private static readonly string[] Prohibidas = ["Documento", "Nombre", "Propietario", "Locatario", "Payload", "DocCifrado"];

    [Fact]
    public void El_pedido_solo_expone_veredicto_reglas_y_datos_del_bien()
    {
        Nombres(typeof(DatosBien)).Should().BeEquivalentTo(["Placa", "Clase", "Marca", "Linea", "Modelo", "Servicio"]);
        Nombres(typeof(ReglaDisparada)).Should().BeEquivalentTo(["Codigo", "Fuente", "Efecto", "MontoPesos"]);
        Nombres(typeof(PedidoRedaccion)).Should().BeEquivalentTo(["Veredicto", "Reglas", "Bien", "RedaccionSuspendida"]);
        ProhibidasEn(typeof(DatosBien), typeof(ReglaDisparada), typeof(PedidoRedaccion)).Should().BeEmpty();
    }

    private static List<string> Nombres(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

    private static IEnumerable<string> ProhibidasEn(params Type[] tipos) =>
        tipos.SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Prohibidas.Any(mala => p.Name.Contains(mala, StringComparison.OrdinalIgnoreCase)))
            .Select(p => $"{tipo.Name}.{p.Name}"));
}
