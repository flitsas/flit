using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>HU #14691 — tramite_pausado / tramite_reanudado en Modificaciones del status-history.</summary>
public sealed class StatusHistoryPausaHu14691Tests
{
  [Fact]
  public void Tipos_incluyeTramitePausadoYReanudado()
  {
    StatusHistoryModificacionLectura.Tipos().Should().Contain("tramite_pausado");
    StatusHistoryModificacionLectura.Tipos().Should().Contain("tramite_reanudado");
  }

  [Fact]
  public void TryProject_tramite_pausado_conObservacion_NuevoEsObservacion()
  {
    var at = DateTimeOffset.Parse("2026-11-24T12:00:00+00:00");
    var id = Guid.NewGuid();
    var actor = Guid.NewGuid();

    StatusHistoryModificacionLectura.TryProject(
      "tramite_pausado",
      id,
      at,
      actor,
      """{"observation":"Esperando documentos del cliente"}""",
      out var fila).Should().BeTrue();

    fila.Etiqueta.Should().Be("Trámite pausado");
    fila.Campo.Should().Be("pausa");
    fila.Anterior.Should().BeEmpty();
    fila.Nuevo.Should().Be("Esperando documentos del cliente");
    fila.EsActoAdministrativo.Should().BeTrue();
    fila.CreatedBy.Should().Be(actor);
  }

  [Fact]
  public void TryProject_tramite_pausado_sinObservacion_NuevoEsEtiqueta()
  {
    var at = DateTimeOffset.Parse("2026-11-24T12:05:00+00:00");

    StatusHistoryModificacionLectura.TryProject(
      "tramite_pausado",
      Guid.NewGuid(),
      at,
      null,
      """{"observation":null}""",
      out var fila).Should().BeTrue();

    fila.Nuevo.Should().Be("Trámite pausado");
    fila.Nuevo.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void TryProject_tramite_reanudado_conObservacion_NuevoEsObservacion()
  {
    StatusHistoryModificacionLectura.TryProject(
      "tramite_reanudado",
      Guid.NewGuid(),
      DateTimeOffset.UtcNow,
      Guid.NewGuid(),
      """{"observation":"Cliente envió lo solicitado"}""",
      out var fila).Should().BeTrue();

    fila.Etiqueta.Should().Be("Trámite reanudado");
    fila.Campo.Should().Be("reanudacion");
    fila.Anterior.Should().BeEmpty();
    fila.Nuevo.Should().Be("Cliente envió lo solicitado");
  }

  [Fact]
  public void TryProject_tramite_reanudado_observacionVacia_NuevoEsEtiqueta()
  {
    StatusHistoryModificacionLectura.TryProject(
      "tramite_reanudado",
      Guid.NewGuid(),
      DateTimeOffset.UtcNow,
      null,
      """{"observation":""}""",
      out var fila).Should().BeTrue();

    fila.Nuevo.Should().Be("Trámite reanudado");
  }
}