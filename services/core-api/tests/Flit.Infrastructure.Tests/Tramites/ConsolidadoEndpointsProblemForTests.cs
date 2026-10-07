using Flit.Api.Endpoints.Tramites;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Flit.Infrastructure.Tests.Tramites;

/// <summary>
/// HU #13375 — no regresión del contrato HTTP de <c>ConsolidadoEndpoints.ProblemFor</c> tras extraer sus textos al
/// catálogo <c>ConsolidadoErrorTextos</c>: mismo status, title y detail para cada código (POST de generación y GET de
/// entrega del consolidado, HU #12785).
/// <para>Uso de ejemplo: <c>ConsolidadoEndpoints.ProblemFor("sin_adjuntos")</c> ⇒ 409 «No hay adjuntos para consolidar.».</para>
/// </summary>
public sealed class ConsolidadoEndpointsProblemForTests
{
    [Theory]
    [InlineData("not_found", 404, "Not Found", "Procedure instance not found.")]
    [InlineData("migrado_solo_lectura", 409, "Conflict", "Trámite migrado (solo lectura): no se regenera el consolidado.")]
    [InlineData("modalidad_no_soportada", 409, "Conflict", "El consolidado solo está disponible para matrícula inicial y traspaso.")]
    [InlineData("fur_requerido", 409, "Conflict", "Debe generar el FUR antes del consolidado.")]
    [InlineData("sin_adjuntos", 409, "Conflict", "No hay adjuntos para consolidar.")]
    [InlineData("adjunto_no_disponible", 409, "Conflict", "Un adjunto del expediente no está disponible en almacenamiento.")]
    [InlineData("mimetype_no_soportado", 409, "Conflict", "Un adjunto tiene un formato no soportado para el consolidado.")]
    [InlineData("storage_unavailable", 503, "Service Unavailable", "No se pudo guardar el consolidado en el almacenamiento de archivos. Intenta de nuevo en unos minutos.")]
    [InlineData("organismo_requerido", 409, "Conflict", "El organismo de tránsito del trámite no está seleccionado o no está activo en el sistema. Verifícalo antes de generar el expediente consolidado.")]
    [InlineData("cualquier_otro", 409, "Conflict", "No se pudo generar el expediente consolidado: cualquier_otro.")]
    public void ProblemFor_ContratoSinCambios(string error, int status, string title, string detail)
    {
        var result = ConsolidadoEndpoints.ProblemFor(error).Should().BeOfType<ProblemHttpResult>().Subject;

        result.StatusCode.Should().Be(status);
        result.ProblemDetails.Status.Should().Be(status);
        result.ProblemDetails.Title.Should().Be(title);
        result.ProblemDetails.Detail.Should().Be(detail);
    }
}
