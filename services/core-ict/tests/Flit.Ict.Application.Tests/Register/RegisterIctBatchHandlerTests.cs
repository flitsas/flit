using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

public sealed class RegisterIctBatchHandlerTests
{
    private readonly IPreTramiteRepository _repository = Substitute.For<IPreTramiteRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RegisterIctBatchHandlerTests()
    {
        _tenant.TenantId.Returns(Guid.NewGuid());
        _tenant.CompanyNit.Returns("901698038");
        _tenant.IntegrationClientId.Returns(Guid.NewGuid());
    }

    private RegisterIctBatchHandler CreateHandler(int max = 20) =>
        new(_repository, _tenant, Options.Create(new IctIngestOptions { MaxItemsPerBatch = max }));

    // Traspaso bilateral (tipo 3) COMPLETO según el contrato de entrada: seller + buyer, compraventa,
    // gestor y dirección. doc por defecto = NIT del token para que la fila sea aceptada.
    private static RegisterRowInput ValidTraspaso(string plate = "PRU57A", string? doc = "901698038") =>
        new(
            TransactionType: 3,
            TransactionOperation: 1,
            CompanyManagerDocument: doc,
            ManagerUser: "gestor",
            ManagerMail: "gestor@flit.com",
            DeliveryAddress: "Calle 1 # 2-3",
            Plate: plate,
            SellingDate: "05-07-2024",
            SellingPrice: 15000000m,
            Seller: [new RegisterActorInput("CC", "11207262", "ABRAHAM", "CANON")],
            Buyer: [new RegisterActorInput("CC", "79912345", "COMPRADOR", "TEST")]);

    [Fact]
    public async Task Batch_over_limit_is_rejected_without_persisting()
    {
        var rows = Enumerable.Range(0, 21).Select(i => ValidTraspaso($"AAA{i:000}")).ToList();

        var (result, error) = await CreateHandler(max: 20).HandleAsync(new RegisterBatchCommand(rows), Ct);

        result.Should().BeNull();
        error.Should().Be("batch_limit_exceeded");
        await _repository.DidNotReceive().AddAsync(Arg.Any<Domain.Entities.ExternalIntegrationMaster>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Row_with_foreign_document_is_flagged_and_others_processed()
    {
        var rows = new List<RegisterRowInput>
        {
            ValidTraspaso("ABC111", doc: "890903938"), // NIT ajeno al del token
            ValidTraspaso("ABC222"),
        };

        var (result, error) = await CreateHandler().HandleAsync(new RegisterBatchCommand(rows), Ct);

        error.Should().BeNull();
        result!.TotalRows.Should().Be(2);
        result.TotalRowsProcessed.Should().Be(1);
        result.Detail[0].Status.Should().Be(2);
        result.Detail[0].Message.Should().Contain("company_manager_document");
        result.Detail[1].Status.Should().Be(1);
    }

    [Theory]
    [InlineData("901698038")]      // exacto
    [InlineData("901698038-3")]    // con dígito de verificación
    [InlineData("901.698.038")]    // con separadores de miles
    [InlineData("901.698.038-3")]  // formato completo
    [InlineData(" 901698038 ")]    // con espacios
    public async Task Row_with_same_nit_in_any_format_is_accepted(string doc)
    {
        // El token es NIT "901698038" (sin dígito de verificación); el payload puede traerlo formateado.
        var rows = new List<RegisterRowInput> { ValidTraspaso("FMT001", doc: doc) };

        var (result, error) = await CreateHandler().HandleAsync(new RegisterBatchCommand(rows), Ct);

        error.Should().BeNull();
        result!.TotalRowsProcessed.Should().Be(1);
        result.Detail[0].Status.Should().Be(1);
    }

    [Theory]
    [InlineData("900123456-1", "900123456")]
    [InlineData("900.123.456-1", "900123456")]
    [InlineData("  900123456  ", "900123456")]
    [InlineData("901698038", "901698038")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizeNit_drops_verification_digit_and_separators(string? input, string expected)
    {
        IctPayloadNormalizer.NormalizeNit(input).Should().Be(expected);
    }

    [Fact]
    public async Task Intra_batch_duplicate_by_plate_is_flagged()
    {
        var rows = new List<RegisterRowInput> { ValidTraspaso("DUP123"), ValidTraspaso("DUP123") };

        var (result, _) = await CreateHandler().HandleAsync(new RegisterBatchCommand(rows), Ct);

        result!.TotalRowsProcessed.Should().Be(1);
        result.Detail[1].Status.Should().Be(2);
        result.Detail[1].Message.Should().Contain("duplicado");
    }
}
