using Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;
using Flit.Admin.Domain.Companies.MandateSigners;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// «Consultar estado» de la validación propia del mandatario (compañía y hub OT): solo aplica a la Persona natural con
/// biometría, respeta el organismo de la ruta y traduce el desenlace del puerto de reconciliación.
/// </summary>
public sealed class ReconcileMandateSignerIdentityHandlerTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMandateSignerReader _reader = Substitute.For<IMandateSignerReader>();
    private readonly IMandateSignerIdentityReconciler _reconciler = Substitute.For<IMandateSignerIdentityReconciler>();

    private ReconcileMandateSignerIdentityHandler Sut() => new(_reader, _reconciler);

    private MandateSignerItem Signer(string model = MandateSignerModels.Natural, string? method = MandateSignatureMethods.Biometria)
    {
        var signer = new MandateSignerItem
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Office,
            TransitOfficeIds = [Office],
            SignerModel = model,
            SignatureMethod = method,
            FullName = "Mandatario",
            DocumentType = "CC",
            DocumentNumber = "1000098455",
        };
        _reader.GetByIdAsync(signer.Id, Arg.Any<CancellationToken>()).Returns(signer);
        return signer;
    }

    [Fact]
    public async Task Aprobacion_recuperada_por_la_consulta_viaja_con_updated()
    {
        var signer = Signer();
        _reconciler.ReconcileAsync(signer.Id, Arg.Any<CancellationToken>())
            .Returns(new MandateSignerIdentityReconcileResult(MandateSignerIdentityReconcileOutcome.Ok, "aprobado", true));

        var result = await Sut().HandleAsync(signer.Id, Office, Ct);

        result.Outcome.Should().Be(ReconcileMandateSignerIdentityOutcome.Ok);
        result.Status.Should().Be("aprobado");
        result.Updated.Should().BeTrue();
    }

    [Fact]
    public async Task Mandatario_de_otro_organismo_es_404_y_no_consulta()
    {
        var signer = Signer();

        var result = await Sut().HandleAsync(signer.Id, Guid.NewGuid(), Ct);

        result.Outcome.Should().Be(ReconcileMandateSignerIdentityOutcome.NotFound);
        await _reconciler.DidNotReceive().ReconcileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Firma_con_baul_no_requiere_validacion()
    {
        var signer = Signer(method: MandateSignatureMethods.Baul);

        var result = await Sut().HandleAsync(signer.Id, null, Ct);

        result.Outcome.Should().Be(ReconcileMandateSignerIdentityOutcome.NoRequiereValidacion);
        await _reconciler.DidNotReceive().ReconcileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MandateSignerIdentityReconcileOutcome.SinValidacion, ReconcileMandateSignerIdentityOutcome.SinValidacion)]
    [InlineData(MandateSignerIdentityReconcileOutcome.ProveedorNoDisponible, ReconcileMandateSignerIdentityOutcome.ProveedorNoDisponible)]
    public async Task Traduce_el_desenlace_del_puerto(
        MandateSignerIdentityReconcileOutcome port, ReconcileMandateSignerIdentityOutcome expected)
    {
        var signer = Signer();
        _reconciler.ReconcileAsync(signer.Id, Arg.Any<CancellationToken>())
            .Returns(new MandateSignerIdentityReconcileResult(port));

        var result = await Sut().HandleAsync(signer.Id, null, Ct);

        result.Outcome.Should().Be(expected);
    }
}
