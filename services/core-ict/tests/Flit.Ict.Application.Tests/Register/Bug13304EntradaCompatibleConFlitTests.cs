using Flit.Ict.Application.Register;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

/// <summary>
/// Bug #13304 (capa 1): core-ict rechaza en la ENTRADA lo que core-api no puede guardar (teléfono &gt; 50,
/// nombre completo &gt; 320, tipo de documento fuera de CC/CE/NIT/PAS/TI) y los textos que excedían la
/// columna y daban 500 en /register.
/// <para>Uso de ejemplo:</para>
/// <code>
/// var errores = new RegisterRowValidator().Validate(fila).Errors;   // fila rechazada si hay errores
/// var master  = IctPayloadNormalizer.ToMaster(fila, tenantId);      // "CEDULA" → "CC", "+57 (300)…" → "57300…"
/// </code>
/// </summary>
public sealed class Bug13304EntradaCompatibleConFlitTests
{
    private static readonly RegisterRowValidator Validator = new();
    private static readonly Guid Tenant = Guid.NewGuid();

    private static RegisterActorInput Actor(
        string docType = "CC",
        string name = "Juan",
        string firstLastName = "Perez",
        string? secondLastName = null,
        string? phone = "3001234567",
        string? email = "j@e.co",
        string? address = null) =>
        new(docType, "12345678", name, firstLastName, secondLastName, phone, email, Address: address);

    private static RegisterRowInput Bilateral(RegisterActorInput seller) => new(
        TransactionType: 3, TransactionOperation: 1,
        CompanyManagerDocument: "901698038", ManagerUser: "gestor", ManagerMail: "g@demo.co",
        DeliveryAddress: "Calle 1 # 2-3", Plate: "ABC123",
        SellingDate: "05-07-2024", SellingPrice: 15000000m,
        Seller: new[] { seller }, Buyer: new[] { Actor() });

    private static List<string> Errores(RegisterActorInput seller) =>
        Validator.Validate(Bilateral(seller)).Errors.Select(e => e.ErrorMessage).ToList();

    // ===== Teléfono (core-api phone varchar(50), igual que la columna de ICT) =====
    private static string Digitos(int n) => string.Concat(Enumerable.Range(0, n).Select(i => (char)('0' + (i % 10))));

    [Fact]
    public void Telefono_de_51_digitos_tras_normalizar_se_rechaza()
    {
        // 51 dígitos con prefijo y separadores (58 caracteres crudos, bajo el tope crudo de 64): cuenta lo que viaja.
        Errores(Actor(phone: "+57 (" + Digitos(49) + ")"))
            .Should().Contain(m => m.Contains("phone debe tener máximo 50 dígitos"));
    }

    [Fact]
    public void Telefono_de_50_digitos_con_simbolos_pasa_y_viaja_normalizado()
    {
        var phone = "+57 (" + Digitos(48) + ")"; // 50 dígitos, 55 caracteres
        Errores(Actor(phone: phone)).Should().BeEmpty();

        var master = IctPayloadNormalizer.ToMaster(Bilateral(Actor(phone: phone)), Tenant);
        var guardado = master.Actors.First(a => a.ActorType == "seller").Phone;
        guardado.Should().Be("57" + Digitos(48));
        guardado.Should().HaveLength(50);
    }

    [Fact]
    public void Tope_de_telefono_es_50() => IctPayloadNormalizer.MaxActorPhoneLength.Should().Be(50);

    [Fact]
    public void NormalizePhone_deja_solo_digitos() =>
        IctPayloadNormalizer.NormalizePhone(" +57 (604) 444-55.66 ").Should().Be("576044445566");

    // ===== Nombre completo (core-api full_name varchar(320)) =====
    // Con cada parte en su tope de 100, el armado llega a 302: el tope combinado de 320 solo se alcanza si una
    // parte ya excede su propio tope. Por eso los casos de 320/321 miran el mensaje combinado, no la lista vacía.
    private const string MensajeCombinado = "juntos deben tener máximo 320 caracteres";

    [Fact]
    public void Nombre_completo_de_321_se_rechaza()
    {
        // 121 + " " + 100 + " " + 98 = 321.
        IctPayloadNormalizer.ActorFullName(new string('A', 121), new string('B', 100), new string('C', 98))
            .Length.Should().Be(321);
        Errores(Actor(name: new string('A', 121), firstLastName: new string('B', 100), secondLastName: new string('C', 98)))
            .Should().Contain(m => m.Contains(MensajeCombinado));
    }

    [Fact]
    public void Nombre_completo_de_320_no_dispara_el_tope_combinado()
    {
        // 120 + " " + 100 + " " + 98 = 320 (name > 100 lo rechaza su propio tope, no el combinado).
        IctPayloadNormalizer.ActorFullName(new string('A', 120), new string('B', 100), new string('C', 98))
            .Length.Should().Be(320);
        Errores(Actor(name: new string('A', 120), firstLastName: new string('B', 100), secondLastName: new string('C', 98)))
            .Should().NotContain(m => m.Contains(MensajeCombinado));
    }

    [Fact]
    public void Nombre_completo_con_cada_parte_en_su_tope_pasa()
    {
        // 100 + " " + 100 + " " + 100 = 302 (antes, con full_name varchar(200), se rechazaba).
        Errores(Actor(name: new string('A', 100), firstLastName: new string('B', 100), secondLastName: new string('C', 100)))
            .Should().BeEmpty();
    }

