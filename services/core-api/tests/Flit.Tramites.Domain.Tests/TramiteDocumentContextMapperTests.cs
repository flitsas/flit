using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10521 (RF30/35/39) — derivación del contexto documental desde los datos persistidos del
/// trámite (actores, campos RUNT, participantes).
/// </summary>
public sealed class TramiteDocumentContextMapperTests
{
    private static ProcedureInstance InstanceWith(
        (string ActorType, string DocumentType)[]? actors = null,
        (string Key, string? Value)[]? fields = null,
        string[]? participantRoles = null)
    {
        var instance = new ProcedureInstance();
        foreach (var (actorType, docType) in actors ?? [])
            instance.Actors.Add(new ProcedureInstanceActor { ActorType = actorType, DocumentType = docType });
        foreach (var (key, value) in fields ?? [])
            instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = key, ValueText = value });
        foreach (var rol in participantRoles ?? [])
            instance.Participants.Add(new ProcedureInstanceParticipant { Rol = rol });
        return instance;
    }

    [Fact]
    public void SinDatos_ContextoSinCondiciones()
    {
        var ctx = TramiteDocumentContextMapper.From(new ProcedureInstance());
        ctx.EsNit.Should().BeFalse();
        ctx.EsPersonaNatural.Should().BeFalse();
        ctx.ServicioEspecial.Should().BeFalse();
        ctx.TieneTramitador.Should().BeFalse();
        ctx.EsImportado.Should().BeFalse();
        ctx.TienePrenda.Should().BeFalse();
        ctx.TieneLeasing.Should().BeFalse();
        ctx.CambioCarroceria.Should().BeFalse();
    }

    [Fact]
    public void ActorNit_EsNit_NoPersonaNatural()
    {
        var ctx = TramiteDocumentContextMapper.From(
            InstanceWith(actors: [("comprador", "NIT"), ("vendedor", "CC")]));
        ctx.EsNit.Should().BeTrue();
        ctx.EsPersonaNatural.Should().BeFalse();
    }

    [Fact]
    public void ActoresPersonaNatural_EsPersonaNatural_NoNit()
    {
        // HU #10542: la persona natural es explícita (PersonType), no inferida del tipo de documento.
        var instance = InstanceWith(actors: [("comprador", "CC"), ("vendedor", "CE")]);
        foreach (var a in instance.Actors) a.PersonType = ActorPersonTypes.Natural;

        var ctx = TramiteDocumentContextMapper.From(instance);
        ctx.EsPersonaNatural.Should().BeTrue();
        ctx.EsNit.Should().BeFalse();
    }

    [Fact]
    public void ActorJuridico_PorPersonType_EsNit()
    {
        var instance = InstanceWith(actors: [("comprador", "CC")]);
        instance.Actors.First().PersonType = ActorPersonTypes.Juridical;

        var ctx = TramiteDocumentContextMapper.From(instance);
        ctx.EsNit.Should().BeTrue();
        ctx.EsPersonaNatural.Should().BeFalse();
    }

    [Fact]
    public void ActorSinTipoPersona_NiPnNiNit()
    {
        // Sin PersonType declarado y documento no-NIT ⇒ ni PN ni NIT (conservador: conserva cédula).
        var ctx = TramiteDocumentContextMapper.From(InstanceWith(actors: [("vendedor", "CC")]));
        ctx.EsPersonaNatural.Should().BeFalse();
        ctx.EsNit.Should().BeFalse();
    }

    [Fact]
    public void OwnerDocumentTypeNit_DesdeCampo_EsNit()
    {
        var ctx = TramiteDocumentContextMapper.From(
            InstanceWith(fields: [("owner_document_type", "NIT")]));
        ctx.EsNit.Should().BeTrue();
    }

    [Fact]
    public void VehicleServiceEspecial_ServicioEspecial()
    {
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("vehicle_service", "SERVICIO ESPECIAL")]))
            .ServicioEspecial.Should().BeTrue();
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("vehicle_service", "PARTICULAR")]))
            .ServicioEspecial.Should().BeFalse();
    }

    [Fact]
    public void ParticipanteMandatario_TieneTramitador()
    {
        TramiteDocumentContextMapper.From(InstanceWith(participantRoles: ["mandatario"]))
            .TieneTramitador.Should().BeTrue();
        TramiteDocumentContextMapper.From(InstanceWith(participantRoles: ["comprador"]))
            .TieneTramitador.Should().BeFalse();
    }

    // ── Banderas manuales del paso de vehículo (RF33/37/38) ──────────────────

    [Fact]
    public void EsImportadoTrue_DesdeCampo_EsImportado()
    {
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_importado", "true")]))
            .EsImportado.Should().BeTrue();
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_importado", "false")]))
            .EsImportado.Should().BeFalse();
    }

    [Fact]
    public void EsLeasingTrue_DesdeCampo_TieneLeasing()
    {
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_leasing", "true")]))
            .TieneLeasing.Should().BeTrue();
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_leasing", "false")]))
            .TieneLeasing.Should().BeFalse();
    }

    [Fact]
    public void CambioCarroceriaTrue_DesdeCampo_CambioCarroceria()
    {
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("cambio_carroceria", "true")]))
            .CambioCarroceria.Should().BeTrue();
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("cambio_carroceria", "false")]))
            .CambioCarroceria.Should().BeFalse();
    }

    [Fact]
    public void CampoBooleanoNoReconocido_NoActivaBandera()
    {
        // Valor distinto de "true" (case-insensitive) ⇒ false; ausencia ⇒ false (cero regresión).
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_leasing", "1")]))
            .TieneLeasing.Should().BeFalse();
        TramiteDocumentContextMapper.From(InstanceWith(fields: [("es_leasing", "TRUE")]))
            .TieneLeasing.Should().BeTrue();
    }
}
