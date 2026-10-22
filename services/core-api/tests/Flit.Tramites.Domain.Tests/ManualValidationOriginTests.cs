using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13285 (Feature #13280 A3, Épica #13202) — <see cref="ManualValidationOrigin"/> distingue el origen de una fila de
/// validación: tramite | prevalidacion | mandatario | representante_legal.
/// <para>Uso: <c>ManualValidationOrigin.From(row)</c> (con <c>row.Person</c> cargada) o <c>From(row, person)</c>.</para>
/// </summary>
public sealed class ManualValidationOriginTests
{
    private static ProcedureInstanceBiometricValidation Fila(
        Guid? instance = null, Guid? signer = null, string? role = null, Guid? person = null) => new()
    {
        Id = Guid.NewGuid(), ProcedureInstanceId = instance, MandateSignerId = signer, PartyRole = role, PersonId = person,
    };

    [Fact]
    public void MandateSignerId_es_mandatario_aunque_tenga_otros_anclajes()
    {
        ManualValidationOrigin.From(Fila(signer: Guid.NewGuid(), role: "mandatario")).Should().Be("mandatario");
        ManualValidationOrigin.From(Fila(instance: Guid.NewGuid(), signer: Guid.NewGuid())).Should().Be("mandatario");
    }

    [Fact]
    public void PartyRole_mandatario_sin_ficha_tambien_es_mandatario()
    {
        ManualValidationOrigin.From(Fila(role: "mandatario")).Should().Be("mandatario");
    }

    [Fact]
    public void Con_tramite_es_tramite_incluso_si_la_persona_es_juridica()
    {
        var persona = new Person { PersonType = PersonTypes.Juridical };
        ManualValidationOrigin.From(Fila(instance: Guid.NewGuid(), role: "comprador"), persona).Should().Be("tramite");
    }

    [Fact]
    public void Standalone_de_persona_juridica_es_representante_legal()
    {
        var persona = new Person { PersonType = PersonTypes.Juridical };
        var fila = Fila(person: Guid.NewGuid());
        fila.Person = persona;

        ManualValidationOrigin.From(fila).Should().Be("representante_legal");
        ManualValidationOrigin.From(Fila(person: Guid.NewGuid()), persona).Should().Be("representante_legal");
    }

    [Fact]
    public void Standalone_natural_o_sin_persona_cargada_es_prevalidacion()
    {
        ManualValidationOrigin.From(Fila(person: Guid.NewGuid())).Should().Be("prevalidacion");
        ManualValidationOrigin.From(Fila(person: Guid.NewGuid()), new Person { PersonType = PersonTypes.Natural })
            .Should().Be("prevalidacion");
    }

    [Fact]
    public void Los_valores_del_contrato_son_estables()
    {
        ManualValidationOrigin.Todos.Should().Equal("tramite", "prevalidacion", "mandatario", "representante_legal");
    }
}
