using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Correcciones de la revisión de seguridad del detalle del RUNT en el PDF (ajuste Caso A, 2026-10-27):
/// accidentalidad por lista de PERMITIDOS con etiqueta fija, acreedor con NIT de persona natural enmascarado (rango DIAN de
/// personas jurídicas) y descifrado que nunca deja escapar una excepción ni su texto.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PdfVehiculoDisenoV2Tests
/// </code>
/// </example>
public sealed partial class PdfVehiculoDisenoV2Tests
{
    // Accidentalidad — solo el catálogo cerrado sale, con su etiqueta en español y en el orden del catálogo (no el del origen);
    // claves con tilde, abreviaturas de documento, personas, licencia, placa de otro vehículo y texto libre no salen.
    [Fact]
    public void Accidentalidad_solo_saca_las_claves_del_catalogo_con_su_etiqueta_y_en_su_orden()
    {
        var accidente = new AccidenteIntempo();
        foreach (var (clave, valor) in new[]
                 {
                     ("secretariaTransito", "SECRETARIA PRUEBA"), ("Dirección", "CALLE FICTICIA 1"), ("Cédula", "79123456"),
                     ("Teléfono", "3000000000"), ("nroDoc", "79123457"), ("cc", "79123458"), ("nit", "900111222"),
                     ("victima", "VICTIMA FICTICIA"), ("lesionado", "LESIONADO FICTICIO"), ("conductor", "CONDUCTOR FICTICIO"),
                     ("licencia", "LIC-0001"), ("placaOtroVehiculo", "XYZ987"), ("descripcion", "JUAN PEREZ CC 79123456"),
                     ("con_heridos", "true"), ("CLASE ACCIDENTE", "CHOQUE"), ("Gravedad", "CON HERIDOS"), ("fecha", "02/02/2025"),
                     ("observacion", "OBS FICTICIA"), ("detalle", "DETALLE FICTICIO"),
                 })
            accidente.Campos.Add(clave, valor);

        var datos = Accidentalidad(accidente);

        datos.Should().Equal(
            new DetalleRuntDato("Fecha", "02/02/2025"), new DetalleRuntDato("Gravedad", "CON HERIDOS"),
            new DetalleRuntDato("Clase de accidente", "CHOQUE"), new DetalleRuntDato("Con heridos", "Sí"),
            new DetalleRuntDato("Organismo de tránsito", "SECRETARIA PRUEBA"));
        string.Join('|', datos.Select(d => d.Valor)).Should().NotContainAny(
            ["79123456", "79123457", "79123458", "900111222", "CALLE", "3000000000", "VICTIMA", "LESIONADO", "CONDUCTOR", "LIC-0001",
                "XYZ987", "JUAN PEREZ", "OBS FICTICIA", "DETALLE FICTICIO"]);
    }

    // Accidentalidad — booleanos «false»/«true» salen como «No»/«Sí»; vacíos no salen; alias del catálogo con su etiqueta fija.
    [Fact]
    public void Accidentalidad_traduce_booleanos_omite_vacios_y_usa_la_etiqueta_del_catalogo()
    {
        var accidente = new AccidenteIntempo();
        accidente.Campos.Add("conHeridos", "false");
        accidente.Campos.Add("conFallecidos", "TRUE");
        accidente.Campos.Add("fechaAccidente", "03/03/2024");
        accidente.Campos.Add("municipio", "  ");
        accidente.Campos.Add("tipo-accidente", "ATROPELLO");
        accidente.Campos.Add("organismo.transito", "STRIA PRUEBA");

        Accidentalidad(accidente).Should().Equal(
            new DetalleRuntDato("Fecha", "03/03/2024"), new DetalleRuntDato("Tipo de accidente", "ATROPELLO"),
            new DetalleRuntDato("Con heridos", "No"), new DetalleRuntDato("Con muertos", "Sí"),
            new DetalleRuntDato("Organismo de tránsito", "STRIA PRUEBA"));
    }

