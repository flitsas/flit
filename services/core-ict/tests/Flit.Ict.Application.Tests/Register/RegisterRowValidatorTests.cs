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
        TransactionType = 2,
        Lessee = new[] { NaturalActor("333") },
    };

    private static bool ErrorOn(RegisterRowInput row, string needle) =>
        Validator.Validate(row).Errors.Exists(e => e.ErrorMessage.Contains(needle, StringComparison.OrdinalIgnoreCase));

    // ===== Caminos felices =====
    [Fact] public void Bilateral_valido_pasa() => Validator.Validate(Bilateral()).IsValid.Should().BeTrue();
    [Fact] public void Matricula_inicial_valida_pasa() => Validator.Validate(MatriculaInicial()).IsValid.Should().BeTrue();
    [Fact] public void Matricula_leasing_valida_pasa() => Validator.Validate(MatriculaLeasing()).IsValid.Should().BeTrue();

    // ===== Tipo de servicio (opcional, solo matrícula) =====
    [Theory]
    [InlineData((short)1)]
    [InlineData((short)2)]
    [InlineData((short)3)]
    [InlineData((short)4)]
    [InlineData((short)5)]
    [InlineData((short)6)]
    public void Vehicle_service_type_dentro_de_rango_pasa(short servicio)
    {
        var row = MatriculaInicial() with { VehicleServiceType = servicio };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Vehicle_service_type_ausente_pasa()
    {
        var row = MatriculaInicial() with { VehicleServiceType = null };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)7)]
    [InlineData((short)-1)]
    public void Vehicle_service_type_fuera_de_rango_es_error(short servicio)
    {
        var row = MatriculaInicial() with { VehicleServiceType = servicio };
        var result = Validator.Validate(row);
        result.IsValid.Should().BeFalse();
        ErrorOn(row, "vehicle_service_type").Should().BeTrue();
    }

    [Fact]
    public void Servicio_publico_sin_empresa_vinculadora_no_es_novedad()
    {
        var row = MatriculaInicial() with
        {
            VehicleServiceType = 2,
            RelatedCompanyDocument = null,
            RelatedCompanyName = null,
        };
        Validator.Validate(row).IsValid.Should().BeTrue();
    }

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

    // ===== Bug #13109 punto 6: plate_assignment_type no participa en traspaso (3, 4) =====
    [Theory]
    [InlineData(3, (short)2)]
    [InlineData(3, (short)7)]
    [InlineData(4, (short)2)]
    [InlineData(4, (short)0)]
    public void Bug13109_P6_Traspaso_ignora_plate_assignment_type(int tipo, short assignment)
    {
        var row = Bilateral() with { TransactionType = tipo, PlateAssignmentType = assignment };

        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(3, "AB12")]
    [InlineData(4, "")]
    [InlineData(3, "ABCDEFGHIJKLMNOP")]
    public void Bug13109_P6_Traspaso_exige_placa_completa_aunque_traiga_assignment_type_2(int tipo, string plate)
    {
        var row = Bilateral() with { TransactionType = tipo, Plate = plate, PlateAssignmentType = 2 };

        ErrorOn(row, "plate").Should().BeTrue();
    }

    [Fact]
    public void Bug13109_P6_Body_de_matricula_como_tipo_3_solo_falla_por_lo_propio_del_traspaso()
    {
        // Reproducción del hallazgo: el body de matrícula (con plate_assignment_type 3 y placa real) enviado
        // como tipo 3. plate_assignment_type no es el motivo del rechazo; lo son el comprador y la
        // compraventa, que siguen siendo obligatorios en traspaso bilateral.
        var row = MatriculaInicial() with { TransactionType = 3, PlateAssignmentType = 3 };

        var errores = Validator.Validate(row).Errors.Select(e => e.ErrorMessage).ToList();

        errores.Should().NotContain(e => e.Contains("plate", StringComparison.OrdinalIgnoreCase));
        errores.Should().Contain(e => e.Contains("buyer"));
        errores.Should().Contain(e => e.Contains("selling_date"));
        errores.Should().Contain(e => e.Contains("selling_price"));
    }

    [Fact]
    public void Bug13109_P6_Matricula_sigue_validando_plate_assignment_type()
    {
        ErrorOn(MatriculaInicial() with { PlateAssignmentType = 7 }, "plate_assignment_type").Should().BeTrue();
        ErrorOn(MatriculaInicial() with { Plate = "ABC123", PlateAssignmentType = 2 }, "plate").Should().BeTrue();
    }

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

    // ===== Feature #14601: paridad de reglas de negocio (P3, P4, P5) =====
    private static RegisterPrincipalMandanteInput ValidMandante(string tipo = "CC") =>
        new(tipo, "987654", "Man", "Dante", Email: "m@e.co");

    private static RegisterRowInput ConRep(RegisterLegalRepresentativeInput rep) =>
        Bilateral() with { Seller = new[] { NitActor(rep) } };

    private static RegisterRowInput ConMandante(RegisterPrincipalMandanteInput m) =>
        Bilateral() with { Seller = new[] { NaturalActor("111") with { PrincipalMandante = m } } };

    [Fact]
    public void Ac1_fila_valida_con_operacion_1_representante_y_mandante_pasa()
    {
        var seller = NitActor(ValidRep() with { Email = "rep@e.co", Phone = "300 123 4567" })
            with
        { PrincipalMandante = ValidMandante() };
        Validator.Validate(Bilateral() with { Seller = new[] { seller } }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Ac4_operacion_actualizar_o_eliminar_se_rechaza_con_mensaje_exacto(int op)
    {
        var errores = Validator.Validate(Bilateral() with { TransactionOperation = op }).Errors
            .Select(e => e.ErrorMessage).ToList();
        errores.Should().ContainSingle().Which.Should().Be(
            "transaction_operation no soportada en el registro: para actualizar use la edición del pre-trámite y para eliminar use abortProcess");
    }

    [Theory]
    [InlineData(0, "transaction_operation es obligatorio")]
    [InlineData(-1, "transaction_operation es obligatorio")]
    [InlineData(4, "transaction_operation invalido")]
    [InlineData(99, "transaction_operation invalido")]
    public void Operacion_fuera_de_1_conserva_obligatorio_o_invalido(int op, string mensaje)
    {
        var errores = Validator.Validate(Bilateral() with { TransactionOperation = op }).Errors
            .Select(e => e.ErrorMessage).ToList();
        errores.Should().ContainSingle().Which.Should().Be(mensaje);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("ZZZ")]
    public void Ac5_representante_con_tipo_documento_no_reconocido_falla(string tipo)
    {
        ErrorOn(ConRep(ValidRep() with { DocumentType = tipo }), "legal_representative_document_type no es valido")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("no-es-correo")]
    [InlineData("a@")]
    public void Ac5_representante_con_email_invalido_falla(string email)
    {
        ErrorOn(ConRep(ValidRep() with { Email = email }), "legal_representative_email formato invalido")
            .Should().BeTrue();
    }

    [Fact]
    public void Representante_con_telefono_de_mas_de_50_digitos_falla()
    {
        var row = ConRep(ValidRep() with { Phone = new string('3', 51) });
        ErrorOn(row, "legal_representative_phone").Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Ac8_representante_con_email_vacio_no_rechaza(string? email)
    {
        Validator.Validate(ConRep(ValidRep() with { Email = email })).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("ZZZ")]
    public void Ac6_mandante_con_tipo_documento_no_reconocido_falla(string tipo)
    {
        ErrorOn(ConMandante(ValidMandante(tipo)), "principal_mandante_document_type no es valido")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("C")]
    [InlineData("N.I.T")]
    [InlineData("nit")]
    public void Alias_validos_de_tipo_documento_se_aceptan_en_representante_y_mandante(string alias)
    {
        Validator.Validate(ConRep(ValidRep() with { DocumentType = alias })).IsValid.Should().BeTrue();
        Validator.Validate(ConMandante(ValidMandante(alias))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Normalizer_guarda_el_tipo_de_representante_y_mandante_normalizado()
    {
        var seller = NitActor(ValidRep() with { DocumentType = "N.I.T" })
            with
        { PrincipalMandante = ValidMandante("c") };
        var master = IctPayloadNormalizer.ToMaster(Bilateral() with { Seller = new[] { seller } }, Guid.NewGuid());
        var actor = master.Actors.Single(a => a.LegalRepresentativeDocumentType is not null);
        actor.LegalRepresentativeDocumentType.Should().Be("NIT");
        actor.PrincipalMandanteDocumentType.Should().Be("CC");
    }

    [Fact]
    public void Actor_sin_documento_falla()
    {
        var badSeller = new RegisterActorInput("CC", "", "Juan", "Perez");
        ErrorOn(Bilateral() with { Seller = new[] { badSeller } }, "document_number").Should().BeTrue();
    }

    // ===== HU #14600 — AC7: transformación fuera del catálogo =====
    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(99)]
    public void Ac7_transformacion_fuera_del_catalogo_rechaza_la_fila(int codigo)
    {
        var row = Bilateral() with
        {
            MoreTransactionTransactionType = new[] { new RegisterTransformationInput(codigo, "x") },
        };

        ErrorOn(row, "more_transaction_transaction_type/transactionType no tiene un valor valido").Should().BeTrue();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(17)]
    public void Ac7_transformacion_del_catalogo_pasa(int codigo)
    {
        var row = Bilateral() with
        {
            MoreTransactionTransactionType = new[] { new RegisterTransformationInput(codigo, "x") },
        };

        Validator.Validate(row).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Normalizer_guarda_el_telefono_del_representante_solo_con_digitos_y_dentro_de_la_columna()
    {
        // 59 caracteres crudos (pasa la guarda de 64) con 50 dígitos: debe guardarse en ≤ 50 (varchar(50)).
        var crudo = string.Join(" ", Enumerable.Repeat("12345", 10));
        crudo.Length.Should().BeGreaterThan(50);
        var rep = ValidRep() with { Phone = crudo };
        var row = ConRep(rep);
        Validator.Validate(row).IsValid.Should().BeTrue();

        var master = IctPayloadNormalizer.ToMaster(row, Guid.NewGuid());
        var guardado = master.Actors.First(a => a.LegalRepresentativeDocumentType is not null).LegalRepresentativePhone;
        guardado.Should().NotBeNull();
        guardado!.Length.Should().BeLessThanOrEqualTo(50);
        guardado.Should().MatchRegex("^[0-9]+$");
    }

    [Fact]
    public void Ac8_sin_transformaciones_no_rechaza() =>
        Validator.Validate(Bilateral() with { MoreTransactionTransactionType = null }).IsValid.Should().BeTrue();
}
