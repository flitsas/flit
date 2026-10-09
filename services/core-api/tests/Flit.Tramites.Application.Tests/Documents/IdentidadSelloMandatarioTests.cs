using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>
/// El sello del mandatario lleva la misma leyenda que el de las partes (documento, UUID, firma y aprobación). Su
/// validación no vence a los 30 días: «Vence» es el fin de su vigencia por rango y no aparece con vigencia fija.
/// </summary>
public sealed class IdentidadSelloMandatarioTests
{
    private static ProcedureInstanceBiometricValidation Validacion() => new()
    {
        Id = Guid.NewGuid(),
        DocumentType = "CC",
        DocumentNumber = "1020304050",
        KyverumVerificationId = "22fe14fc-798b-44ca-b8a9-f4bd60ea6faf",
        CertificateHash = "KV-ABC123",
        // 15:00 UTC = 10:00 en Bogotá, mismo día.
        ValidatedAt = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero),
        ValidUntil = new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void VigenciaFija_MismaLeyendaQueLasPartes_SinVence()
    {
        IdentidadSelloText.BuildMandatario(Validacion(), vigenteHasta: null).Should().Be(
            "Validación biométrica CC 1020304050\nUUID 22fe14fc-798b-44ca-b8a9-f4bd60ea6faf\nFirma KV-ABC123\nAprob 2026/10/08");
    }

    [Fact]
    public void VigenciaPorRango_VenceEsElFinDeSuVigencia_NoLosTreintaDias()
    {
        IdentidadSelloText.BuildMandatario(Validacion(), new DateOnly(2027, 3, 31))
            .Should().EndWith("Aprob 2026/10/08 · Vence 2027/03/31");
    }

    [Fact]
    public void ElSelloDeLasPartes_NoCambia()
    {
        IdentidadSelloText.Build(Validacion()).Should().EndWith("Firma KV-ABC123\nAprob 2026/10/08 · Vence 2026/11/07");
    }
}
