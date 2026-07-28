using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10955 (AC2/AC3/AC4/AC5) — lookup de datos de CONTACTO ya conocidos de una persona (ciudad,
/// correo, dirección, teléfono), sin nombre ni documento (esos vienen del RUNT) y sin gate de
/// consentimiento.
/// </summary>
public sealed class ActorContactLookupHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ActorContactLookupHandler _sut;

    public ActorContactLookupHandlerTests()
    {
        _sut = new ActorContactLookupHandler(_repo);
    }

    private static ProcedureInstanceActor Actor(
        Guid tenantId, string docType, string docNumber, string? email, string? phone, string metadata) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = Guid.NewGuid(),
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = docType,
            DocumentNumber = docNumber,
            FullName = "Nombre Persistido Irrelevante",
            Email = email,
            Phone = phone,
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>AC2 — devuelve ciudad/correo/dirección/teléfono del actor más reciente, NUNCA nombre ni documento.</summary>
    [Fact]
    public async Task HandleAsync_PersonaConAntecedentes_DevuelveSoloDatosDeContacto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var actor = Actor(tenantId, "CC", "123456789", "juan@example.com", "3001234567",
            """{"ciudad":"Medellín","direccion":"Cra 43A #1-50"}""");
        _repo.FindLatestActorContactAsync(tenantId, "CC", "123456789", ct).Returns(actor);

        var (result, error) = await _sut.HandleAsync(tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Ciudad.Should().Be("Medellín");
        result.Email.Should().Be("juan@example.com");
        result.Direccion.Should().Be("Cra 43A #1-50");
        result.Telefono.Should().Be("3001234567");
    }

    /// <summary>AC3 — sin antecedentes: 200 con los 4 campos vacíos, nunca error.</summary>
    [Fact]
    public async Task HandleAsync_SinAntecedentes_DevuelveCamposVacios_SinError()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.FindLatestActorContactAsync(tenantId, "CC", "999999999", ct)
            .Returns((ProcedureInstanceActor?)null);

        var (result, error) = await _sut.HandleAsync(tenantId, "CC", "999999999", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Ciudad.Should().BeNull();
        result.Email.Should().BeNull();
        result.Direccion.Should().BeNull();
        result.Telefono.Should().BeNull();
    }

    /// <summary>AC4 — sin gate de consentimiento: el handler no depende de ningún PersonDataConsent.</summary>
    [Fact]
    public async Task HandleAsync_SinConsentimientoRegistrado_IgualDevuelveElContacto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        // No hay ningún IPersonDataConsentRepository inyectado en el handler: estructuralmente no hay
        // gate posible. El repo de actores basta para servir el contacto.
        var actor = Actor(tenantId, "CE", "555", "sin.consent@example.com", null, "{}");
        _repo.FindLatestActorContactAsync(tenantId, "CE", "555", ct).Returns(actor);

        var (result, error) = await _sut.HandleAsync(tenantId, "CE", "555", ct);

        error.Should().BeNull();
        result!.Email.Should().Be("sin.consent@example.com");
    }

    /// <summary>AC5 — el aislamiento por tenant se delega al repositorio (WHERE tenant_id explícito).</summary>
    [Fact]
    public async Task HandleAsync_ResuelveElContactoConElTenantRecibido_NoConOtro()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        // La misma persona registrada en la compañía B; la compañía A no debe ver esos datos.
        var actorTenantB = Actor(tenantB, "CC", "123456789", "empresa-b@example.com", "3009999999", "{}");
        _repo.FindLatestActorContactAsync(tenantB, "CC", "123456789", ct).Returns(actorTenantB);
        _repo.FindLatestActorContactAsync(tenantA, "CC", "123456789", ct)
            .Returns((ProcedureInstanceActor?)null);

        var (result, error) = await _sut.HandleAsync(tenantA, "CC", "123456789", ct);

        error.Should().BeNull();
        result!.Email.Should().BeNull("la compañía A no tiene antecedentes propios de esta persona");
        await _repo.Received(1).FindLatestActorContactAsync(tenantA, "CC", "123456789", ct);
        await _repo.DidNotReceive().FindLatestActorContactAsync(tenantB, "CC", "123456789", ct);
    }

    [Fact]
    public async Task HandleAsync_TipoDocumentoInvalido_RetornaError()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), "XX", "123", ct);

        error.Should().Be("invalid_document_type");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NumeroDocumentoFaltante_RetornaError()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), "CC", "   ", ct);

        error.Should().Be("missing_document_number");
        result.Should().BeNull();
    }

    /// <summary>Normaliza (trim + upper para tipoDoc) antes de resolver contra el repositorio.</summary>
    [Fact]
    public async Task HandleAsync_NormalizaTipoDocumentoYNumero_AntesDeConsultar()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var actor = Actor(tenantId, "CC", "123456789", "juan@example.com", null, "{}");
        _repo.FindLatestActorContactAsync(tenantId, "CC", "123456789", ct).Returns(actor);

        var (result, error) = await _sut.HandleAsync(tenantId, " cc ", " 123456789 ", ct);

        error.Should().BeNull();
        result!.Email.Should().Be("juan@example.com");
        await _repo.Received(1).FindLatestActorContactAsync(tenantId, "CC", "123456789", ct);
    }
}
