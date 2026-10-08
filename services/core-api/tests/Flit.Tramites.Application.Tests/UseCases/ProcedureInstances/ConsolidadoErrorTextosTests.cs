using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13375 — catálogo único de textos del consolidado (<see cref="ConsolidadoErrorTextos"/>): la variante HTTP
/// (extraída de <c>ConsolidadoEndpoints.ProblemFor</c>, sin cambio de contrato) y la variante del CSV de omitidos.
/// <para>Uso de ejemplo: <c>ConsolidadoErrorTextos.ParaLote("sin_adjuntos")</c> ⇒ texto legible para el CSV;
/// <c>ConsolidadoErrorTextos.Problema("storage_unavailable")</c> ⇒ 503 con su <c>detail</c>.</para>
/// </summary>
public sealed class ConsolidadoErrorTextosTests
{
    [Theory]
    [InlineData(ConsolidadoLoteOmisiones.MigradoSoloLectura, "Trámite migrado sin consolidado")]
    [InlineData(ConsolidadoLoteOmisiones.FurRequerido, "El trámite no tiene FUR; no se pudo generar el consolidado")]
    [InlineData(ConsolidadoLoteOmisiones.ErrorTecnico, "No se pudo generar el consolidado, intente de nuevo")]
    [InlineData(ConsolidadoLoteOmisiones.AccesoRevocado, "Acceso revocado")]
    [InlineData(ConsolidadoLoteOmisiones.QuipuxSoloLectura, "Organismo en modo Quipux de solo lectura")]
    [InlineData(ConsolidadoLoteOmisiones.SinAdjuntos, "No hay adjuntos para consolidar")]
    // HU #13417 (DDL 134): el código lo usa #13418 al omitir el trámite de una hija sin consolidado (P1 = a).
    [InlineData(ConsolidadoLoteOmisiones.RedSinConsolidado, "Trámite de una compañía de la red sin consolidado: la red es de solo consulta")]
    public void ParaLote_LiteralesDelDiseno(string codigo, string esperado) =>
        ConsolidadoErrorTextos.ParaLote(codigo).Should().Be(esperado);

    [Fact]
    public void ParaLote_CubreLosDiezCodigosDelVocabulario_SinTextoDePantalla()
    {
        foreach (var codigo in ConsolidadoLoteOmisiones.Todos)
        {
            var texto = ConsolidadoErrorTextos.ParaLote(codigo);
            texto.Should().NotBeNullOrWhiteSpace(codigo);
            texto.Should().NotContain(codigo, "el CSV lleva texto legible, no el código");
            texto.Should().NotContain("Debe generar", "los textos de pantalla del gestor no sirven en un CSV");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("storage_unavailable")]
    [InlineData("consolidado_no_generado")]
    public void ParaLote_CodigoFueraDelVocabulario_Rechaza(string? codigo)
    {
        var act = () => ConsolidadoErrorTextos.ParaLote(codigo!);

        act.Should().Throw<ArgumentException>();
    }

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
    [InlineData("otra_causa", 409, "Conflict", "No se pudo generar el expediente consolidado: otra_causa.")]
    public void Problema_ContratoHttpIntacto(string error, int status, string title, string detail) =>
        ConsolidadoErrorTextos.Problema(error).Should().Be(new ConsolidadoProblema(status, title, detail));
}
