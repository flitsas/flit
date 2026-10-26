using System.Text.Json;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13876 / #13727 (ADR-0075, Q1-Q2) — los records espejo de core-consultas contra los fixtures compartidos
/// <c>contracts/fixtures/bus/*.v1.json</c> (los mismos que valida core-diagnostico): el trabajo se lee tal cual y el resultado se
/// serializa con las mismas claves y formato.
/// <para>Uso de ejemplo: <c>ResultadoConsultaDiagnostico.Fallo(trabajo, StatusCode.Unavailable, TimeSpan.FromSeconds(1))</c>.</para>
/// </summary>
public sealed class ContratoBusTests
{
    private static EventEnvelope Fixture(string nombre) =>
        EventEnvelope.FromJson(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bus", nombre)));

    [Fact]
    public void El_trabajo_del_fixture_se_lee_con_el_record_espejo_y_su_solicitud_es_el_Request_proto()
    {
        var sobre = Fixture("trabajo-consulta-diagnostico.v1.json");

        var trabajo = sobre.DataAs<TrabajoConsultaDiagnostico>();

        trabajo.TrabajoId.Should().Be(Guid.Parse("0192f3a0-0000-7000-8000-000000000201"));
        trabajo.ConsultaId.Should().Be(Guid.Parse("0192f3a0-0000-7000-8000-000000000301"));
        trabajo.PersonaId.Should().BeNull();
        (trabajo.Fuente, trabajo.Rpc).Should().Be((FuentesBus.Runt, RpcConsultas.PorPlaca));
        JsonParser.Default.Parse<ConsultarPorPlacaRequest>(trabajo.Solicitud.GetRawText()).Placa.Should().Be("TST001");
        sobre.Type.Should().StartWith(TrabajoConsultaDiagnostico.Prefijo + ".");
        sobre.Version.Should().Be(TrabajoConsultaDiagnostico.VersionContrato);
    }

    [Fact]
    public void El_resultado_OK_se_serializa_con_las_claves_y_el_formato_del_fixture()
    {
        var fixture = Fixture("resultado-consulta-diagnostico.v1.json");
        var trabajo = Fixture("trabajo-consulta-diagnostico.v1.json").DataAs<TrabajoConsultaDiagnostico>();
        var respuesta = fixture.Data.GetProperty("respuesta");

        var datos = JsonSerializer.SerializeToElement(
            ResultadoConsultaDiagnostico.Exito(trabajo, respuesta, TimeSpan.FromMilliseconds(1234), desdeCache: false), EventEnvelope.JsonOptions);

        Claves(datos).Should().Equal(Claves(fixture.Data), "mismas claves y en el mismo orden que el fixture");
        foreach (var clave in new[] { "trabajoId", "consultaId", "personaId", "fuente", "estado", "grpcStatus", "duracionMs", "desdeCache" })
            datos.GetProperty(clave).GetRawText().Should().Be(fixture.Data.GetProperty(clave).GetRawText(), clave);
        JsonElement.DeepEquals(datos.GetProperty("respuesta"), respuesta).Should().BeTrue("la respuesta viaja tal cual (proto-JSON)");
        fixture.Type.Should().Be(ResultadoConsultaDiagnostico.Tipo);
        fixture.Version.Should().Be(ResultadoConsultaDiagnostico.VersionContrato);
    }

    [Fact]
    public void El_resultado_ERROR_lleva_el_nombre_del_StatusCode_y_respuesta_null()
    {
        var trabajo = Fixture("trabajo-consulta-diagnostico.v1.json").DataAs<TrabajoConsultaDiagnostico>() with { PersonaId = Guid.NewGuid() };

        var datos = JsonSerializer.SerializeToElement(
            ResultadoConsultaDiagnostico.Fallo(trabajo, StatusCode.DeadlineExceeded, TimeSpan.FromMilliseconds(90_000.7)), EventEnvelope.JsonOptions);

        datos.GetProperty("estado").GetString().Should().Be("ERROR");
        datos.GetProperty("grpcStatus").GetString().Should().Be("DeadlineExceeded");
        datos.GetProperty("respuesta").ValueKind.Should().Be(JsonValueKind.Null);
        datos.GetProperty("duracionMs").GetInt32().Should().Be(90_000);
        datos.GetProperty("desdeCache").GetBoolean().Should().BeFalse();
        datos.GetProperty("personaId").GetGuid().Should().Be(trabajo.PersonaId!.Value);
        Claves(datos).Should().Equal(Claves(Fixture("resultado-consulta-diagnostico.v1.json").Data));
    }

    [Theory]
    [InlineData(false, "consultas.diagnostico.*")]
    [InlineData(true, "consultas.diagnostico-express.*")]
    public void La_cola_de_lotes_y_la_del_Express_se_atan_con_su_propio_comodin(bool esExpress, string patron) =>
        TrabajoConsultaDiagnostico.Patron(esExpress).Should().Be(patron);

    private static List<string> Claves(JsonElement objeto) => [.. objeto.EnumerateObject().Select(p => p.Name)];
}
