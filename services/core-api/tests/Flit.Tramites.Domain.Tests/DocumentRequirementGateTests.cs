using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-04 (CFD-06) — gate puro del paso de documentos.
/// Cubre BE-04-AC-03 (obligatorio faltante bloquea) y AC-04 (dummy no bloquea).
/// </summary>
public sealed class DocumentRequirementGateTests
{
    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>();

    [Fact]
    public void MissingRequired_RequiredNotUploaded_ReturnsBlocker()
    {
        // BE-04-AC-03
        var reqs = new[] { new DocumentRequirementItem("CEDULA", IsRequired: true, IsDummy: false) };

        var blockers = DocumentRequirementGate.MissingRequired(reqs, Nothing);

        blockers.Should().ContainSingle().Which.Should().Be("DOCUMENT_CEDULA_REQUIRED");
    }

    [Fact]
    public void MissingRequired_DummyDocument_DoesNotBlock()
    {
        // BE-04-AC-04
        var reqs = new[] { new DocumentRequirementItem("PROMESA", IsRequired: true, IsDummy: true) };

        var blockers = DocumentRequirementGate.MissingRequired(reqs, Nothing);

        blockers.Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_RequiredUploaded_DoesNotBlock()
    {
        var reqs = new[] { new DocumentRequirementItem("CEDULA", IsRequired: true, IsDummy: false) };
        var uploaded = new HashSet<string> { "CEDULA" };

        DocumentRequirementGate.MissingRequired(reqs, uploaded).Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_OptionalDocument_DoesNotBlock()
    {
        var reqs = new[] { new DocumentRequirementItem("FOTO", IsRequired: false, IsDummy: false) };

        DocumentRequirementGate.MissingRequired(reqs, Nothing).Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_MixedSet_ReturnsOnlyMissingRequiredNonDummy()
    {
        var reqs = new[]
        {
            new DocumentRequirementItem("CEDULA", true, false),   // falta → bloquea
            new DocumentRequirementItem("PROMESA", true, true),   // dummy → no
            new DocumentRequirementItem("SOAT", true, false),     // cargado → no
            new DocumentRequirementItem("FOTO", false, false),    // opcional → no
        };
        var uploaded = new HashSet<string> { "SOAT" };

        var blockers = DocumentRequirementGate.MissingRequired(reqs, uploaded);

        blockers.Should().ContainSingle().Which.Should().Be("DOCUMENT_CEDULA_REQUIRED");
    }

    [Fact]
    public void MissingRequired_SystemGenerated_DoesNotBlock_AunqueSeaObligatorio()
    {
        var reqs = new[]
        {
            new DocumentRequirementItem("soat", IsRequired: true, IsDummy: false, IsSystemGenerated: true),
        };

        DocumentRequirementGate.MissingRequired(reqs, Nothing).Should().BeEmpty();
    }
}
