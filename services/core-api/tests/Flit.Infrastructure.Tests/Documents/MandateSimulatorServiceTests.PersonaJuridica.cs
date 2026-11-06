using System.Reflection;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14413 AC4 — el simulador y el trámite identifican igual a un mandatario Persona jurídica. El mismo
/// candidato recorre los dos caminos reales de generación.
/// </summary>
public sealed partial class MandateSimulatorServiceTests
{
    private const string LiteralCuerpoPersonaJuridica =
        "Y de ACME GESTION S.A.S., con NIT No. 900123456-7, quien para los efectos del presente contrato se denominará EL MANDATARIO,";

    [Fact]
    public async Task HU14413_AC4_ElSimuladorYElTramite_UsanLosMismosLiteralesDePersonaJuridica()
    {
        var candidato = new MandateSignerCandidate(
            Mandatario, "ACME GESTION S.A.S.", "900123456-7", null, TipoDocumento: "NIT",
            SignerModel: MandateSignerOrigins.ModeloJuridica);
        var directorio = new DirectorioFijo(candidato);

        var capturing = new CapturingGenerator();
        var (service, _) = Build(conMandatario: true, generator: capturing, directory: directorio);
        var simulado = await service.PreviewAsync(
            new MandateSimulationRequest(
                Funza, "natural", MandatoAssignmentModeCodes.Signer, Mandatario, "traspaso_standard"),
            Ct);
        simulado.Success.Should().BeTrue();

        var tramite = await MandatoEnElTramiteAsync(MandatoAssignmentModeCodes.Signer, directorio);

        foreach (var data in new[] { capturing.Captured!, tramite })
        {
            CuerpoDe(data).Should().Contain(LiteralCuerpoPersonaJuridica);
            MandatoPdfGenerator.MandatarioIdentificacion(data.Mandatario, data)
                .Should().Equal("RAZÓN SOCIAL: ACME GESTION S.A.S.", "NIT: 900123456-7");
        }
    }

    private static string CuerpoDe(MandatoData data)
    {
        var parrafos = (List<IReadOnlyList<MandatoPdfGenerator.ParrafoSegmento>>)typeof(MandatoPdfGenerator)
            .GetMethod("BuildParrafos", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(
                null,
                [
                    data, data.Tramite.Partes[0], false, MandatoVariante.Municipio,
                    "TRASPASO", "ABC123", "Funza", "Funza", "1 de agosto de 2026",
                ])!;
        return string.Join("\n", parrafos.Select(p => string.Concat(p.Select(s => s.Texto))));
    }
}
