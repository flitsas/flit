using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11141 — <c>firmaBaulPartes</c> es la lista que alimenta el resumen de firma del paso FUR y las
/// pestañas de comprador/vendedor del expediente. Debe reflejar el mecanismo SELECCIONADO para cada
/// actor, no la mera existencia de una firma en el baúl.
/// </summary>
public sealed class ListBiometriaFirmaBaulTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ProcedureInstanceActor ActorJuridico(string parte, string? mecanismo)
    {
        var rl = new Dictionary<string, object?>
        {
            ["tipoDocumento"] = "CC",
            ["numeroDocumento"] = parte == "comprador" ? "52082029" : "79522832",
            ["nombreCompleto"] = "REPRESENTANTE DEMO",
        };
        if (mecanismo is not null)
            rl["mecanismoFirma"] = mecanismo;

        return new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ActorType = parte,
            DocumentType = "NIT",
            DocumentNumber = parte == "comprador" ? "900511343" : "890903938",
            FullName = $"EMPRESA {parte.ToUpperInvariant()} S.A.S.",
            PersonType = "juridical",
            Metadata = JsonSerializer.Serialize(new Dictionary<string, object?> { ["representanteLegal"] = rl }),
        };
    }

    private static async Task<IReadOnlyList<string>> PartesConBaul(
        string? mecanismoComprador, string? mecanismoVendedor)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("traspaso"),
            Id = id,
            TenantId = TenantId,
            ModalidadEntrada = "traspaso",
            ReferenceNumber = "T-1",
        };
        instance.Actors.Add(ActorJuridico("comprador", mecanismoComprador));
        instance.Actors.Add(ActorJuridico("vendedor", mecanismoVendedor));

        var repo = Substitute.For<IProcedureInstanceRepository>();
        var ct = TestContext.Current.CancellationToken;
        repo.GetByIdWithBiometricsAndActorsAsync(id, TenantId, ct).Returns(instance);

        // Ambos representantes TIENEN firma vigente en el baúl: es justo el escenario en el que la
        // elección del gestor decide, y en el que antes se rotulaba siempre como baúl.
        var vault = new StubVaultPolicy(new SignatureVaultMatch(
            Guid.NewGuid(), "REPRESENTANTE DEMO", "hash", "ruta", "sha",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "52082029"));

        var handler = new ListBiometriaHandler(repo, new BiometricsProviderOptions(), vault);
        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        return result!.FirmaBaulPartes ?? [];
    }

    [Fact]
    public async Task ConIdentidadElegidaParaAmbos_NingunaParteSeRotulaComoBaul()
    {
        var partes = await PartesConBaul(MecanismoFirma.Identidad, MecanismoFirma.Identidad);

        partes.Should().BeEmpty("se eligió validación de identidad, que es lo que se plasma en el documento");
    }

    [Fact]
    public async Task ConMecanismosDistintosPorParte_CadaUnaSeRotulaConElSuyo()
    {
        // El caso exacto que reportó el PO: mecanismos distintos para comprador y vendedor.
        var partes = await PartesConBaul(MecanismoFirma.Identidad, MecanismoFirma.Baul);

        partes.Should().BeEquivalentTo(["vendedor"]);
    }

    [Fact]
    public async Task ConBaulElegidoParaAmbos_LasDosSeRotulanComoBaul()
    {
        var partes = await PartesConBaul(MecanismoFirma.Baul, MecanismoFirma.Baul);

        partes.Should().BeEquivalentTo(["comprador", "vendedor"]);
    }

    [Fact]
    public async Task SinEleccionExplicita_SeMantieneLaPrecedenciaDelBaul()
    {
        var partes = await PartesConBaul(null, null);

        partes.Should().BeEquivalentTo(["comprador", "vendedor"]);
    }

    private sealed class StubVaultPolicy(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
