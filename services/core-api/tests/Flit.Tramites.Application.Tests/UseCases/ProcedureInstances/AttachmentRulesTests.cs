using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class AttachmentRulesTests
{
    // HU #10590 — DocTipos del checklist de traspaso unilateral aceptados por la whitelist
    // (antes → 400 invalid_tipo, el front no podía subir el documento).
    [Theory]
    [InlineData("paz_salvo_locatario")]
    [InlineData("doc_locatario")]
    [InlineData("contrato_leasing")]
    [InlineData("declaracion_arrendadora")]
    public void Validate_AcceptsTraspasoUnilateralDocTipos(string tipo)
    {
        AttachmentRules.ValidTipos.Should().Contain(tipo);
        AttachmentRules.Validate(tipo, "application/pdf", 1024).Should().BeNull();
    }

    [Theory]
    [InlineData("PAZ_SALVO_LOCATARIO")]
    [InlineData("Doc_Locatario")]
    public void Validate_DocTiposAreCaseInsensitive(string tipo)
    {
        AttachmentRules.Validate(tipo, "application/pdf", 1024).Should().BeNull();
    }

    [Fact]
    public void Validate_UnknownTipo_ReturnsInvalidTipo()
    {
        AttachmentRules.Validate("no_existe", "application/pdf", 1024).Should().Be("invalid_tipo");
    }
}