    // Garantías — NIT en el rango DIAN de personas jurídicas (9 dígitos que empiezan por 8 o 9, con o sin DV) ⇒ razón social y NIT.
    [Theory]
    [InlineData("900123456")]
    [InlineData("900123456-7")]
    [InlineData("9001234567")]
    [InlineData("800.123.456-1")]
    public void Garantia_con_nit_de_persona_juridica_muestra_nombre_y_nit(string nit)
    {
        var garantia = Garantia("NIT", nit);

        garantia.Should().Be(new DetalleRuntGarantia("BANCO FICTICIO S.A.", "NIT", nit, "SI", "20/03/2020"));
    }

    // Garantías — NIT de persona natural (cédula usada como NIT, 10 dígitos que no son 9 + DV, 8 dígitos con DV) o sin tipo ⇒
    // «Persona natural» y el documento enmascarado.
    [Theory]
    [InlineData("NIT", "79123456")]
    [InlineData("NIT", "1020304050")]
    [InlineData("NIT", "79123456-1")]
    [InlineData("NIT", "123456789")]
    [InlineData("", "900123456")]
    public void Garantia_de_persona_natural_sale_sin_nombre_y_enmascarada(string tipo, string numero)
    {
        var garantia = Garantia(tipo, numero);

        garantia.Acreedor.Should().Be(DetalleRuntGarantia.PersonaNatural);
        garantia.NumeroDocumento.Should().Be(EnmascaradorDocumento.Enmascarar(numero));
        garantia.NumeroDocumento.Should().NotContain(numero);
    }

    // Descifrado — una excepción no prevista del protector ⇒ null (sin propagar ni citar su texto) y el PDF se genera sin él.
    [Fact]
    public void Leer_con_excepcion_no_prevista_devuelve_null_y_el_pdf_no_la_cita()
    {
        var detalle = LectorDetalleRunt.Leer(new ProtectorQueFalla(new InvalidOperationException("SECRETO {\"placa\":\"ABC123\"}")), [1, 2, 3]);

        detalle.Should().BeNull();
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido("ABC123") with { DetalleRunt = detalle }, GeneradoEn));
        texto.Should().NotContain("SECRETO");
        PdfDePrueba.Tiene(texto, "INFORMACIÓN DEL VEHÍCULO").Should().BeTrue(texto);
    }

    // Descifrado — la cancelación sí se propaga (no es un fallo de lectura).
    [Fact]
    public void Leer_propaga_la_cancelacion()
    {
        var leer = () => LectorDetalleRunt.Leer(new ProtectorQueFalla(new OperationCanceledException()), [1, 2, 3]);

        leer.Should().Throw<OperationCanceledException>();
    }

    private static IReadOnlyList<DetalleRuntDato> Accidentalidad(AccidenteIntempo accidente)
    {
        var respuesta = RespuestaRunt("ABC123");
        respuesta.Vehiculo.AccidentRate.Clear();
        respuesta.Vehiculo.AccidentRate.Add(accidente);
        return LectorDetalleRunt.Desde(respuesta)!.Accidentalidad;
    }

    private static DetalleRuntGarantia Garantia(string tipo, string numero)
    {
        var respuesta = RespuestaRunt("ABC123");
        respuesta.Vehiculo.GarantiasFavorDe.Clear();
        respuesta.Vehiculo.GarantiasFavorDe.Add(new GarantiaFavorDeIntempo
        {
            TipoDocumentoAcreedor = tipo,
            NumeroDocumentoAcreedor = numero,
            Acreedor = "BANCO FICTICIO S.A.",
            Confecamaras = "SI",
            FechaInscripcion = "20/03/2020",
        });
        return LectorDetalleRunt.Desde(respuesta)!.GarantiasAFavor.Should().ContainSingle().Subject;
    }

    /// <summary>Protector de prueba que falla al descifrar con la excepción dada.</summary>
    private sealed class ProtectorQueFalla(Exception error) : IProtectorRespuestaFuente
    {
        public byte[] Proteger(string texto) => [];

        public string Desproteger(byte[] cifrado) => throw error;
    }
}
