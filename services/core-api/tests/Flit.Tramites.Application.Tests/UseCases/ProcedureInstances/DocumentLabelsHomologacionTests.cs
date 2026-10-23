using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #12704 — pie del consolidado usa Ganador A24, no Secretaría en FUR.</summary>
public sealed class DocumentLabelsHomologacionTests
{
    [Fact]
    public void Display_Soat_EsGanadorA24()
    {
        DocumentLabels.Display("soat").Should().Be("SOAT");
        DocumentLabels.Display("soat").Should().NotBe("SOAT RUNT");
        DocumentLabels.Display("soat").Should().NotBe("SOAT vigente");
    }

    [Fact]
    public void Display_Fur_NoNombraSecretaria()
    {
        DocumentLabels.Display("fur").Should().Contain("FUR");
        DocumentLabels.Display("fur").Should().NotContain("Secretaría");
    }
}
