using FluentAssertions;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

// Casilla 18 del FUR — normalizador compartido de `vehicle_service` (field_values). El campo tiene
// dos orígenes: texto libre del RUNT (traspaso) y el código elegido por el operador (matrícula
// inicial). Resolve() reduce cualquiera de las dos formas a UN código canónico para que el FUR
// marque exactamente una casilla.
public sealed class VehicleServiceTypeTests
{
    // ── Los 6 códigos cerrados (matrícula inicial): ya vienen normalizados, pasan tal cual ──
    [Theory]
    [InlineData("PARTICULAR", VehicleServiceTypeCode.Particular)]
    [InlineData("PUBLICO", VehicleServiceTypeCode.Publico)]
    [InlineData("DIPLOMATICO", VehicleServiceTypeCode.Diplomatico)]
    [InlineData("OFICIAL", VehicleServiceTypeCode.Oficial)]
    [InlineData("ESPECIAL", VehicleServiceTypeCode.Especial)]
    [InlineData("OTROS", VehicleServiceTypeCode.Otros)]
    [InlineData("particular", VehicleServiceTypeCode.Particular)] // case-insensitive
    public void Resolve_codigos_cerrados_de_matricula_inicial(string codigo, string esperado)
    {
        VehicleServiceTypeCode.Resolve(codigo).Should().Be(esperado);
    }

    // ── Texto libre del RUNT: cada tipo real observado en traspaso ──
    [Theory]
    [InlineData("Particular", VehicleServiceTypeCode.Particular)]
    [InlineData("PUBLICO", VehicleServiceTypeCode.Publico)]
    [InlineData("SERVICIO PUBLICO", VehicleServiceTypeCode.Publico)]
    [InlineData("PUBLIC", VehicleServiceTypeCode.Publico)]
    [InlineData("DIPLOMATICO", VehicleServiceTypeCode.Diplomatico)]
    [InlineData("CUERPO DIPLOMATICO", VehicleServiceTypeCode.Diplomatico)]
    [InlineData("OFICIAL", VehicleServiceTypeCode.Oficial)]
    [InlineData("VEHICULO OFICIAL", VehicleServiceTypeCode.Oficial)]
    [InlineData("ESPECIAL", VehicleServiceTypeCode.Especial)]
    [InlineData("SERVICIO ESPECIAL", VehicleServiceTypeCode.Especial)]
    [InlineData("OTRO", VehicleServiceTypeCode.Otros)]
    [InlineData("OTROS", VehicleServiceTypeCode.Otros)]
    public void Resolve_texto_libre_del_runt_por_tipo(string texto, string esperado)
    {
        VehicleServiceTypeCode.Resolve(texto).Should().Be(esperado);
    }

    // ── El caso compuesto real que originó el bug: dos casillas marcadas a la vez ──
    [Theory]
    [InlineData("SERVICIO PUBLICO ESPECIAL")]
    [InlineData("PUBLICO ESPECIAL")]
    [InlineData("servicio publico especial")] // minúsculas
    public void Resolve_texto_compuesto_publico_especial_resuelve_a_especial(string texto)
    {
        // Precedencia elegida: ESPECIAL es la clasificación específica que ya condiciona una regla de
        // negocio aguas abajo (RF33, TramiteDocumentContextMapper), así que gana sobre el calificador
        // genérico PUBLICO — ver el comentario de Resolve() para la justificación completa.
        VehicleServiceTypeCode.Resolve(texto).Should().Be(VehicleServiceTypeCode.Especial);
    }

    [Theory]
    [InlineData("OFICIAL PUBLICO")]
    [InlineData("PUBLICO OFICIAL")]
    public void Resolve_texto_compuesto_publico_oficial_resuelve_a_oficial(string texto)
    {
        VehicleServiceTypeCode.Resolve(texto).Should().Be(VehicleServiceTypeCode.Oficial);
    }

    [Theory]
    [InlineData("DIPLOMATICO PUBLICO")]
    public void Resolve_texto_compuesto_publico_diplomatico_resuelve_a_diplomatico(string texto)
    {
        VehicleServiceTypeCode.Resolve(texto).Should().Be(VehicleServiceTypeCode.Diplomatico);
    }

    // ── Vacío: se mantiene el comportamiento histórico (cae a Particular) ──
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_vacio_cae_a_particular(string? texto)
    {
        VehicleServiceTypeCode.Resolve(texto).Should().Be(VehicleServiceTypeCode.Particular);
    }

    // ── Desconocido: no se marca ninguna casilla (comportamiento histórico: ningún Contains coincidía) ──
    [Theory]
    [InlineData("TAXI")]
    [InlineData("ALQUILER")]
    [InlineData("XYZ-123")]
    public void Resolve_texto_desconocido_devuelve_null(string texto)
    {
        VehicleServiceTypeCode.Resolve(texto).Should().BeNull();
    }
}
