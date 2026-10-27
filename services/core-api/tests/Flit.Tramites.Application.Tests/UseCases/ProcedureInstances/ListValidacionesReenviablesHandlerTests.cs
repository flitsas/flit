using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13821 (Épica #13817) — <see cref="ListValidacionesReenviablesHandler"/>: el cuadro «Reenviar
/// validación» lista PERSONAS que no han firmado, no filas del expediente. AC1: quien ya firmó no aparece.
/// AC2: copropietarios por separado, sin baúl, sin identidades reutilizadas, una fila por persona.
/// </summary>
public sealed class ListValidacionesReenviablesHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private ProcedureInstance Instance(ProcedureType tipo)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureType = tipo,
            ReferenceNumber = "TRM-2026-013821",
            Status = "borrador",
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private ProcedureInstanceActor Actor(ProcedureInstance instance, string parte, string documento, int ordinal = 1, string? nombre = null)
    {
        var actor = new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = parte,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = nombre ?? $"PERSONA {documento}",
            Email = $"{documento}@actor.com",
            PersonType = "natural",
            Ordinal = ordinal,
        };
        instance.Actors.Add(actor);
        return actor;
    }

    private ProcedureInstanceBiometricValidation Validacion(
        ProcedureInstance instance,
        string? parte,
        string documento,
        string status,
        DateTimeOffset? createdAt = null,
        string provider = BiometricProviders.Mock)
    {
        var now = DateTimeOffset.UtcNow;
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = parte,
            Name = $"PERSONA {documento}",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = $"{documento}@validacion.com",
            Status = status,
            Provider = provider,
            CaptureUrl = $"https://capture.example.com/{documento}",
            ExpiresAt = now.AddHours(20),
            CreatedAt = createdAt ?? now.AddHours(-1),
            ValidatedAt = status == BiometricEstados.Aprobado ? now.AddDays(-1) : null,
            ValidUntil = status == BiometricEstados.Aprobado ? now.AddDays(29) : null,
        };
        instance.BiometricValidations.Add(v);
        return v;
    }

    private async Task<IReadOnlyList<ValidacionReenviableDto>> Listar(ProcedureInstance instance, ISignatureVaultPolicy? vault = null)
    {
        var handler = new ListValidacionesReenviablesHandler(_repo, vault);
        var (result, error) = await handler.HandleAsync(instance.Id, _tenantId, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return result!.Personas;
    }

    // ── AC1 — solo personas sin firmar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Traspaso_CompradorFirmado_SoloApareceElVendedorConSuCorreo()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Actor(instance, "vendedor", "222");
        Validacion(instance, "comprador", "111", BiometricEstados.Aprobado);
        var vendedor = Validacion(instance, "vendedor", "222", BiometricEstados.Enviado);

        var personas = await Listar(instance);

        personas.Should().ContainSingle();
        personas[0].ValidationId.Should().Be(vendedor.Id);
        personas[0].PartyRole.Should().Be("vendedor");
        personas[0].Email.Should().Be("222@validacion.com");
        personas[0].CaptureUrl.Should().Be("https://capture.example.com/222");
    }

    [Fact]
    public async Task AC1_Traspaso_NingunoFirmado_AparecenLosDosEnOrdenCompradorVendedor()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "vendedor", "222");
        Actor(instance, "comprador", "111");
        Validacion(instance, "vendedor", "222", BiometricEstados.Enviado);
        Validacion(instance, "comprador", "111", BiometricEstados.Rechazado);

        var personas = await Listar(instance);

        personas.Select(p => p.PartyRole).Should().Equal("comprador", "vendedor");
    }

    [Fact]
    public async Task AC1_Matricula_FilaLegadaSinRol_SeAtribuyeAlComprador()
    {
        var instance = Instance(ProcedureTypeFixture.Matricula);
        Actor(instance, "comprador", "111");
        var legada = Validacion(instance, null, "111", BiometricEstados.Expirado);

        var personas = await Listar(instance);

        personas.Should().ContainSingle().Which.ValidationId.Should().Be(legada.Id);
    }

    [Fact]
    public async Task AC1_TodosFirmados_ListaVacia()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Actor(instance, "vendedor", "222");
        Validacion(instance, "comprador", "111", BiometricEstados.Aprobado);
        Validacion(instance, "vendedor", "222", BiometricEstados.Aprobado);

        (await Listar(instance)).Should().BeEmpty();
    }

    // ── AC2 — copropietarios, baúl, reutilizadas, una fila por persona ──────────────────────────────

    [Fact]
    public async Task AC2_Copropietarios_UnaEntradaPorPersonaConSuOrdinal()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Actor(instance, "vendedor", "221", ordinal: 1);
        Actor(instance, "vendedor", "222", ordinal: 2);
        Validacion(instance, "comprador", "111", BiometricEstados.Enviado);
        Validacion(instance, "vendedor", "221", BiometricEstados.Enviado);
        Validacion(instance, "vendedor", "222", BiometricEstados.Enviado);

        var personas = await Listar(instance);

        personas.Should().HaveCount(3);
        personas.Where(p => p.PartyRole == "vendedor").Select(p => p.Ordinal).Should().Equal(1, 2);
    }

    [Fact]
    public async Task AC2_VariosIntentos_SeTomaElMasRecienteSinAprobar()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Validacion(instance, "comprador", "111", BiometricEstados.Rechazado, DateTimeOffset.UtcNow.AddDays(-3));
        var reciente = Validacion(instance, "comprador", "111", BiometricEstados.Expirado, DateTimeOffset.UtcNow.AddDays(-1));

        var personas = await Listar(instance);

        personas.Should().ContainSingle().Which.ValidationId.Should().Be(reciente.Id);
    }

    [Fact]
    public async Task AC2_IdentidadReutilizadaDeOtroTramite_CuentaComoFirmadaYNoSeLista()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Actor(instance, "vendedor", "222");
        Validacion(instance, "comprador", "111", BiometricEstados.Expirado);
        Validacion(instance, "vendedor", "222", BiometricEstados.Enviado);
        _repo.FindVigenteApprovedByDocumentAsync(_tenantId, "CC", "111", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstanceBiometricValidation { Id = Guid.NewGuid(), Status = BiometricEstados.Aprobado });

        var personas = await Listar(instance);

        personas.Should().ContainSingle().Which.PartyRole.Should().Be("vendedor");
    }

    [Fact]
    public async Task AC2_SinFilaPropia_NoSeInventaLaPersona()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        Actor(instance, "vendedor", "222");
        Validacion(instance, "vendedor", "222", BiometricEstados.Enviado);

        var personas = await Listar(instance);

        personas.Should().ContainSingle().Which.PartyRole.Should().Be("vendedor");
    }

    [Fact]
    public async Task AC2_PersonaJuridicaFirmadaPorBaul_NoSeLista()
    {
        var instance = Instance(ProcedureTypeFixture.Traspaso);
        Actor(instance, "comprador", "111");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ActorType = "vendedor",
            DocumentType = "NIT",
            DocumentNumber = "900511343",
            FullName = "EMPRESA VENDEDORA S.A.S.",
            PersonType = "juridical",
            Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["representanteLegal"] = new Dictionary<string, object?>
                {
                    ["tipoDocumento"] = "CC",
                    ["numeroDocumento"] = "79522832",
                    ["nombreCompleto"] = "REPRESENTANTE DEMO",
                    ["mecanismoFirma"] = MecanismoFirma.Baul,
                },
            }),
        });
        Validacion(instance, "comprador", "111", BiometricEstados.Enviado);
        Validacion(instance, "vendedor", "79522832", BiometricEstados.Enviado);
        var vault = new StubVaultPolicy(new SignatureVaultMatch(
            Guid.NewGuid(), "REPRESENTANTE DEMO", "hash", "ruta", "sha",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "79522832"));

        var personas = await Listar(instance, vault);

        personas.Should().ContainSingle().Which.PartyRole.Should().Be("comprador");
    }

    [Fact]
    public async Task AC2_ValidacionEnFlujoManual_SeListaMarcada()
    {
        var instance = Instance(ProcedureTypeFixture.Matricula);
        Actor(instance, "comprador", "111");
        Validacion(instance, "comprador", "111", BiometricEstados.ManualActivo, provider: BiometricProviders.Manual);

        var personas = await Listar(instance);

        personas.Should().ContainSingle().Which.EsFlujoManual.Should().BeTrue();
    }

    [Fact]
    public async Task TramiteInexistente_NotFound()
    {
        var handler = new ListValidacionesReenviablesHandler(_repo);
        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), _tenantId, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    private sealed class StubVaultPolicy(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
