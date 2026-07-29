using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Estados;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #11051 — gate de generación documental del GESTOR: con el trámite en estado final su
/// documentación es la definitiva (la que el organismo de tránsito tuvo a la vista al aprobar) y el
/// gestor no la regenera. La regeneración interna del sistema NO consulta esta regla: la aprobación del
/// OT regenera por diseño (HU #10996).
/// </summary>
public sealed class GeneracionDocumentalEstadoTests
{
    // AC1/AC2 — estados finales: el gestor no genera ni regenera.
    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    public void EstadoFinal_BloqueaLaGeneracionDelGestor(string estado)
    {
        TramiteEstado.PermiteGeneracionDocumentalDelGestor(estado).Should().BeFalse();
    }

    // AC4 — estados en proceso (y rechazado): la generación procede como siempre.
    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    public void EstadoNoFinal_PermiteLaGeneracionDelGestor(string estado)
    {
        TramiteEstado.PermiteGeneracionDocumentalDelGestor(estado).Should().BeTrue();
    }

    // Un status desconocido o ausente no debe bloquear: el gate es sobre estados finales conocidos, y
    // los handlers ya tienen sus propias validaciones (not_found, migrado_solo_lectura, gates de FUR).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("subsanacion")]
    public void EstadoDesconocidoOAusente_NoBloquea(string? estado)
    {
        TramiteEstado.PermiteGeneracionDocumentalDelGestor(estado).Should().BeTrue();
    }

    // El gate se apoya en EsFinal: si mañana cambia la lista de estados finales, ambos deben moverse
    // juntos. Este test fija esa relación para que no se dupliquen criterios.
    [Fact]
    public void ElGate_EsExactamenteLaNegacionDeEsFinal()
    {
        foreach (var estado in TramiteEstado.Todos)
        {
            TramiteEstado.PermiteGeneracionDocumentalDelGestor(estado)
                .Should().Be(!TramiteEstado.EsFinal(estado), $"estado '{estado}'");
        }
    }

    // El código de error es contrato con el frontend (mensaje del aviso del wizard, HU #11053).
    [Fact]
    public void CodigoDeError_EsEstable()
    {
        TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal
            .Should().Be("generacion_bloqueada_estado_final");
    }
}
