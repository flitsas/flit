using Flit.Ict.Application.Register;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

/// <summary>
/// Cubre la validación de ENTRADA por fila (contrato Excel "2 - Transacction"), portada de v1
/// ValidationTypeData + ValidateActors. Verde = el body se acepta (Status 1); rojo con el campo
/// esperado = se rechaza (Status 2).
/// </summary>
public sealed class RegisterRowValidatorTests
{
    private static readonly RegisterRowValidator Validator = new();

    private static RegisterActorInput NaturalActor(string doc = "12345678") =>
        new("CC", doc, "Juan Perez", "Perez", Phone: "3001234567", Email: "j@e.co");

    private static RegisterActorInput NitActor(RegisterLegalRepresentativeInput? rep) =>
        new("NIT", "900123456", "Empresa SA", "", LegalRepresentative: rep);

    private static RegisterLegalRepresentativeInput ValidRep() =>
        new(DocumentType: "CC", DocumentNumber: "123456", Name: "Rep", FirstLastName: "Legal");

    // ── Filas base válidas por tipo ──
    private static RegisterRowInput Bilateral() => new(
        TransactionType: 3, TransactionOperation: 1,
        CompanyManagerDocument: "901698038", ManagerUser: "gestor", ManagerMail: "g@demo.co",
        DeliveryAddress: "Calle 1 # 2-3", Plate: "ABC123",
        SellingDate: "05-07-2024", SellingPrice: 15000000m,
        Seller: new[] { NaturalActor("111") }, Buyer: new[] { NaturalActor("222") });

    private static RegisterRowInput MatriculaInicial() => new(
        TransactionType: 1, TransactionOperation: 1,
        CompanyManagerDocument: "901698038", ManagerUser: "gestor", ManagerMail: "g@demo.co",
        DeliveryAddress: "Calle 1", TrafficSecretaryCode: "5001000", Vin: "12Y3SR456RJ789123",
        Plate: "ABC123", Seller: new[] { NaturalActor("111") });

    private static RegisterRowInput MatriculaLeasing() => MatriculaInicial() with
    {
        TransactionType = 2, Lessee = new[] { NaturalActor("333") },
    };

    private static bool ErrorOn(RegisterRowInput row, string needle) =>
        Validator.Validate(row).Errors.Exists(e => e.ErrorMessage.Contains(needle, StringComparison.OrdinalIgnoreCase));

    // ===== Caminos felices =====
    [Fact] public void Bilateral_valido_pasa() => Validator.Validate(Bilateral()).IsValid.Should().BeTrue();
    [Fact] public void Matricula_inicial_valida_pasa() => Validator.Validate(MatriculaInicial()).IsValid.Should().BeTrue();
    [Fact] public void Matricula_leasing_valida_pasa() => Validator.Validate(MatriculaLeasing()).IsValid.Should().BeTrue();

    [Fact]
    public void Matricula_placa_vacia_con_assignment_type_2_pasa()
    {
        var row = MatriculaInicial() with { Plate = "", PlateAssignmentType = 2 };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Seller_nit_con_representante_legal_valido_pasa()
    {
        var row = Bilateral() with { Seller = new[] { NitActor(ValidRep()) } };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    // ===== Presencia de actores por tipo =====
    [Fact] public void Traspaso_sin_buyer_falla() => ErrorOn(Bilateral() with { Buyer = null }, "buyer").Should().BeTrue();
    [Fact] public void Sin_seller_falla() => ErrorOn(Bilateral() with { Seller = null }, "seller").Should().BeTrue();
    [Fact] public void Leasing_sin_lessee_falla() => ErrorOn(MatriculaLeasing() with { Lessee = null }, "lessee").Should().BeTrue();

    [Fact]
    public void Unilateral_tambien_exige_buyer()
    {
        var uni = Bilateral() with { TransactionType = 4, SellingDate = null, SellingPrice = null, Buyer = null };
        ErrorOn(uni, "buyer").Should().BeTrue();
    }

    // ===== Campos raíz obligatorios =====
    [Fact] public void Manager_user_vacio_falla() => ErrorOn(Bilateral() with { ManagerUser = "" }, "manager_user").Should().BeTrue();
    [Fact] public void Delivery_address_vacio_falla() => ErrorOn(Bilateral() with { DeliveryAddress = "" }, "delivery_address").Should().BeTrue();
    [Fact] public void Company_document_vacio_falla() => ErrorOn(Bilateral() with { CompanyManagerDocument = "" }, "company_manager_document").Should().BeTrue();

    // ===== Condicionales por tipo =====
    [Fact] public void Matricula_sin_vin_falla() => ErrorOn(MatriculaInicial() with { Vin = null }, "vin").Should().BeTrue();
    [Fact] public void Matricula_sin_secretaria_falla() => ErrorOn(MatriculaInicial() with { TrafficSecretaryCode = null }, "traffic_secretary_code").Should().BeTrue();
    [Fact] public void Bilateral_sin_selling_date_falla() => ErrorOn(Bilateral() with { SellingDate = null }, "selling_date").Should().BeTrue();
    [Fact] public void Bilateral_sin_selling_price_falla() => ErrorOn(Bilateral() with { SellingPrice = null }, "selling_price").Should().BeTrue();
    [Fact] public void Selling_price_con_tres_decimales_falla() => ErrorOn(Bilateral() with { SellingPrice = 100.123m }, "selling_price").Should().BeTrue();

    [Fact]
    public void Matricula_placa_corta_sin_assignment_type_falla()
    {
        var row = MatriculaInicial() with { Plate = "", PlateAssignmentType = null };
        ErrorOn(row, "plate_assignment_type").Should().BeTrue();
    }

    [Fact] public void Assignment_type_invalido_falla() => ErrorOn(MatriculaInicial() with { PlateAssignmentType = 5 }, "plate_assignment_type").Should().BeTrue();
    [Fact] public void Traspaso_placa_corta_falla() => ErrorOn(Bilateral() with { Plate = "AB12" }, "plate").Should().BeTrue();

    // ===== Actores: forma y condicionales NIT/mandante =====
    [Fact] public void Seller_nit_sin_representante_legal_falla() => ErrorOn(Bilateral() with { Seller = new[] { NitActor(null) } }, "legal_representative").Should().BeTrue();

    [Fact]
    public void Lessee_nit_sin_representante_legal_pasa()
    {
        // El locatario NIT NO exige representante legal (paridad v1 ValidateActors).
        var row = MatriculaLeasing() with { Lessee = new[] { NitActor(null) } };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Mandante_incompleto_falla()
    {
        var seller = NaturalActor("111") with { PrincipalMandante = new RegisterPrincipalMandanteInput(DocumentType: "CC") };
        ErrorOn(Bilateral() with { Seller = new[] { seller } }, "principal_mandante").Should().BeTrue();
    }

    [Fact]
    public void Actor_sin_documento_falla()
    {
        var badSeller = new RegisterActorInput("CC", "", "Juan", "Perez");
        ErrorOn(Bilateral() with { Seller = new[] { badSeller } }, "document_number").Should().BeTrue();
    }
}
