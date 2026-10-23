using Flit.Modules.Quipux.Domain.Envios;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Envios;

/// <summary>
/// Bug #13912 — el claim hace <c>attempts += 1</c> antes de procesar; la consulta previa solo
/// aplica a partir del segundo claim (hubo un ciclo previo que pudo haber POSTeado a Quipux).
/// </summary>
public sealed class QuipuxSubmissionConsultaPreviaTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void RequiereConsultaPrevia_respeta_umbral_tras_claim(int attempts, bool esperado)
    {
        var submission = new QuipuxSubmission
        {
            Status = QuipuxSubmissionEstado.Pendiente,
            Attempts = attempts,
        };

        submission.RequiereConsultaPrevia.Should().Be(esperado);
    }

    [Fact]
    public void RequiereConsultaPrevia_false_si_ya_no_esta_pendiente()
    {
        var submission = new QuipuxSubmission
        {
            Status = QuipuxSubmissionEstado.Registrado,
            Attempts = 3,
        };

        submission.RequiereConsultaPrevia.Should().BeFalse();
    }
}
