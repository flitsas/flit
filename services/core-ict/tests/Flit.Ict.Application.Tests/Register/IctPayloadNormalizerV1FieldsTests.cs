using Flit.Ict.Application.Register;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

/// <summary>
/// Campos del contrato v1 del register que antes se descartaban silenciosamente: representante legal
/// y mandante de cada actor, y las banderas/datos de nivel trámite (pausa inicial, prenda, compañía
/// relacionada, etc.). Estas pruebas fijan que el normalizador los aplane al pre-trámite.
/// </summary>
public sealed class IctPayloadNormalizerV1FieldsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static RegisterActorInput SellerWithRlAndMandante() =>
        new(
            DocumentType: "NIT",
            DocumentNumber: "890903938",
            Name: "Comercializadora",
            FirstLastName: "Andina",
            SecondLastName: "SAS",
            Phone: "3111111111",
            Email: "ventas@andina.co",
            City: "Bogotá",
            State: "Cundinamarca",
            Address: "Av 68 #1-2",
            ExpeditionDate: null,
            LegalRepresentative: new RegisterLegalRepresentativeInput(
                DocumentType: "CC",
                DocumentNumber: "159753",
                Name: "Jane",
                FirstLastName: "Doe",
                SecondLastName: "Ruiz",
                Phone: "3123456780",
                Email: "jane.doe@andina.co",
                City: "Bogotá",
                State: "Cundinamarca",
                Address: "Calle 456 #12-34"),
            PrincipalMandante: new RegisterPrincipalMandanteInput(
                DocumentType: "CC",
                DocumentNumber: "8675309",
                Name: "Mandante",
                FirstLastName: "XYZ",
                SecondLastName: "Perez",
                Email: "mandante@andina.co"));

    [Fact]
    public void Legal_representative_is_flattened_into_the_actor()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "ACT321",
            Seller: [SellerWithRlAndMandante()]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        var seller = master.Actors.Single(a => a.ActorType == "seller");
        seller.LegalRepresentativeDocumentType.Should().Be("CC");
        seller.LegalRepresentativeDocumentNumber.Should().Be("159753");
        seller.LegalRepresentativeName.Should().Be("Jane");
        seller.LegalRepresentativeFirstLastName.Should().Be("Doe");
        seller.LegalRepresentativeSecondLastName.Should().Be("Ruiz");
        seller.LegalRepresentativePhone.Should().Be("3123456780");
        seller.LegalRepresentativeEmail.Should().Be("jane.doe@andina.co");
        seller.LegalRepresentativeCity.Should().Be("Bogotá");
        seller.LegalRepresentativeState.Should().Be("Cundinamarca");
        seller.LegalRepresentativeAddress.Should().Be("Calle 456 #12-34");
    }

    [Fact]
    public void Principal_mandante_is_flattened_into_the_actor()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "ACT321",
            Seller: [SellerWithRlAndMandante()]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        var seller = master.Actors.Single(a => a.ActorType == "seller");
        seller.PrincipalMandanteDocumentType.Should().Be("CC");
        seller.PrincipalMandanteDocumentNumber.Should().Be("8675309");
        seller.PrincipalMandanteName.Should().Be("Mandante");
        seller.PrincipalMandanteFirstLastName.Should().Be("XYZ");
        seller.PrincipalMandanteSecondLastName.Should().Be("Perez");
        seller.PrincipalMandanteEmail.Should().Be("mandante@andina.co");
    }

    [Fact]
    public void Actor_without_representative_or_mandante_leaves_those_fields_null()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "ACT321",
            Buyer: [new RegisterActorInput("CC", "52000222", "Ana", "Gomez")]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        var buyer = master.Actors.Single(a => a.ActorType == "buyer");
        buyer.LegalRepresentativeDocumentNumber.Should().BeNull();
        buyer.PrincipalMandanteDocumentNumber.Should().BeNull();
    }

    [Fact]
    public void Blank_optional_subfields_collapse_to_null_instead_of_empty_strings()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "ACT321",
            Seller:
            [
                new RegisterActorInput(
                    "CC", "11207262", "ABRAHAM", "CANON",
                    LegalRepresentative: new RegisterLegalRepresentativeInput(DocumentType: "   ", Name: ""))
            ]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        var seller = master.Actors.Single(a => a.ActorType == "seller");
        seller.LegalRepresentativeDocumentType.Should().BeNull();
        seller.LegalRepresentativeName.Should().BeNull();
    }

    [Fact]
    public void Tramite_level_v1_flags_and_data_are_mapped_to_the_master()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "FUL789",
            Priority: true,
            TransactionFlit: "FLIT001",
            StartsProcedureInPaused: true,
            ObservationWhenPaused: "En espera de liquidación",
            SendAutomaticTrafficSecretary: false,
            PlateAssignmentType: 3,
            RelatedCompanyDocument: "890903938",
            RelatedCompanyName: "Transportes del Sur SAS",
            LimitationsOperationType: 2,
            LimitationsCreditor: "Banco Acreedor SA",
            LimitationsCreditorDocumentType: "NIT",
            LimitationsCreditorDocumentNumber: "987654321",
            LimitationsInscriptionDate: "2024-07-05",
            ArmorLevelNumberId: 1,
            NewVehicleFuelType: 5);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        master.Priority.Should().BeTrue();
        master.TransactionFlit.Should().Be("FLIT001");
        master.StartsProcedureInPaused.Should().BeTrue();
        master.ObservationWhenPaused.Should().Be("En espera de liquidación");
        master.SendAutomaticTrafficSecretary.Should().BeFalse();
        master.PlateAssignmentType.Should().Be(3);
        master.RelatedCompanyDocument.Should().Be("890903938");
        master.RelatedCompanyName.Should().Be("Transportes del Sur SAS");
        master.LimitationsOperationType.Should().Be(2);
        master.LimitationsCreditor.Should().Be("Banco Acreedor SA");
        master.LimitationsCreditorDocumentType.Should().Be("NIT");
        master.LimitationsCreditorDocumentNumber.Should().Be("987654321");
        master.LimitationsInscriptionDate.Should().Be("2024-07-05");
        master.ArmorLevelNumberId.Should().Be(1);
        master.NewVehicleFuelType.Should().Be(5);
    }

    [Fact]
    public void Transformations_are_mapped_with_their_runt_code_and_description()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "TRF852",
            MoreTransactionTransactionType:
            [
                new RegisterTransformationInput(5, "Azul AguaMarina"),
                new RegisterTransformationInput(17, "Estacas"),
            ]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        master.Transformations.Should().HaveCount(2);
        master.Transformations.Should().ContainSingle(t => t.IdTransformationType == 5 && t.Description == "Azul AguaMarina");
        master.Transformations.Should().ContainSingle(t => t.IdTransformationType == 17 && t.Description == "Estacas");
        master.Transformations.Should().OnlyContain(t => t.TenantId == Tenant);
    }

    [Fact]
    public void Unknown_transformation_codes_are_ignored_instead_of_breaking_the_row()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "TRF852",
            MoreTransactionTransactionType:
            [
                new RegisterTransformationInput(5, "Azul"),
                new RegisterTransformationInput(99, "Codigo fuera del catalogo RUNT"),
            ]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        master.Transformations.Should().ContainSingle().Which.IdTransformationType.Should().Be(5);
    }

    [Fact]
    public void Duplicate_transformation_codes_collapse_to_one_row()
    {
        var row = new RegisterRowInput(
            TransactionType: 3,
            TransactionOperation: 1,
            Plate: "TRF852",
            MoreTransactionTransactionType:
            [
                new RegisterTransformationInput(9, "Primera"),
                new RegisterTransformationInput(9, "Repetida"),
            ]);

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        // La PK es (master, código RUNT): una segunda fila con el mismo código reventaría el insert.
        master.Transformations.Should().ContainSingle().Which.Description.Should().Be("Primera");
    }

    [Fact]
    public void Send_automatic_traffic_secretary_defaults_to_true_when_omitted()
    {
        var row = new RegisterRowInput(TransactionType: 3, TransactionOperation: 1, Plate: "FUL789");

        var master = IctPayloadNormalizer.ToMaster(row, Tenant);

        master.SendAutomaticTrafficSecretary.Should().BeTrue();
        master.StartsProcedureInPaused.Should().BeFalse();
        master.Priority.Should().BeFalse();
    }
}
