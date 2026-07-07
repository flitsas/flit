using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10592 — las partes que llevan validación de identidad dependen de la MODALIDAD del trámite, ya no
/// del literal fijo <c>["comprador","vendedor"]</c>: matrícula → <c>[comprador]</c>; traspaso →
/// <c>[comprador, vendedor]</c>; traspaso unilateral → <c>[arrendadora]</c> (el locatario es documental).
/// </summary>
public sealed class IdentityApprovalResolverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ProcedureInstance Instance(string modalidad, params string[] partesAprobadas)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000400",
            ModalidadEntrada = modalidad,
            CreatedAt = Now,
        };

        // Fila PROPIA aprobada-vigente por parte: sin ValidUntil/ValidatedAt cuenta como vigente
        // (BiometricRules.EsAprobadaVigente); sin documentos, DocumentoCoincide es lenient.
        foreach (var parte in partesAprobadas)
            instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                PartyRole = parte,
                Status = BiometricEstados.Aprobado,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = Now.AddHours(1),
                CreatedAt = Now,
            });

        return instance;
    }

    private static readonly IReadOnlySet<string> NoKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ApprovedPartiesFromKeys_Unilateral_ResuelveSoloArrendadora()
    {
        // Hay biométrica aprobada de arrendadora Y de comprador; para el unilateral SOLO cuenta la arrendadora.
        var instance = Instance(
            TramiteModalidadEntradaCodes.TraspasoUnilateral,
            BiometricRules.ParteArrendadora,
            BiometricRules.ParteComprador);

        var approved = IdentityApprovalResolver.ApprovedPartiesFromKeys(instance, NoKeys, Now);

        approved.Should().BeEquivalentTo([BiometricRules.ParteArrendadora]);
        approved.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Fact]
    public void ApprovedPartiesFromKeys_Traspaso_ResuelveCompradorYVendedor()
    {
        var instance = Instance(
            TramiteModalidadEntradaCodes.Traspaso,
            BiometricRules.ParteComprador,
            BiometricRules.ParteVendedor);

        var approved = IdentityApprovalResolver.ApprovedPartiesFromKeys(instance, NoKeys, Now);

        approved.Should().BeEquivalentTo([BiometricRules.ParteComprador, BiometricRules.ParteVendedor]);
    }

    [Fact]
    public void ApprovedPartiesFromKeys_Matricula_ResuelveSoloComprador()
    {
        // Aunque exista una biométrica de vendedor, la matrícula solo considera al comprador.
        var instance = Instance(
            TramiteModalidadEntradaCodes.MatriculaInicial,
            BiometricRules.ParteComprador,
            BiometricRules.ParteVendedor);

        var approved = IdentityApprovalResolver.ApprovedPartiesFromKeys(instance, NoKeys, Now);

        approved.Should().BeEquivalentTo([BiometricRules.ParteComprador]);
    }

    [Fact]
    public async Task ResolveApprovedPartiesAsync_Unilateral_SoloArrendadoraSinTocarRepo()
    {
        // Con la fila propia de la arrendadora aprobada-vigente, no se consulta el repo (fallback cross-trámite).
        var instance = Instance(
            TramiteModalidadEntradaCodes.TraspasoUnilateral,
            BiometricRules.ParteArrendadora);
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var ct = TestContext.Current.CancellationToken;

        var approved = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, Now, ct);

        approved.Should().BeEquivalentTo([BiometricRules.ParteArrendadora]);
        await repo.DidNotReceiveWithAnyArgs().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), ct);
    }
}
