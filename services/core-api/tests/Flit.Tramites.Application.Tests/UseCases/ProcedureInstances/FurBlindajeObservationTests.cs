using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bloque del párrafo 23 que declara qué blindaje se solicita.
///
/// <para>Antes de este bloque el trámite solo persistía un booleano, así que un nivel 1, un nivel 3 y
/// un desmonte producían el mismo FUR: la única marca es «vehículo blindado SI/NO» y no distingue el
/// nivel — con el desmonte ni siquiera dice que hubo trámite, porque cae en NO igual que un vehículo
/// que nunca estuvo blindado.</para>
///
/// <para>Los literales son los mismos que replica la vista previa del asistente
/// (<c>frontend/lib/catalogs/blindaje.ts</c>); si cambian aquí, cambian allí.</para>
/// </summary>
public sealed class FurBlindajeObservationTests
{
    [Theory]
    [InlineData(BlindajeOpcion.Nivel1, "BLINDAJE NIVEL 1.")]
    [InlineData(BlindajeOpcion.Nivel2, "BLINDAJE NIVEL 2.")]
    [InlineData(BlindajeOpcion.Nivel3, "BLINDAJE NIVEL 3.")]
    public void Compose_CadaNivelSeDeclaraConSuNumero(BlindajeOpcion opcion, string esperado)
    {
        FurBlindajeObservation.Compose(opcion).Should().Be(esperado);
    }

    [Fact]
    public void Compose_Desmonte_DeclaraElRetiro()
    {
        // Es el caso que la casilla no puede expresar: cae en NO, igual que un vehículo sin blindar.
        FurBlindajeObservation.Compose(BlindajeOpcion.Desmonte).Should().Be("DESMONTE DE BLINDAJE.");
    }

    [Fact]
    public void Compose_SinOpcion_NoInventaContenido()
    {
        // Regla del artefacto: faltan datos ⇒ sí casilla, NO se inventa el texto. Un trámite de
        // blindaje abierto antes de que la opción existiera entra por aquí.
        FurBlindajeObservation.Compose(BlindajeOpcion.Ninguna).Should().BeNull();
    }

    [Theory]
    [InlineData("NIVEL_2", "BLINDAJE NIVEL 2.")]
    [InlineData("  desmonte ", "DESMONTE DE BLINDAJE.")]
    [InlineData("NIVEL_9", null)]
    [InlineData(null, null)]
    public void Compose_DesdeElValorPersistido(string? valor, string? esperado)
    {
        FurBlindajeObservation.Compose(valor).Should().Be(esperado);
    }

    [Fact]
    public void Compose_CabeDeSobraEnElPresupuestoDelRecuadro()
    {
        // El bloque automático entra ÍNTEGRO y le quita sitio al texto libre del gestor
        // (FurObservacionesComposer): por eso los literales son cortos a propósito.
        foreach (var opcion in new[]
                 {
                     BlindajeOpcion.Nivel1, BlindajeOpcion.Nivel2,
                     BlindajeOpcion.Nivel3, BlindajeOpcion.Desmonte,
                 })
        {
            FurBlindajeObservation.Compose(opcion)!.Length
                .Should().BeLessThan(FurObservacionesComposer.PresupuestoCaracteres / 10);
        }
    }
}
