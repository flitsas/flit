using System.Collections.Generic;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class TraspasoPartesTests
{
    // HU #11019 — el correo compartido se DETECTA (dato) pero ya NO bloquea: es legítimo que ambas
    // partes usen el mismo buzón. Solo el mismo documento las convierte en la misma persona.
    [Fact]
    public void DetectarDuplicadas_MismoEmail_NoBloquea()
    {
        var dup = TraspasoPartes.DetectarDuplicadas(
            new ParteDatos("V", "1000445469", "a@x.co"),
            new ParteDatos("C", "1018232363", "A@X.CO"));

        dup.MismoEmail.Should().BeTrue();
        dup.MismoDocumento.Should().BeFalse();
        TraspasoPartes.MensajeDuplicadas(dup).Should().BeNull();
    }

    [Fact]
    public void DetectarDuplicadas_MismoDocumentoNormalizado()
    {
        var dup = TraspasoPartes.DetectarDuplicadas(
            new ParteDatos("V", "1.000.445.469", "v@x.co"),
            new ParteDatos("C", "1000445469", "c@x.co"));

        dup.MismoDocumento.Should().BeTrue();
        TraspasoPartes.MensajeDuplicadas(dup).Should().Contain("documento");
    }

    [Fact]
    public void DetectarDuplicadas_PartesDistintas_MensajeNull()
    {
        var dup = TraspasoPartes.DetectarDuplicadas(
            new ParteDatos("V", "111", "v@x.co"),
            new ParteDatos("C", "222", "c@x.co"));

        TraspasoPartes.MensajeDuplicadas(dup).Should().BeNull();
    }

    [Fact]
    public void DetectarDuplicadas_AmbosIguales_ReportaSoloElDocumento()
    {
        var dup = TraspasoPartes.DetectarDuplicadas(
            new ParteDatos("V", "111", "x@x.co"),
            new ParteDatos("C", "111", "x@x.co"));

        // HU #11019 — con documento y correo iguales el bloqueo lo causa el DOCUMENTO.
        TraspasoPartes.MensajeDuplicadas(dup).Should().Contain("documento");
        TraspasoPartes.MensajeDuplicadas(dup).Should().NotContain("correo");
    }

    [Fact]
    public void RequiereReenvio_RespetaEstadosVigentes()
    {
        TraspasoPartes.RequiereReenvio(null).Should().BeTrue();
        TraspasoPartes.RequiereReenvio(new ValidacionRow(1, null, null, "aprobado")).Should().BeFalse();
        TraspasoPartes.RequiereReenvio(new ValidacionRow(1, null, null, "enviado")).Should().BeFalse();
        TraspasoPartes.RequiereReenvio(new ValidacionRow(1, null, null, "en_proceso")).Should().BeFalse();
        TraspasoPartes.RequiereReenvio(new ValidacionRow(1, null, null, "rechazado")).Should().BeTrue();
        TraspasoPartes.RequiereReenvio(new ValidacionRow(1, null, null, "expirado")).Should().BeTrue();
    }

    [Fact]
    public void ResolverValidacionParte_PriorizaAprobadoSobreRechazado()
    {
        var rows = new List<ValidacionRow>
        {
            new(1, "100", "vendedor", "rechazado"),
            new(2, "100", "vendedor", "aprobado"),
        };
        var v = TraspasoPartes.ResolverValidacionParte(rows, "100", "vendedor");
        v!.Estado.Should().Be("aprobado");
        v.Id.Should().Be(2);
    }

    [Fact]
    public void ResolverValidacionParte_IgnoraDocumentoAnterior()
    {
        var rows = new List<ValidacionRow>
        {
            new(1, "999", "comprador", "aprobado"), // documento viejo
            new(2, "100", "comprador", "enviado"),  // documento corregido
        };
        var v = TraspasoPartes.ResolverValidacionParte(rows, "100", "comprador");
        v!.Id.Should().Be(2);
        v.Documento.Should().Be("100");
    }

    [Fact]
    public void ResolverValidacionParte_DocumentoSinMatch_NoDevuelveStalePorRol()
    {
        var rows = new List<ValidacionRow> { new(1, "999", "comprador", "aprobado") };
        TraspasoPartes.ResolverValidacionParte(rows, "100", "comprador").Should().BeNull();
    }

    [Fact]
    public void ResolverValidacionParte_FilaLegacySinDocumento_PorRol()
    {
        var rows = new List<ValidacionRow> { new(1, null, "comprador", "aprobado") };
        TraspasoPartes.ResolverValidacionParte(rows, "100", "comprador")!.Id.Should().Be(1);
    }

    [Fact]
    public void ResolverVigentePorDocumento_EligeAprobadaPeseAReenvioPosterior()
    {
        var rows = new List<ValidacionRow>
        {
            new(29, "1000445469", null, "enviado"),
            new(28, "1000445469", null, "aprobado"),
            new(27, "1193552679", null, "rechazado"),
        };
        var v = TraspasoPartes.ResolverVigentePorDocumento(rows, "1000445469");
        v!.Id.Should().Be(28);
        v.Estado.Should().Be("aprobado");
    }

    [Fact]
    public void ResolverVigentePorDocumento_DocumentoConocidoNoDevuelveOtraPersona()
    {
        var rows = new List<ValidacionRow>
        {
            new(28, "1000445469", null, "aprobado"),
        };
        TraspasoPartes.ResolverVigentePorDocumento(rows, "999999").Should().BeNull();
    }

    [Fact]
    public void ResolverVigentePorDocumento_NormalizaDocumento()
    {
        var rows = new List<ValidacionRow> { new(28, "1000445469", null, "aprobado") };
        TraspasoPartes.ResolverVigentePorDocumento(rows, "1.000.445.469")!.Id.Should().Be(28);
    }

    [Fact]
    public void ResolverVigentePorDocumento_SinDocumento_MayorEstado()
    {
        var rows = new List<ValidacionRow>
        {
            new(29, "1000445469", null, "enviado"),
            new(28, "1000445469", null, "aprobado"),
            new(27, "1193552679", null, "rechazado"),
        };
        TraspasoPartes.ResolverVigentePorDocumento(rows, null)!.Id.Should().Be(28);
    }

    [Fact]
    public void ResolverVigentePorDocumento_ListaVacia_Null()
    {
        TraspasoPartes.ResolverVigentePorDocumento([], "1").Should().BeNull();
        TraspasoPartes.ResolverVigentePorDocumento(null, null).Should().BeNull();
    }
}
