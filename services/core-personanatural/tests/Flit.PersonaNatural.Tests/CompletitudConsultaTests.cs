using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// La placa y el documento del titular se capturan al consultar y al registrar a la persona.
/// El tipo los vuelve a pedir como campos; no deben bloquear el envío si ya están.
/// </summary>
public class CompletitudConsultaTests
{
    private static readonly DefinicionTipoDto Definicion = new(
        "TRASPASO_STANDARD", "Traspaso", "TRASPASO", 1, "h", true, string.Empty,
        [
            new PasoDto("consulta", "Consulta", ["vehicle_query"],
            [
                new CampoDto("plate", "Placa", "text", true, [], "{}"),
                new CampoDto("owner_document_type", "Tipo de documento del titular", "text", true, [], "{}"),
                new CampoDto("owner_document_number", "Documento del titular", "text", true, [], "{}"),
                new CampoDto("km", "Kilometraje", "number", true, [], "{}"),
            ]),
        ],
        [new ActorRolDto("vendedor", "Vendedor", 1, 1, true, true, true)],
        [],
        new FlagsTipoDto("PLATE", false, false, false, false, false, false));

    [Fact]
    public void PlacaYDocumentoDelTitularYaCapturados_NoSeVuelvenAPedir()
    {
        var (_, pendientes) = ReglasBorrador.Completitud(
            Definicion,
            new Dictionary<string, string>(),
            new DatosSolicitud(),
            [("vendedor", 1)],
            new HashSet<string>(),
            new ReglasBorrador.CapturaConsulta("ABC123", null, "CC", true));

        pendientes.Select(p => p.Referencia).Should().Equal("km");
    }

    [Fact]
    public void SinPlacaNiTitular_EsosCamposSiguenPendientes()
    {
        var (_, pendientes) = ReglasBorrador.Completitud(
            Definicion,
            new Dictionary<string, string>(),
            new DatosSolicitud(),
            [],
            new HashSet<string>());

        pendientes.Where(p => p.Codigo == "campo_requerido").Select(p => p.Referencia)
            .Should().BeEquivalentTo("plate", "owner_document_type", "owner_document_number", "km");
    }

    [Fact]
    public void ElCompradorCubreAlTitular_CuandoNoHayVendedor()
    {
        var (_, pendientes) = ReglasBorrador.Completitud(
            Definicion,
            new Dictionary<string, string> { ["plate"] = "ABC123" },
            new DatosSolicitud(),
            [("comprador", 1)],
            new HashSet<string>(),
            new ReglasBorrador.CapturaConsulta("ABC123", null, "CC", true));

        pendientes.Should().NotContain(p => p.Referencia == "owner_document_type" || p.Referencia == "owner_document_number");
    }
}
