using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11196 (AC1) y HU #11197 (AC1/AC2/AC4) — marcar el trámite para firma a posteriori. La marca solo
/// existe cuando el representante NO tiene con qué firmar: si tuviera firma del baúl o identidad
/// vigente, diferir solo retrasaría un trámite que puede cerrarse hoy.
/// </summary>
public sealed class MarcarFirmaPosteriorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IDeferredSignatureMarkRepository _marks =
        Substitute.For<IDeferredSignatureMarkRepository>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    private MarcarFirmaPosteriorHandler Handler() => new(_repo, _marks, _vault);

    [Fact]
    public async Task AC1_ConIdentidadYFirmaVencidas_ElTramiteQuedaMarcadoParaEsaEmpresaYEseRepresentante()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();
        DeferredSignatureMark? guardada = null;
        _marks.When(m => m.Add(Arg.Any<DeferredSignatureMark>()))
            .Do(call => guardada = call.Arg<DeferredSignatureMark>());

        var (result, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().BeNull();
        result!.Marcado.Should().BeTrue();
        guardada.Should().NotBeNull();
        guardada!.CompanyDocumentNumber.Should().Be("900123456");
        guardada.RepresentativeDocumentNumber.Should().Be("1090123456");
        guardada.Estado.Should().Be(DeferredSignatureEstados.Pendiente);
        await _marks.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConFirmaDelBaulVigente_NoSePuedeDiferir()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();
        _vault.ResolveAsync(Tenant, "CC", "1090123456", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Ana Representante", "hash", "vault/f.png", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "1090123456"));

        var (_, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().Be("firma_disponible");
        _marks.DidNotReceive().Add(Arg.Any<DeferredSignatureMark>());
    }

    [Fact]
    public async Task ConIdentidadVigenteDeOtroTramite_NoSePuedeDiferir()
    {
        // La identidad se reutiliza entre trámites (HU #10350): si la persona ya validó en otro, este
        // trámite puede firmarse ya.
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();
        _repo.FindVigenteApprovedByDocumentAsync(
                Tenant, "CC", "1090123456", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                PartyRole = "comprador",
                Name = "Ana",
                DocumentType = "CC",
                DocumentNumber = "1090123456",
                Email = "rep@empresa.com",
                Status = BiometricEstados.Aprobado,
                TokenHash = "hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var (_, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().Be("firma_disponible");
    }

    [Fact]
    public async Task FueraDeBorrador_NoSePuedeDiferir()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite(status: TramiteEstado.Entregado);

        var (_, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task PersonaNatural_NoAplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite(juridica: false);

        var (_, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().Be("no_aplica");
    }

    [Fact]
    public async Task MarcarDosVeces_NoCreaUnaSegundaMarca()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();
        var previa = new DeferredSignatureMark
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = id,
            PartyRole = "comprador",
            CompanyDocumentNumber = "900123456",
            RepresentativeDocumentType = "CC",
            RepresentativeDocumentNumber = "1090123456",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _marks.FindPendienteAsync(Tenant, id, "comprador", Arg.Any<CancellationToken>()).Returns(previa);

        var (result, error) = await Handler().HandleAsync(id, Tenant, "comprador", ct);

        error.Should().BeNull();
        result!.Marcado.Should().BeTrue();
        result.MarcadoAt.Should().Be(previa.CreatedAt); // conserva la fecha original
        _marks.DidNotReceive().Add(Arg.Any<DeferredSignatureMark>());
    }

    [Fact]
    public async Task HU11197_AC2_ConFirmaUtilizable_LaConsultaDiceQueNoAplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();
        _vault.ResolveAsync(Tenant, "CC", "1090123456", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Ana Representante", "hash", "vault/f.png", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "1090123456"));

        var (result, error) = await Handler().ConsultarAsync(id, Tenant, "comprador", ct);

        error.Should().BeNull();
        result!.Aplica.Should().BeFalse();
        result.Marcado.Should().BeFalse();
    }

    [Fact]
    public async Task HU11197_AC1_SinFirmaUtilizable_LaConsultaOfreceLaOpcion()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite();

        var (result, error) = await Handler().ConsultarAsync(id, Tenant, "comprador", ct);

        error.Should().BeNull();
        result!.Aplica.Should().BeTrue();
        result.RepresentanteNombre.Should().Be("Ana Representante");
    }

    [Fact]
    public async Task HU11197_LaConsultaSobrePersonaNatural_NoRompeLaPantalla()
    {
        // Devolver un error aquí obligaría al frontend a distinguir "no aplica" de "falló"; para el
        // gestor son lo mismo: la opción no existe.
        var ct = TestContext.Current.CancellationToken;
        var id = SeedTramite(juridica: false);

        var (result, error) = await Handler().ConsultarAsync(id, Tenant, "comprador", ct);

        error.Should().BeNull();
        result!.Aplica.Should().BeFalse();
    }

    private Guid SeedTramite(string status = TramiteEstado.Borrador, bool juridica = true)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            DocumentType = juridica ? "NIT" : "CC",
            DocumentNumber = juridica ? "900123456" : "123456",
            FullName = juridica ? "Empresa Compradora SAS" : "Juan Comprador",
            Email = "contacto@empresa.com",
            PersonType = juridica ? "juridical" : "natural",
            Metadata = juridica
                ? """{"representanteLegal":{"tipoDocumento":"CC","numeroDocumento":"1090123456","nombreCompleto":"Ana Representante","email":"rep@empresa.com"}}"""
                : "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return id;
    }
}
