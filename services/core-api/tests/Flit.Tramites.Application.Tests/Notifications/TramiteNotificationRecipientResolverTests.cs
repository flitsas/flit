using System.Text.Json;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Notifications;

/// <summary>
/// HU #11462 — resolución de destinatarios por tipo de persona (ADR-0045).
/// </summary>
public sealed class TramiteNotificationRecipientResolverTests
{
    private readonly TramiteNotificationRecipientResolver _sut = new();

    [Fact]
    public void ParteJuridica_ProduceDosDestinatariosDistintos()
    {
        var instance = Traspaso();
        var actor = JuridicalComprador(
            empresaEmail: "empresa@flit.test",
            rlEmail: "rl@flit.test",
            rlNombre: "Rep Legal");

        var result = _sut.Resolve(instance, [actor], []);

        result.Recipients.Should().HaveCount(2);
        result.Recipients[0].Kind.Should().Be(TramiteRecipientKind.Empresa);
        result.Recipients[0].Email.Should().Be("empresa@flit.test");
        result.Recipients[0].DisplayName.Should().Be("ACME S.A.S.");
        result.Recipients[1].Kind.Should().Be(TramiteRecipientKind.RepresentanteLegal);
        result.Recipients[1].Email.Should().Be("rl@flit.test");
        result.Recipients[1].DisplayName.Should().Be("Rep Legal");
        result.Gaps.Should().BeEmpty();
    }

    [Fact]
    public void CupoRlSinCorreo_NoSeRellenaConElDeLaEmpresa()
    {
        var actor = JuridicalComprador(empresaEmail: "empresa@flit.test", rlEmail: null, rlNombre: "Rep Sin Mail");

        var result = _sut.Resolve(Matricula(), [actor], []);

        result.Recipients.Should().ContainSingle();
        result.Recipients[0].Kind.Should().Be(TramiteRecipientKind.Empresa);
        result.Gaps.Should().ContainSingle(g =>
            g.Kind == TramiteRecipientKind.RepresentanteLegal
            && g.Role == "comprador");
        result.Recipients.Should().NotContain(r =>
            r.Kind == TramiteRecipientKind.RepresentanteLegal
            && r.Email == "empresa@flit.test");
    }

    [Fact]
    public void ParticipanteNoRespaldaParteJuridica()
    {
        var actor = JuridicalComprador(empresaEmail: null, rlEmail: null, rlNombre: null, metadata: "{}");
        var participant = new ProcedureInstanceParticipant
        {
            Rol = "comprador",
            Nombre = "Participante Portal",
            Email = "portal@flit.test",
        };

        var result = _sut.Resolve(Matricula(), [actor], [participant]);

        result.Recipients.Should().BeEmpty();
        result.Gaps.Should().HaveCount(2);
        result.Recipients.Select(r => r.Email).Should().NotContain("portal@flit.test");
    }

    [Fact]
    public void PersonaNatural_ParticipanteTienePrecedencia()
    {
        var actor = new ProcedureInstanceActor
        {
            ActorType = "comprador",
            PersonType = "natural",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "Juan Natural",
            Email = "actor@flit.test",
            Metadata = "{}",
        };
        var participant = new ProcedureInstanceParticipant
        {
            Rol = "comprador",
            Nombre = "Juan Portal",
            Email = "portal@flit.test",
        };

        var result = _sut.Resolve(Matricula(), [actor], [participant]);

        result.Recipients.Should().ContainSingle();
        result.Recipients[0].Kind.Should().Be(TramiteRecipientKind.Persona);
        result.Recipients[0].Email.Should().Be("portal@flit.test");
        result.Recipients[0].DisplayName.Should().Be("Juan Portal");
    }

    [Fact]
    public void LegacySinPersonTypeConNit_SeTrataComoJuridica()
    {
        var actor = new ProcedureInstanceActor
        {
            ActorType = "comprador",
            PersonType = null,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Legacy SAS",
            Email = "empresa@flit.test",
            Metadata = RlMetadata("rl@flit.test", "Rep Legacy"),
        };

        var result = _sut.Resolve(Matricula(), [actor], []);

        result.Recipients.Should().HaveCount(2);
        result.Recipients.Select(r => r.Kind).Should().BeEquivalentTo(
            [TramiteRecipientKind.Empresa, TramiteRecipientKind.RepresentanteLegal]);
    }

    [Fact]
    public void VendedorSoloSeNotificaEnTraspaso()
    {
        var comprador = Natural("comprador", "c@flit.test");
        var vendedor = Natural("vendedor", "v@flit.test");

        var soloMatricula = _sut.Resolve(Matricula(), [comprador, vendedor], []);
        soloMatricula.Recipients.Should().ContainSingle(r => r.Role == "comprador");
        soloMatricula.Recipients.Should().NotContain(r => r.Role == "vendedor");

        var traspaso = _sut.Resolve(Traspaso(), [comprador, vendedor], []);
        traspaso.Recipients.Should().HaveCount(2);
        traspaso.Recipients.Select(r => r.Role).Should().BeEquivalentTo(["comprador", "vendedor"]);
    }

    [Fact]
    public void CriterioDeIdentidadNoSeMueve()
    {
        var actor = JuridicalComprador(
            empresaEmail: "empresa@flit.test",
            rlEmail: "rl@flit.test",
            rlNombre: "Rep Legal");

        var subject = IdentitySubjectResolver.For(actor);

        subject.Email.Should().Be("rl@flit.test");
        subject.Email.Should().NotBe("empresa@flit.test");
    }

    private static ProcedureInstance Matricula() => new()
    {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
    };

    private static ProcedureInstance Traspaso() => new()
    {
        ProcedureType = ProcedureTypeFixture.For("traspaso"),
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
    };

    private static ProcedureInstanceActor Natural(string role, string email) => new()
    {
        ActorType = role,
        PersonType = "natural",
        DocumentType = "CC",
        DocumentNumber = "1",
        FullName = role,
        Email = email,
        Metadata = "{}",
    };

    private static ProcedureInstanceActor JuridicalComprador(
        string? empresaEmail,
        string? rlEmail,
        string? rlNombre,
        string? metadata = null) =>
        new()
        {
            ActorType = "comprador",
            PersonType = "juridical",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "ACME S.A.S.",
            Email = empresaEmail,
            Metadata = metadata ?? RlMetadata(rlEmail, rlNombre),
        };

    private static string RlMetadata(string? email, string? nombre)
    {
        var payload = new
        {
            representanteLegal = new
            {
                tipoDocumento = "CC",
                numeroDocumento = "555",
                nombreCompleto = nombre,
                email,
                telefono = (string?)null,
            },
        };
        return JsonSerializer.Serialize(payload);
    }
}
