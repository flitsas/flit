using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #14153 AC1 / hallazgo H1 de seguridad — la respuesta HTTP de <c>RunConsultation</c>
/// (<c>POST /api/v1/tramites/instances/{id}/consultations/{templateCode}</c>, <c>Results.Ok(result)</c>) no expone el crudo del
/// proveedor. core-api lo recibe siempre de core-consultas (pide <c>incluir_respuesta_cruda</c>) y con la HU #14153 trae también el
/// conductor (licencias, documento en <see cref="RawProviderPayload.SubjectKey"/>); Trámites debe seguir recibiendo lo de siempre.
/// Se serializa con <see cref="JsonSerializerDefaults.Web"/>, que es lo que usa la Minimal API (Flit.Api no configura opciones JSON).
/// <para>Uso de ejemplo: <c>JsonSerializer.Serialize(resultadoConCrudo, Web)</c> → sin <c>rawPayload</c> ni el contenido del crudo.</para>
/// </summary>
public sealed class ConsultationResultContratoJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ConsultationResult ResultadoConCrudo() => new(
        Provider: "kyverum_runt_conductor",
        Overall: "ok",
        Checks: [new ConsultationCheck("runt_persona", "RUNT persona", "ok", "runt", null)],
        HydratedFields: [new HydratedField("comprador_nombre", "PERSONA PRUEBA", null)],
        RawPayload: new RawProviderPayload(
            ProviderKey: "kyverum_runt_conductor",
            SubjectKind: "person",
            SubjectKey: "CC-9999999999",
            PayloadJson: "{\"licencias\":[{\"numero\":\"LIC-CRUDO-123\"}]}",
            QueriedAt: DateTimeOffset.Parse("2026-10-28T10:00:00Z")));

    [Fact]
    public void Respuesta_de_RunConsultation_no_serializa_el_crudo_del_proveedor()
    {
        var json = JsonSerializer.Serialize(ResultadoConCrudo(), Web);

        json.Should().NotContain("rawPayload");
        json.Should().NotContain("RawPayload");
        json.Should().NotContain("CC-9999999999");
        json.Should().NotContain("LIC-CRUDO-123");
        json.Should().NotContain("licencias");
    }

    [Fact]
    public void Respuesta_de_RunConsultation_conserva_el_contrato_de_siempre()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ResultadoConCrudo(), Web));
        var raiz = doc.RootElement;

        raiz.GetProperty("provider").GetString().Should().Be("kyverum_runt_conductor");
        raiz.GetProperty("overall").GetString().Should().Be("ok");
        raiz.GetProperty("checks").GetArrayLength().Should().Be(1);
        raiz.GetProperty("hydratedFields").GetArrayLength().Should().Be(1);
        raiz.TryGetProperty("fallo", out _).Should().BeFalse();
    }
}
