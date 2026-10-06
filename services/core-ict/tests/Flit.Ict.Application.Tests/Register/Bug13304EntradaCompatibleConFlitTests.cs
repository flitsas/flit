using Flit.Ict.Application.Register;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

/// <summary>
/// Bug #13304 (capa 1): core-ict rechaza en la ENTRADA lo que core-api no puede guardar (teléfono &gt; 20,
/// nombre completo &gt; 200, tipo de documento fuera de CC/CE/NIT/PAS/TI) y los textos que excedían la
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

    // ===== Teléfono =====
    [Fact]
    public void Telefono_de_21_digitos_tras_normalizar_se_rechaza()
    {
        // 21 dígitos repartidos con espacios y símbolos: lo que cuenta es lo que viaja (solo dígitos).
        Errores(Actor(phone: "+57 (300) 123-4567 8901 23456"))
            .Should().Contain(m => m.Contains("phone debe tener máximo 20 dígitos"));
    }

    [Fact]
    public void Telefono_de_20_digitos_con_simbolos_pasa_y_viaja_normalizado()
    {
        var phone = "+57 (300) 123-4567 8901 2345"; // 20 dígitos, 28 caracteres
        Errores(Actor(phone: phone)).Should().BeEmpty();

        var master = IctPayloadNormalizer.ToMaster(Bilateral(Actor(phone: phone)), Tenant);
        master.Actors.First(a => a.ActorType == "seller").Phone.Should().Be("57300123456789012345");
    }

    [Fact]
    public void NormalizePhone_deja_solo_digitos() =>
        IctPayloadNormalizer.NormalizePhone(" +57 (604) 444-55.66 ").Should().Be("576044445566");

    // ===== Nombre completo =====
    [Fact]
    public void Nombre_completo_de_201_se_rechaza()
    {
        // 100 + " " + 100 = 201: cada parte cabe en su columna, pero junto excede full_name varchar(200).
        Errores(Actor(name: new string('A', 100), firstLastName: new string('B', 100)))
            .Should().Contain(m => m.Contains("juntos deben tener máximo 200 caracteres"));
    }

    [Fact]
    public void Nombre_completo_de_200_pasa()
    {
        // 99 + " " + 100 = 200.
        Errores(Actor(name: new string('A', 99), firstLastName: new string('B', 100))).Should().BeEmpty();
        IctPayloadNormalizer.ActorFullName(new string('A', 99), new string('B', 100), null).Length.Should().Be(200);
    }

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
}
