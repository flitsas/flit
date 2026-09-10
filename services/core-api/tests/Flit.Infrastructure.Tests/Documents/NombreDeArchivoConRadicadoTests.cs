using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #12371 AC8 — los documentos se llaman por el radicado nuevo, y TODOS igual:
/// <c>mandato_FT1-0000012.pdf</c>, no <c>mandato_FT1_0000012.pdf</c>.
///
/// <para>Cada generador limpia el radicado con su propio <c>SafeRef</c> antes de ponerlo en el
/// nombre. Con el número pelado daba igual; con <c>FT1-0000012</c> dos de ellos convertían el
/// guion en <c>_</c> y el mismo trámite tenía un nombre en el FUR y otro en el mandato. Esta prueba
/// fija que el guion sobrevive en todos.</para>
/// </summary>
public sealed class NombreDeArchivoConRadicadoTests
{
    public static TheoryData<Type> Generadores() =>
    [
        typeof(MandatoPdfGenerator),
        typeof(SolicitudVirtualPdfGenerator),
        typeof(FurOverlayDocumentGenerator),
    ];

    [Theory]
    [MemberData(nameof(Generadores))]
    public void SafeRef_ConservaElRadicadoTalCual(Type generador)
    {
        var safeRef = generador.GetMethod("SafeRef", BindingFlags.NonPublic | BindingFlags.Static);
        safeRef.Should().NotBeNull($"{generador.Name} limpia el radicado con un SafeRef privado");

        safeRef!.Invoke(null, ["FT1-0000012"]).Should().Be("FT1-0000012");
    }
}