    [Fact]
    public void Tope_de_nombre_completo_es_320() => IctPayloadNormalizer.MaxActorFullNameLength.Should().Be(320);

    [Fact]
    public void ActorFullName_replica_el_armado_del_cliente_grpc() =>
        IctPayloadNormalizer.ActorFullName(" Ana ", "Gomez", " Ruiz ").Should().Be("Ana Gomez Ruiz");

    // ===== Tipo de documento =====
    [Theory]
    [InlineData("XX")]
    [InlineData("NUIP")]
    [InlineData("CD")]
    [InlineData("1")]
    [InlineData("CEDULA RARA")]
    public void Tipo_de_documento_desconocido_se_rechaza(string docType) =>
        Errores(Actor(docType: docType))
            .Should().Contain(m => m.Contains("document_type del actor no es válido"));

    [Theory]
    [InlineData("CEDULA", "CC")]
    [InlineData("cc", "CC")]
    [InlineData("C", "CC")]
    [InlineData("C.C.", "CC")]
    [InlineData("Cédula", "CC")]
    [InlineData("CÉDULA DE CIUDADANÍA", "CC")]
    [InlineData("cedula de  ciudadania", "CC")]
    [InlineData("CE", "CE")]
    [InlineData("Cédula de extranjería", "CE")]
    [InlineData("nit", "NIT")]
    [InlineData("N.I.T.", "NIT")]
    [InlineData("PASAPORTE", "PAS")]
    [InlineData("PA", "PAS")]
    [InlineData("PP", "PAS")]
    [InlineData("pas", "PAS")]
    [InlineData("TARJETA DE IDENTIDAD", "TI")]
    [InlineData("T.I", "TI")]
    public void Alias_se_normaliza_al_codigo(string alias, string esperado) =>
        IctPayloadNormalizer.NormalizeDocumentType(alias).Should().Be(esperado);

    [Fact]
    public void Alias_CEDULA_pasa_la_validacion_y_se_guarda_como_CC()
    {
        Errores(Actor(docType: "CEDULA")).Should().BeEmpty();

        var master = IctPayloadNormalizer.ToMaster(Bilateral(Actor(docType: "CEDULA")), Tenant);
        master.Actors.First(a => a.ActorType == "seller").DocumentType.Should().Be("CC");
    }

    [Fact]
    public void Alias_de_NIT_sigue_exigiendo_representante_legal()
    {
        // IsNit normaliza: "N.I.T." es NIT y, como seller, exige representante legal.
        Errores(new RegisterActorInput("N.I.T.", "900123456", "Empresa SA", ""))
            .Should().Contain(m => m.Contains("legal_representative es obligatorio"));
    }

    // ===== Topes de columna (antes 500) =====
    [Fact]
    public void Email_largo_se_rechaza() =>
        Errores(Actor(email: new string('a', 250) + "@e.com"))
            .Should().Contain(m => m.Contains("email del actor debe tener máximo 255 caracteres"));

    [Fact]
    public void Address_larga_se_rechaza() =>
        Errores(Actor(address: new string('x', 151)))
            .Should().Contain(m => m.Contains("address del actor debe tener máximo 150 caracteres"));

    [Fact]
    public void Email_del_representante_legal_largo_se_rechaza()
    {
        var rep = new RegisterLegalRepresentativeInput("CC", "123456", "Rep", "Legal", Email: new string('a', 256));
        Errores(new RegisterActorInput("NIT", "900123456", "Empresa SA", "", LegalRepresentative: rep))
            .Should().Contain(m => m.Contains("legal_representative_email debe tener máximo 255 caracteres"));
    }

    [Fact]
    public void Mensajes_de_rechazo_no_incluyen_el_valor_recibido()
    {
        // Sin PII en los mensajes: se nombra el campo, nunca el dato.
        var phone = "+57 300 999 888 777 666 555";
        var email = new string('z', 250) + "@pii.co";
        var mensajes = Errores(Actor(phone: phone, email: email, docType: "RARO"));
        mensajes.Should().NotBeEmpty();
        mensajes.Should().NotContain(m => m.Contains("pii.co") || m.Contains("999") || m.Contains("RARO"));
    }

    // ===== Review PR #536: topes de precio y de valor crudo =====
    private static List<string> ErroresPrecio(decimal price) =>
        Validator.Validate(Bilateral(Actor()) with { SellingPrice = price }).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Precio_de_16_enteros_pasa() =>
        ErroresPrecio(9999999999999999.99m).Should().BeEmpty();

    [Fact]
    public void Precio_de_17_enteros_se_rechaza() =>
        ErroresPrecio(10000000000000000m)
            .Should().Contain(m => m.Contains("16 dígitos enteros"));

    [Fact]
    public void DocumentType_y_phone_crudos_de_mas_de_64_se_rechazan_sin_normalizar()
    {
        var mensajes = Errores(Actor(docType: new string('C', 65), phone: new string('5', 65)));
        mensajes.Should().Contain(m => m.Contains("document_type del actor debe tener máximo 64 caracteres"));
        mensajes.Should().Contain(m => m.Contains("phone debe tener máximo 64 caracteres"));
        // Las reglas de normalización no corren sobre el valor crudo excedido.
        mensajes.Should().NotContain(m => m.Contains("document_type del actor no es válido"));
        mensajes.Should().NotContain(m => m.Contains("máximo 50 dígitos"));
    }
}
