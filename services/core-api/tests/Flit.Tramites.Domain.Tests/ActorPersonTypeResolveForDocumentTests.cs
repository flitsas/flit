using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// La naturaleza de la persona se resuelve por el DOCUMENTO. Es la invariante que sostiene el paso
/// de actores desde que el tipo de documento pasó a ser el único control (antes lo declaraba un
/// interruptor Persona Natural / Persona Jurídica aparte, y nada obligaba a que ambos coincidieran).
/// </summary>
public sealed class ActorPersonTypeResolveForDocumentTests
{
    // ── El NIT manda: lo declarado no puede contradecirlo ──────────────────────────

    [Theory]
    [InlineData("natural")]
    [InlineData("juridical")]
    [InlineData(null)]
    [InlineData("")]
    public void ConNit_SiempreEsJuridica_SinImportarLoDeclarado(string? declarado)
    {
        ActorPersonTypes.ResolveForDocument("NIT", declarado)
            .Should().Be(ActorPersonTypes.Juridical);
    }

    [Theory]
    [InlineData("nit")]
    [InlineData("  NIT  ")]
    public void ElNitSeReconoceSinImportarCajaNiEspacios(string documento)
    {
        ActorPersonTypes.ResolveForDocument(documento, "natural")
            .Should().Be(ActorPersonTypes.Juridical);
    }

    // ── Cualquier otro documento conserva lo declarado, tal cual ───────────────────

    [Theory]
    [InlineData("CC")]
    [InlineData("CE")]
    [InlineData("PAS")]
    public void SinNit_ConservaLoDeclarado(string documento)
    {
        ActorPersonTypes.ResolveForDocument(documento, "natural")
            .Should().Be(ActorPersonTypes.Natural);
        ActorPersonTypes.ResolveForDocument(documento, "juridical")
            .Should().Be(ActorPersonTypes.Juridical);
    }

    /// <summary>
    /// «Sin declarar» se conserva como tal y NO se rellena con <c>natural</c>: para
    /// <see cref="Flit.Tramites.Domain.Tramites.Services.ChecklistPersonTypeRules"/> ese vacío
    /// significa «no toques el checklist», y rellenarlo cambiaría en silencio qué documentos se le
    /// exigen a los clientes que nunca mandan el campo.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("basura")]
    public void SinNit_LoNoDeclarado_SigueSinDeclarar(string? declarado)
    {
        ActorPersonTypes.ResolveForDocument("CC", declarado).Should().BeNull();
    }
}
