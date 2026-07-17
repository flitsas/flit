using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10688 (AC2/AC5) — el resolvedor único decide "quién valida la identidad" de una parte: el actor si
/// es natural, el representante legal si es jurídico (con documento). Sin regresión en persona natural.
/// </summary>
public sealed class IdentitySubjectResolverTests
{
    private const string RlMetadata =
        "{\"representanteLegal\":{\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"555\"," +
        "\"nombreCompleto\":\"Rep Legal\",\"email\":\"rl@x.com\",\"telefono\":\"3001112233\"}}";

    private static ProcedureInstanceActor Actor(
        string? personType, string metadata = "{}",
        string docType = "CC", string docNumber = "123",
        string fullName = "Juan Actor", string? email = "actor@x.com") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            PersonType = personType,
            DocumentType = docType,
            DocumentNumber = docNumber,
            FullName = fullName,
            Email = email,
            Metadata = metadata,
        };

    [Fact]
    public void For_PersonaNatural_DevuelveDatosDelActor()
    {
        var subject = IdentitySubjectResolver.For(Actor("natural"));

        subject.EsRepresentanteLegal.Should().BeFalse();
        subject.Nombre.Should().Be("Juan Actor");
        subject.TipoDocumento.Should().Be("CC");
        subject.NumeroDocumento.Should().Be("123");
        subject.Email.Should().Be("actor@x.com");
    }

    [Fact]
    public void For_PersonaJuridicaConRLConDocumento_DevuelveDatosDelRL()
    {
        var actor = Actor("juridical", RlMetadata, docType: "NIT", docNumber: "900123456", fullName: "ACME S.A.S.");

        var subject = IdentitySubjectResolver.For(actor);

        subject.EsRepresentanteLegal.Should().BeTrue();
        subject.Nombre.Should().Be("Rep Legal");
        subject.TipoDocumento.Should().Be("CC");
        subject.NumeroDocumento.Should().Be("555");
        subject.Email.Should().Be("rl@x.com"); // el correo de validación va al RL
    }

    [Fact]
    public void For_PersonaJuridicaSinDocumentoDelRL_CaeAlActor()
    {
        // RL con solo correo (Fase 1 no exige documento) → no se puede validar biométricamente al RL:
        // se conserva el comportamiento previo (documento del actor/NIT).
        var soloCorreo = "{\"representanteLegal\":{\"email\":\"rl@x.com\"}}";
        var actor = Actor("juridical", soloCorreo, docType: "NIT", docNumber: "900123456", fullName: "ACME S.A.S.");

        var subject = IdentitySubjectResolver.For(actor);

        subject.EsRepresentanteLegal.Should().BeFalse();
        subject.TipoDocumento.Should().Be("NIT");
        subject.NumeroDocumento.Should().Be("900123456");
    }

    [Fact]
    public void For_PersonaJuridicaMetadataVacio_CaeAlActor()
    {
        var actor = Actor("juridical", "{}", docType: "NIT", docNumber: "900123456");

        var subject = IdentitySubjectResolver.For(actor);

        subject.EsRepresentanteLegal.Should().BeFalse();
        subject.NumeroDocumento.Should().Be("900123456");
    }

    [Fact]
    public void For_PersonaJuridicaRLSinCorreo_UsaCorreoDelActor()
    {
        var sinCorreo = "{\"representanteLegal\":{\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"555\"}}";
        var actor = Actor("juridical", sinCorreo, docType: "NIT", docNumber: "900123456", email: "empresa@x.com");

        var subject = IdentitySubjectResolver.For(actor);

        subject.EsRepresentanteLegal.Should().BeTrue();
        subject.NumeroDocumento.Should().Be("555");
        subject.Email.Should().Be("empresa@x.com");
    }

    [Fact]
    public void For_MetadataInvalido_NoLanza_CaeAlActor()
    {
        var actor = Actor("juridical", "{not-json", docType: "NIT", docNumber: "900123456");

        var subject = IdentitySubjectResolver.For(actor);

        subject.EsRepresentanteLegal.Should().BeFalse();
        subject.NumeroDocumento.Should().Be("900123456");
    }
}
