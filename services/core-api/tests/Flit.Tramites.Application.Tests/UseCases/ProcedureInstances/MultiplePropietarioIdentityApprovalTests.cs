using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0053 (Múltiple Propietario) §4.3 — "todos firman": <c>IdentityApprovalResolver</c> exige que
/// TODOS los actores de un lado tengan identidad aprobada+vigente, no solo el principal. El resolver es
/// <c>internal</c> (sin <c>InternalsVisibleTo</c> hacia este ensamblado, mismo criterio documentado en
/// <see cref="PredicadoActorJuridicoUnicoTests"/>): se ejercita por el camino público
/// (<see cref="GetWizardStateHandler"/>), observando el paso de identidad (Index 4 en matrícula inicial)
/// y <c>CanSubmit</c>.
/// </summary>
public sealed class MultiplePropietarioIdentityApprovalTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task DosCompradores_SoloElPrincipalAprobado_PasoIdentidadQuedaIncompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = TramiteConDosCompradores(
            aprobarPrincipal: true, aprobarAgregado: false, out _, out _);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        StubSinReferenciaCruzada();

        var handler = new GetWizardStateHandler(_repo);
        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task DosCompradores_AmbosAprobados_PasoIdentidadQuedaCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = TramiteConDosCompradores(
            aprobarPrincipal: true, aprobarAgregado: true, out _, out _);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        StubSinReferenciaCruzada();

        var handler = new GetWizardStateHandler(_repo);
        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task UnSoloComprador_Aprobado_ComportamientoIdenticoAlActual()
    {
        // Regresión cero: con un solo actor por lado, el comportamiento no cambia.
        var ct = TestContext.Current.CancellationToken;
        var instance = TramiteBase();
        var doc = "111";
        instance.Actors.Add(Comprador(doc, ordinal: 1));
        instance.BiometricValidations.Add(Aprobado("comprador", doc));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        StubSinReferenciaCruzada();

        var handler = new GetWizardStateHandler(_repo);
        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    private void StubSinReferenciaCruzada() =>
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

    private static ProcedureInstance TramiteBase()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000003",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
        });
        instance.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            Overall = "green",
            Checks = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });
        return instance;
    }

    private static ProcedureInstance TramiteConDosCompradores(
        bool aprobarPrincipal, bool aprobarAgregado, out string docPrincipal, out string docAgregado)
    {
        docPrincipal = "111";
        docAgregado = "222";
        var instance = TramiteBase();
        instance.Actors.Add(Comprador(docPrincipal, ordinal: 1));
        instance.Actors.Add(Comprador(docAgregado, ordinal: 2));
        if (aprobarPrincipal)
            instance.BiometricValidations.Add(Aprobado("comprador", docPrincipal));
        if (aprobarAgregado)
            instance.BiometricValidations.Add(Aprobado("comprador", docAgregado));
        return instance;
    }

    private static ProcedureInstanceActor Comprador(string documento, int ordinal) => new()
    {
        Id = Guid.NewGuid(),
        ActorType = "comprador",
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = $"Comprador {documento}",
        Email = $"comprador{documento}@x.com",
        Phone = "3001112233",
        Ordinal = ordinal,
        // HU #11593 — ciudad/dirección/teléfono forman parte de la exigencia dura de completitud
        // (ParteCompletaRule): sin ellos el paso de actores queda incompleto y bloquea (locked) el
        // paso de identidad, que es justo lo que este test necesita evaluar de forma aislada.
        Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
    };

    private static ProcedureInstanceBiometricValidation Aprobado(string parte, string documento)
    {
        var now = DateTimeOffset.UtcNow;
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            DocumentType = "CC",
            DocumentNumber = documento,
            Name = $"Comprador {documento}",
            Email = $"comprador{documento}@x.com",
            TokenHash = "h-" + documento,
            ExpiresAt = now.AddHours(1),
            CreatedAt = now,
        };
        v.Approve(now);
        return v;
    }
}
