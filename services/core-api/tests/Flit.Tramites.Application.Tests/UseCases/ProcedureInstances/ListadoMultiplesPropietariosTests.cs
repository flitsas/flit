using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0053 (Múltiple Propietario) en el listado de trámites: la fila trae a TODOS los propietarios
/// de cada lado —principal primero— con la acreditación de CADA persona, y la marca
/// <c>MultiplesPropietarios</c> cuando algún lado tiene 2+. Antes la fila tomaba un solo actor por
/// rol y los copropietarios no aparecían en la tabla.
/// </summary>
public sealed class ListadoMultiplesPropietariosTests
{
    [Fact]
    public void ConCopropietarios_TraeTodosEnOrdenYMarcaLaFila()
    {
        var instance = Traspaso();
        // Llegan desordenados a propósito: el grafo no garantiza el orden y el principal es el Ordinal 1.
        instance.Actors.Add(Actor("vendedor", "Luis Dos", ordinal: 2));
        instance.Actors.Add(Actor("vendedor", "Ana Uno", ordinal: 1));
        instance.Actors.Add(Actor("comprador", "Eva Tres", ordinal: 1));

        var fila = ListProcedureInstancesHandler.ToSummary(instance, new HashSet<string>());

        fila.VendedoresActores!.Select(a => a.Nombre).Should().Equal("Ana Uno", "Luis Dos");
        fila.CompradoresActores!.Select(a => a.Nombre).Should().Equal("Eva Tres");
        fila.VendedorNombre.Should().Be("Ana Uno", "la columna de un solo nombre sigue mostrando al principal");
        fila.MultiplesPropietarios.Should().BeTrue();
    }

    [Fact]
    public void CadaCopropietario_LlevaSuPropiaFirma()
    {
        var instance = Traspaso();
        var ana = Actor("vendedor", "Ana Uno", ordinal: 1);
        var luis = Actor("vendedor", "Luis Dos", ordinal: 2);
        instance.Actors.Add(ana);
        instance.Actors.Add(luis);
        instance.Actors.Add(Actor("comprador", "Eva Tres", ordinal: 1));

        // Solo Ana acreditó su identidad: el lado sigue «pendiente» (todos firman), pero el desplegable
        // tiene que decir quién ya firmó y quién no.
        var aprobados = new HashSet<ProcedureInstanceActor>(ReferenceEqualityComparer.Instance) { ana };
        var fila = ListProcedureInstancesHandler.ToSummary(
            instance, new HashSet<string>(), actoresAprobados: aprobados);

        fila.VendedoresActores.Should().Equal(
            new ActorListadoDto("Ana Uno", FirmaParteEstados.Firmado, Ordinal: 1, NumeroDocumento: "1018"),
            new ActorListadoDto("Luis Dos", FirmaParteEstados.Pendiente, Ordinal: 2, NumeroDocumento: "1028"));
        fila.FirmaVendedorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public void UnPropietarioPorLado_NoMarcaLaFila()
    {
        var instance = Traspaso();
        instance.Actors.Add(Actor("vendedor", "Ana Uno", ordinal: 1));
        instance.Actors.Add(Actor("comprador", "Eva Tres", ordinal: 1));

        var fila = ListProcedureInstancesHandler.ToSummary(instance, new HashSet<string>());

        fila.MultiplesPropietarios.Should().BeFalse();
        fila.VendedoresActores!.Select(a => a.Nombre).Should().Equal("Ana Uno");
    }

    [Fact]
    public void SinActoresDelLado_LaListaEsNula()
    {
        var instance = Traspaso();
        instance.Actors.Add(Actor("comprador", "Eva Tres", ordinal: 1));

        var fila = ListProcedureInstancesHandler.ToSummary(instance, new HashSet<string>());

        fila.VendedoresActores.Should().BeNull();
        fila.VendedorNombre.Should().BeNull();
    }

    private static ProcedureInstance Traspaso() => new()
    {
        ProcedureType = ProcedureTypeFixture.For("traspaso"),
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000002",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceActor Actor(string rol, string nombre, int ordinal) => new()
    {
        Id = Guid.NewGuid(),
        ActorType = rol,
        DocumentType = "CC",
        DocumentNumber = $"10{ordinal}{rol.Length}",
        FullName = nombre,
        Ordinal = ordinal,
    };
}
