using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Traslado de cuenta: el organismo de DESTINO es el objeto del trámite, no un adorno del párrafo
/// 23. Sin él el FUR no puede decir a dónde se traslada la cuenta, así que el gate de preparación
/// lo exige presente y habilitado para la compañía —será ella quien radique allí después.
/// <para>La habilitación se comprueba EN LA PREPARACIÓN y no solo al elegirla: un borrador vive
/// días y el convenio con el organismo pudo revocarse en el medio. Ese caso es el que más se
/// escapa, porque en la pantalla el destino sigue viéndose escogido.</para>
/// <para>El trámite espejo —<c>RADICADO_CUENTA</c>— no pasa por aquí: allí el destino ES el
/// organismo del propio trámite y lo valida el gate de entrega.</para>
/// </summary>
public sealed class TrasladoCuentaOrganismoDestinoTests
{
    private static readonly Guid DestinoId = Guid.Parse("1436197e-2c35-5a6a-bc1b-c3bb4f3abb96");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();

    public TrasladoCuentaOrganismoDestinoTests()
    {
        _grantGate.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task SinDestinoDeclarado_NoDejaPreparar()
    {
        var i = Wire(destino: null);

        var outcome = await Preparar(i);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoDestinoRequerido);
        i.Status.Should().Be(TramiteEstado.Borrador, "el trámite no avanza sin saber a dónde va la cuenta");
    }

    [Fact]
    public async Task DestinoIlegible_SeTrataComoAusente()
    {
        // Un id corrupto no es "otro organismo": es no tener destino. Antes de exigirlo, este valor
        // llegaba al FUR y salía impreso tal cual en el párrafo 23.
        var i = Wire(destino: "no-es-un-guid");

        var outcome = await Preparar(i);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoDestinoRequerido);
    }

    [Fact]
    public async Task DestinoConGrantRevocado_NoDejaPreparar()
    {
        _grantGate.IsEnabledForTenantAsync(Arg.Any<Guid>(), DestinoId, Arg.Any<CancellationToken>())
            .Returns(false);
        var i = Wire(destino: DestinoId.ToString());

        var outcome = await Preparar(i);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.OrganismoDestinoRequerido);
        outcome.ErrorDetail.Should().Contain("habilitada", "el mensaje distingue «no la elegiste» de «ya no la tienes»");
    }

    [Fact]
    public async Task DestinoHabilitado_DejaPasarElGate()
    {
        var i = Wire(destino: DestinoId.ToString());

        var outcome = await Preparar(i);

        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.OrganismoDestinoRequerido);
    }

    [Fact]
    public async Task RadicadoDeCuenta_NoExigeDestinoAparte()
    {
        // El espejo: su destino ES el organismo del trámite, así que declarar uno más sobraría.
        var i = Wire(destino: null, code: "RADICADO_CUENTA", requiereDestino: false);

        var outcome = await Preparar(i);

        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.OrganismoDestinoRequerido);
    }

    private Task<TramiteTransitionOutcome> Preparar(ProcedureInstance i) =>
        Servicio().TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null),
            TestContext.Current.CancellationToken);

    private TramiteLifecycleService Servicio() => new(
        _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance,
        new RecordingTransitionRecorder(), new RecordingTransitionPublisher());

    /// <summary>
    /// Trámite de la familia OTROS listo para preparar salvo por el destino: documentos obligatorios
    /// cargados, impronta generada y biométrica del propietario aprobada. Los gates que van ANTES
    /// del destino tienen que estar satisfechos o el test mediría otra cosa.
    /// </summary>
    private ProcedureInstance Wire(string? destino, string code = "TRASLADO_CUENTA", bool requiereDestino = true)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        var perfil = requiereDestino
            ? """{"entryMode":"PLATE","requiresBuyer":true,"requiresDestinationTransitOffice":true}"""
            : """{"entryMode":"PLATE","requiresBuyer":true}""";

        var i = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = typeId,
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcedureType = new ProcedureType
            {
                Id = typeId,
                Code = code,
                Name = code,
                Family = "OTROS",
                GateProfile = perfil,
                PublicationStatus = PublicationStatus.Published,
                WizardEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        foreach (var tipo in new[] { "tarjeta_propiedad", "doc_identidad_propietario", "paz_salvo", "impronta" })
        {
            i.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = tipo,
                StoragePath = $"p/{tipo}",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            PartyRole = "comprador",
            Status = BiometricEstados.Aprobado,
            Name = "DANIEL AMADO",
            DocumentType = "CC",
            DocumentNumber = "1193552679",
            Email = "daniel@example.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        if (destino is not null)
        {
            i.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                FieldKey = TransitOfficeFieldKeys.DestinoId,
                ValueText = destino,
            });
        }

        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(typeId, Arg.Any<CancellationToken>()).Returns(i.ProcedureType);
        return i;
    }
}
