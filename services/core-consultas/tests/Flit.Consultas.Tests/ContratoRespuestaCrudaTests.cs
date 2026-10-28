using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #14153 AC9 — el comentario de <c>RespuestaCruda.json</c> en <c>contracts/proto/flit/consultas/v1/resultado.proto</c> ya no
/// dice «JSON sanitizado» (en el camino hacia Diagnóstico el crudo viaja sin sanear) y el mensaje conserva sus campos y números.
/// <para>Uso de ejemplo: <c>dotnet test --filter FullyQualifiedName~ContratoRespuestaCrudaTests</c>.</para>
/// </summary>
public sealed class ContratoRespuestaCrudaTests
{
    [Fact]
    public void El_comentario_de_RespuestaCruda_json_explica_que_no_se_sanea_hacia_Diagnostico()
    {
        var mensaje = Bloque(File.ReadAllText(Proto()), "message RespuestaCruda {");

        mensaje.Should().NotContain("JSON sanitizado");
        mensaje.Should().Contain("sin sanear").And.Contain("Diagnóstico");
        mensaje.Should().Contain("string json = 4;");
    }

    [Fact]
    public void RespuestaCruda_conserva_sus_campos_y_numeros()
    {
        RespuestaCruda.Descriptor.Fields.InFieldNumberOrder().Select(f => (f.Name, f.FieldNumber))
            .Should().Equal(("proveedor", 1), ("tipo_sujeto", 2), ("clave_sujeto", 3), ("json", 4));
    }

    private static string Bloque(string proto, string inicio)
    {
        var desde = proto.IndexOf(inicio, StringComparison.Ordinal);
        desde.Should().BeGreaterThanOrEqualTo(0, $"resultado.proto declara {inicio}");
        return proto[desde..proto.IndexOf('}', desde)];
    }

    private static string Proto()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var ruta = Path.Combine(dir.FullName, "contracts", "proto", "flit", "consultas", "v1", "resultado.proto");
            if (File.Exists(ruta))
                return ruta;
        }

        throw new FileNotFoundException("contracts/proto/flit/consultas/v1/resultado.proto no está en ningún ancestro de la salida de las pruebas");
    }
}
