using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

public sealed class FurCompraventaCopropiedadTests
{
    [Fact]
    public void Otorgantes_Traspaso_UsaVendedores()
    {
        var data = Traspaso(Vendedores(3), Compradores(2));
        var o = FurCompraventaCopropiedad.Otorgantes(data);
        o.Should().HaveCount(3);
        o.Select(p => p.Rol).Should().OnlyContain(r => r == "vendedor");
    }

    [Fact]
    public void ListaComa_CuatroNombresConTipoYNumero()
    {
        var texto = FurCompraventaCopropiedad.ListaComa(Vendedores(4));
        texto.Should().Contain("JORGE ENRIQUE ARIZA BERNAL, C.C. 19494493");
        texto.Should().Contain("LUISA FERNANDA CASTRO DIAZ, C.C. 1039442201");
    }

    [Fact]
    public void Identificacion_UnSoloVendedor_ConservaCasillasHistoricas()
    {
        var partes = new List<DocumentParte>
        {
            new("vendedor", "JORMAN AURELIO COPETE SANCHEZ", "10741866", null, DocumentType: "CC", Ordinal: 1),
        };

        var texto = Texto(FurCompraventaCopropiedad.Identificacion(partes));

        texto.Should().Contain("JORMAN AURELIO COPETE SANCHEZ");
        texto.Should().Contain("C.C. [X]");
        texto.Should().Contain("NIT [ ]");
        texto.Should().Contain("10741866");
        texto.Should().NotContain("C.C. 10741866");
        FurCompraventaCopropiedad.CondicionPropietario(partes).Should().Be("propietario(a) inscrito(a)");
    }

    [Fact]
    public void Identificacion_VariosVendedores_SeparanPorComaConTipoYNumero()
    {
        var partes = Vendedores(4);

        var texto = Texto(FurCompraventaCopropiedad.Identificacion(partes));

        texto.Should().Contain("JORGE ENRIQUE ARIZA BERNAL, C.C. 19494493");
        texto.Should().Contain("MONICA BERNAL BUENO, C.C. 31476772");
        texto.Should().Contain("PEDRO JOSE RAMIREZ LOPEZ, C.C. 52988741");
        texto.Should().Contain("LUISA FERNANDA CASTRO DIAZ, C.C. 1039442201");
        texto.Should().NotContain("C.C. [X]");
        FurCompraventaCopropiedad.CondicionPropietario(partes).Should().Be("propietarios inscritos");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Generate_Copropietarios_CabeEnUnaPagina(int nVendedores)
    {
        var data = Traspaso(Vendedores(nVendedores), Compradores(1));
        var pdf = FurCompraventaDocumentGenerator.Generate(data);

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        CountPages(pdf).Should().Be(1);
    }

    [Fact]
    public void Generate_CuatroVendedoresYCuatroCompradores_CabeEnUnaPagina()
    {
        var data = Traspaso(Vendedores(4), Compradores(4));
        var pdf = FurCompraventaDocumentGenerator.Generate(data);

        CountPages(pdf).Should().Be(1);
    }

    [Fact]
    public void Generate_UnVendedorYUnComprador_SigueEnUnaPagina()
    {
        var data = Traspaso(Vendedores(1), Compradores(1));
        var pdf = FurCompraventaDocumentGenerator.Generate(data);

        CountPages(pdf).Should().Be(1);
    }

    private static string Texto(List<(string Text, bool Bold)> fragments) =>
        string.Concat(fragments.Select(f => f.Text));

    private static List<DocumentParte> Vendedores(int n)
    {
        string[] names =
        [
            "JORGE ENRIQUE ARIZA BERNAL",
            "MONICA BERNAL BUENO",
            "PEDRO JOSE RAMIREZ LOPEZ",
            "LUISA FERNANDA CASTRO DIAZ",
        ];
        string[] docs = ["19494493", "31476772", "52988741", "1039442201"];
        return Enumerable.Range(0, n)
            .Select(i => new DocumentParte(
                "vendedor", names[i], docs[i], null, DocumentType: "CC", Ordinal: i + 1))
            .ToList();
    }

    private static List<DocumentParte> Compradores(int n)
    {
        string[] names =
        [
            "EUGENIA MARIA CARDENAS TORRES",
            "JULIO MARIO FONNEGRA SUCERQUIA",
            "ANA LUCIA PEREZ GOMEZ",
            "CARLOS ANDRES RUIZ DIAZ",
        ];
        string[] docs = ["43623787", "98624794", "1020304050", "1122334455"];
        return Enumerable.Range(0, n)
            .Select(i => new DocumentParte(
                "comprador", names[i], docs[i], null, DocumentType: "CC", Ordinal: i + 1))
            .ToList();
    }

    private static FurDocumentData Traspaso(List<DocumentParte> vendedores, List<DocumentParte> compradores)
    {
        List<DocumentParte> partes = [.. vendedores, .. compradores];
        return new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-2026-000099",
            Modalidad: "TRASPASO",
            TipologiaCodigo: "TRASPASO_STANDARD",
            Vehiculo: new VehiculoDatos(
                Marca: "CHEVROLET",
                Linea: "TRACKER",
                Modelo: "2015",
                Color: "BLANCO",
                Clase: "CAMIONETA",
                Combustible: "GASOLINA",
                Cilindraje: "1800",
                Vin: "KL1TD5659FB000001",
                Placa: "ICS187",
                NumeroChasis: "KL1TD5659FB000001",
                NumeroMotor: "M123456"),
            Organismo: new OrganismoTransito("11001000", "OT BOGOTA", "BOGOTA"),
            Partes: partes,
            ValorVenta: 39_700_000m,
            Causal: null,
            SellosFirma: [],
            FechaTramite: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            IdentidadValidada: true,
            SellosIdentidad: partes.ToDictionary(
                p => FurOverlayPartyKey.For(p.Rol, p.Ordinal),
                _ => "Validación biométrica · UUID · Firma KV · 2026"));
    }

    private static int CountPages(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }
}
