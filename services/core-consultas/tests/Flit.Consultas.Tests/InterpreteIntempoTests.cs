using System.Text.Json;
using Flit.Consultas.Api.RuntIntempo;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13874 (F10-H2) — intérprete del crudo plano de Intempo (§4) al <c>VehiculoIntempo</c> tipado con las 22 claves del
/// normalizado (§5), puerto de <c>IntempoApiProxy.getResponseVehicleSpanish</c> (BackCrudTransfer_master, solo lectura).
/// Crudos inventados en línea (placas TST###, documentos 99999…, nombres ficticios); nunca muestras reales.
/// </summary>
/// <example>
/// <code>
/// using var doc = JsonDocument.Parse(crudo);
/// VehiculoIntempo v = InterpreteIntempo.Interpretar(doc.RootElement);
/// </code>
/// </example>
public sealed class InterpreteIntempoTests
{
    /// <summary>Crudo plano inventado de §4: TST201, leasing NIT 999990201 con locatario C 9999902011.</summary>
    internal const string CrudoTst201 = """
        {
          "noPlaca": "TST201", "noVin": "9ZZPRUEBA00000201", "noMotor": "MOTORTST201", "noChasis": "CHASISTST201",
          "noLicenciaTransito": "99999000201", "cilindraje": 1600, "claseVehiculo": "AUTOMOVIL", "clasificacion": "AUTOMOVIL",
          "color": "GRIS", "linea": "LINEA PRUEBA", "marca": "MARCA PRUEBA", "modelo": 2024, "fechaMatricula": "02/02/2024",
          "organismoTransito": "ORGANISMO PRUEBA", "tipoCarroceria": "SEDAN", "tipoCombustible": "GASOLINA",
          "tipoServicioNombre": "Particular", "esRegrabadoChasis": "NO", "esRegrabadoMotor": "NO", "esRegrabadoSerie": "NO",
          "esRegrabadoVin": "NO", "prendas": "SI", "tieneGravamenes": "SI", "repotenciado": "NO",
          "capacidadPasajerosSentados": 5, "serialVersionUID": "TST201-UID",
          "datosTecnicos": { "capacidadCarga": "0", "noEjes": "2", "pesoBrutoVehicular": "1700", "puertas": "4" },
          "gravamenes": [{ "tipoDocumentoAcreedor": "N", "numeroDocumentoAcreedor": "999990299", "nombreAcreedor": "BANCO PRUEBA SA",
                           "fechaInscripcion": "03/02/2024" }],
          "prendasGM": [{ "idPrenda": "1", "idVehiculoPrenda": "2", "fechaRegistro": "03/02/2024", "tipoDocumentoEntidad": "N",
                          "numeroDocumentoEntidad": "999990299", "entidad": "BANCO PRUEBA SA", "estado": "ACTIVA" }],
          "DatosBlindaje": { "esBlindado": "NO" },
          "limitacionesPropiedad": [{ "tipoLimitacion": "ABSTENCION DE TRAMITE", "noDocumento": "99999", "entidadJuridica": "JUZGADO PRUEBA",
                                      "fechaExpedicion": "01/03/2024", "fechaRadicacion": "02/03/2024" }],
          "soatNacionales": [{ "noPoliza": "99999000201", "fechaExpedicion": "01/01/2026", "fechaVigencia": "01/01/2026",
                               "fechaVencimiento": "31/12/2026", "entidadExpideSoat": "ASEGURADORA PRUEBA SA", "estado": "VIGENTE",
                               "nitEntidad": "999990298" }],
          "informacionTarjetaOperacion": { "modalidadServicio": "ESPECIAL" },
          "revisiones": [{ "nroRTM": "RTM-TST201", "tipoRevision": "REVISION TECNICO-MECANICO", "fechaExpedicion": "05/05/2025",
                           "fechaVigente": "05/05/2026", "cdaExpide": "CDA PRUEBA", "vigente": "SI" }],
          "historicoPropietarios": [{ "nombreCompletoPropietario": "PERSONA PRUEBA DOS", "tipoDocumento": "Cédula Ciudadanía",
                                      "numeroDocumento": "9999902012", "fechaInicioPropiedad": "01/01/2020",
                                      "direccion": { "id": 31, "direccion": "CARRERA FICTICIA 9", "idDepartamento": "", "departamento": "DEPARTAMENTO PRUEBA",
                                                     "idCiudad": 5001, "ciudad": "CIUDAD PRUEBA", "telefono": "6000000001" } }],
          "propietariosActuales": [{
            "id": 21, "idTipoDocumento": "N", "tipoDocumento": "NIT", "noDocumento": "999990201", "nombreCompleto": "EMPRESA PRUEBA UNO SAS",
            "tipoPropiedad": 3, "detallePropiedad": "LEASING",
            "direcciones": [{ "id": 22, "direccion": "CALLE FICTICIA 1 2 3", "idDepartamento": 76, "departamento": "DEPARTAMENTO PRUEBA",
                              "idCiudad": 76001, "ciudad": "CIUDAD PRUEBA", "telefono": "6000000000", "email": "contacto@prueba.invalid",
                              "fechaActualizacion": "01/01/2026" }],
            "locatarios": [{ "tipo": 1, "tdoc": "C", "ndoc": "9999902011", "nombreRazonSocial": "PERSONA PRUEBA UNO",
                             "direcciones": [{ "id": 23, "direccion": "AVENIDA FICTICIA 4", "idDepartamento": 76, "departamento": "DEPARTAMENTO PRUEBA",
                                               "idCiudad": 76001, "ciudad": "CIUDAD PRUEBA", "telefono": "6000000002" }] }]
          }],
          "infracciones": [{ "nroComparendo": "99999000000000000201", "fecha": "01/06/2025", "infraccion": "C02", "descripcion": "INFRACCION PRUEBA",
                             "estado": "PENDIENTE", "valorInfraccion": 604100, "placa": "TST201", "reportaFuga": "NO", "accidente": "NO",
                             "secretariaTransito": "SECRETARIA PRUEBA" }]
        }
        """;

    private static VehiculoIntempo Interpretar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return InterpreteIntempo.Interpretar(doc.RootElement);
    }

    private static string ProtoJson(VehiculoIntempo v) =>
        new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true)).Format(v);

    /// <summary>Rutas de claves del proto-JSON: «a.b», «a[].b». Un arreglo vacío o un objeto sin claves solo deja su ruta.</summary>
    private static SortedSet<string> Arbol(string json)
    {
        var rutas = new SortedSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        void Recorrer(JsonElement e, string ruta)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    var hija = ruta.Length == 0 ? p.Name : $"{ruta}.{p.Name}";
                    rutas.Add(hija);
                    Recorrer(p.Value, hija);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    Recorrer(x, ruta + "[]");
        }

        Recorrer(doc.RootElement, "");
        return rutas;
    }

    private static IEnumerable<string> Bajo(string prefijo, params string[] claves) => claves.Select(c => $"{prefijo}.{c}");

    private static readonly string[] Direccion = ["id", "direccion", "idDepartamento", "departamento", "idCiudad", "ciudad", "telefono"];
    private static readonly string[] Persona = ["documentType", "documentNumber", "name", "firstName", "secondName", "firstLastName", "secondLastName"];

    /// <summary>Árbol de §5 (§5.1–§5.5) para un crudo que trae todos los bloques, con la forma de proto-JSON del contrato H1.</summary>
    private static SortedSet<string> ArbolSeccion5()
    {
        var a = new SortedSet<string>(ContratoRuntIntempoTests.ClavesSeccion5, StringComparer.Ordinal);
        a.UnionWith(Bajo("informacionGeneral", "cilidraje", "claseVehiculo", "clasificacion", "color", "linea", "marca", "modelo",
            "fechaMatricula", "noChasis", "noLicenciaTransito", "noMotor", "noPlaca", "noVin", "organismoTransito", "puertas",
            "tipoCarroceria", "tipoCombustible", "tipoServicio", "esRegrabadoChasis", "esRegrabadoMotor", "esRegrabadoSerie",
            "esRegrabadoVin", "prendas", "tieneGravamenes", "repotenciado", "clasicoAntiguo", "estadoDelVehiculo", "mostrarSolicitudes",
            "seguridadEstado", "tarjetaServicio", "verValidaDIAN", "validacionDIAN", "tieneLTImportacion", "serialVersionUID"));
        a.UnionWith(Bajo("datosTecnicos", "capacidadCarga", "noEjes", "pesoBrutoVehicular", "pasajerosSentados", "alto", "ancho", "largo",
            "rodaje", "serialVersionUID"));
        a.UnionWith(Bajo("soat[]", "noPoliza", "fechaExpedicion", "fechaVigencia", "fechaVencimiento", "entidadExpideSoat", "estado"));
        a.UnionWith(Bajo("tecnoMecanica[]", "serialVersionUID", "nroCertificado", "tipoRevision", "fechaExpedicion", "fechaVigente",
            "cdaExpide", "vigente", "numeroPlaca", "informacionConsistente", "url"));
        a.UnionWith(Bajo("garantiasFavorDe[]", "tipoDocumentoAcreedor", "numeroDocumentoAcreedor", "acreedor", "fechaInscripcion", "confecamaras"));
        a.UnionWith(Bajo("garantiasMobiliarias[]", "idPrenda", "idVehiculoPrenda", "fechaRegistro", "tipoDocumentoEntidad",
            "numeroDocumentoEntidad", "entidad", "estado"));
        a.UnionWith(Bajo("informacionBlindaje", "serialVersionUID", "blindado", "nivelBlindaje", "nivelBlindajeNumero", "fechaBlindaje",
            "numeroResolucion", "tipoBlindajeNombre", "fechaExpedicionCertificado", "fechaExpedicionCertificadoFormatoWS", "autorizacion"));
        a.UnionWith(Bajo("limitacionPropiedad[]", "tipoLimitacion", "noDocumento", "entidadJuridica", "tipoDocumentoDemandante",
            "noDocumentoDemandante", "fechaExpedicion", "fechaRadicacion", "departamento", "municipio"));
        a.UnionWith(Bajo("polizasResponsabilidadCivil[]", "serialVersionUID"));
        a.UnionWith(Bajo("tarjetaOperacion[]", "serialVersionUID", "modalidadServicio"));
        a.UnionWith(Bajo("propietariosActuales[]", "id", "idTipoDocumento", "tipoDocumento", "noDocumento", "nombreCompleto", "tipoPropiedad",
            "detallePropiedad", "direcciones", "locatarios"));
        a.UnionWith(Bajo("propietariosActuales[].direcciones[]", [.. Direccion, "email", "fechaActualizacion"]));
        a.UnionWith(Bajo("propietariosActuales[].locatarios[]", "tipo", "tdoc", "ndoc", "nombreRazonSocial", "direcciones"));
        a.UnionWith(Bajo("propietariosActuales[].locatarios[].direcciones[]", Direccion));
        a.UnionWith(Bajo("historicoPropietarios[]", "nombreCompletoPropietario", "tipoDocumento", "numeroDocumento", "fechaInicioPropiedad", "direccion"));
        a.UnionWith(Bajo("historicoPropietarios[].direccion", [.. Direccion, "email", "fechaActualizacion"]));
        a.UnionWith(Bajo("owner[]", Persona));
        a.UnionWith(Bajo("lessee[]", [.. Persona, "lesseeType"]));
        a.UnionWith(Bajo("accidentRate[]", "campos"));
        a.UnionWith(Bajo("infracciones[]", "nroComparendo", "fecha", "infraccion", "descripcion", "estado", "valorInfraccion", "placa",
            "reportaFuga", "accidente", "secretariaTransito"));
        return a;
    }

    [Fact]
    public void El_normalizado_tiene_exactamente_el_arbol_de_claves_de_la_seccion_5()
    {
        var v = Interpretar(CrudoTst201);
        var json = ProtoJson(v);

        using (var doc = JsonDocument.Parse(json))
            doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(ContratoRuntIntempoTests.ClavesSeccion5,
                "22 claves en la raíz, en el orden de §5");
        Arbol(json).Should().BeEquivalentTo(ArbolSeccion5(), o => o.WithStrictOrdering(),
            "el normalizado tiene exactamente el árbol de claves de §5.1–§5.5");

        v.Plate.Should().Be("TST201");
        v.Vin.Should().Be("9ZZPRUEBA00000201");
        v.InformacionGeneral.Cilidraje.Should().Be("1600");
        v.DatosTecnicos.PasajerosSentados.Should().Be("5");
        v.InformacionGeneral.TipoServicio.Should().Be("Particular");
        v.InformacionBlindaje.Blindado.Should().Be("NO");
        v.Soat.Should().ContainSingle().Which.NoPoliza.Should().Be("99999000201");
        v.GarantiasFavorDe.Single().Acreedor.Should().Be("BANCO PRUEBA SA", "acreedor o nombreAcreedor");
        v.Infracciones.Single().ValorInfraccion.Should().Be("604100.00", "entero a texto decimal, como ComparendoDeFuente");
        v.Infracciones.Single().SecretariaTransito.Should().Be("SECRETARIA PRUEBA");
        var dir = v.PropietariosActuales.Single().Direcciones.Single();
        (dir.Id, dir.IdDepartamento, dir.IdCiudad).Should().Be((22L, "76", "76001"), "idDepartamento/idCiudad: número a texto");
        v.HistoricoPropietarios.Single().Direccion.IdDepartamento.Should().Be("", "acepta la cadena vacía");
        v.HistoricoPropietarios.Single().Direccion.IdCiudad.Should().Be("5001");
        v.PropietariosActuales.Single().Locatarios.Single().Direcciones.Single().HasEmail.Should().BeFalse(
            "las direcciones del locatario no traen email ni fechaActualizacion");
    }

    [Fact]
    public void Ficha_toma_cilindraje_como_cilidraje_y_pasajeros_de_la_raiz()
    {
        var v = Interpretar("""
            { "noPlaca": "TST202", "cilindraje": "2000", "capacidadPasajerosSentados": "7", "modelo": 2025, "serialVersionUID": "UID-202",
              "datosTecnicos": { "pasajerosSentados": "99", "puertas": 5, "capacidadCarga": "800", "noEjes": "2" } }
            """);

        v.InformacionGeneral.Cilidraje.Should().Be("2000");
        v.InformacionGeneral.Modelo.Should().Be("2025");
        v.InformacionGeneral.NoPlaca.Should().Be("TST202");
        v.InformacionGeneral.Puertas.Should().Be("5", "puertas sale de datosTecnicos.puertas");
        v.InformacionGeneral.HasNoMotor.Should().BeFalse("noMotor se omite si no viene");
        v.InformacionGeneral.SerialVersionUid.Should().Be("UID-202");
        v.DatosTecnicos.PasajerosSentados.Should().Be("7", "raíz capacidadPasajerosSentados, no el objeto técnico");
        (v.DatosTecnicos.CapacidadCarga, v.DatosTecnicos.NoEjes, v.DatosTecnicos.SerialVersionUid).Should().Be(("800", "2", "UID-202"));
        (v.DatosTecnicos.Alto, v.DatosTecnicos.Ancho, v.DatosTecnicos.Largo, v.DatosTecnicos.Rodaje).Should().Be(("", "", "", ""));
    }

    [Fact]
    public void TipoServicio_cae_a_tipoServicioNombre_y_peso_a_peso()
    {
        var primero = Interpretar("""{ "tipoServicio": "Público", "tipoServicioNombre": "Particular", "datosTecnicos": { "pesoBrutoVehicular": "3000", "peso": "10" } }""");
        primero.InformacionGeneral.TipoServicio.Should().Be("Público", "tipoServicio gana");
        primero.DatosTecnicos.PesoBrutoVehicular.Should().Be("3000");

        var cae = Interpretar("""{ "tipoServicio": "", "tipoServicioNombre": "Oficial", "datosTecnicos": { "peso": 2500 }, "tipoImportacion": "SI" }""");
        cae.InformacionGeneral.TipoServicio.Should().Be("Oficial");
        cae.DatosTecnicos.PesoBrutoVehicular.Should().Be("2500");
        cae.InformacionGeneral.TieneLtImportacion.Should().Be("SI", "tieneLTImportacion o tipoImportacion");

        var nada = Interpretar("{}");
        nada.InformacionGeneral.TipoServicio.Should().Be("");
        nada.DatosTecnicos.PesoBrutoVehicular.Should().Be("");
        nada.InformacionGeneral.TieneLtImportacion.Should().Be("");
    }

    [Fact]
    public void Blindaje_emite_blindado_desde_blindado_o_esBlindado()
    {
        Interpretar("""{ "DatosBlindaje": { "blindado": "SI", "esBlindado": "NO", "nivelBlindaje": "3" } }""").InformacionBlindaje
            .Should().Match<InformacionBlindajeIntempo>(b => b.Blindado == "SI" && b.NivelBlindaje == "3");
        Interpretar("""{ "DatosBlindaje": { "esBlindado": "NO" } }""").InformacionBlindaje.Blindado.Should().Be("NO");
        Interpretar("""{ "datosBlindaje": { "blindado": "SI" } }""").InformacionBlindaje.Blindado.Should().Be("",
            "la clave es PascalCase DatosBlindaje");

        var json = ProtoJson(Interpretar("""{ "DatosBlindaje": { "esBlindado": "NO" } }"""));
        using var doc = JsonDocument.Parse(json);
        var blindaje = doc.RootElement.GetProperty("informacionBlindaje");
        blindaje.TryGetProperty("esBlindado", out _).Should().BeFalse("el servicio nuevo emite solo blindado");
        blindaje.GetProperty("blindado").GetString().Should().Be("NO");
        blindaje.GetProperty("fechaExpedicionCertificadoFormatoWS").GetString().Should().Be("");
    }

    [Fact]
    public void Arreglos_sin_origen_salen_casi_vacios_y_AccidenteVehiculo_en_PascalCase()
    {
        var sin = Interpretar("""{ "noPlaca": "TST203", "solicitudes": [{ "noSolicitud": "1" }] }""");
        sin.NormalizacionSaneamiento.Should().ContainSingle();
        sin.PolizasResponsabilidadCivil.Should().ContainSingle().Which.SerialVersionUid.Should().Be("");
        sin.TarjetaOperacion.Should().ContainSingle().Which.Should().Match<TarjetaOperacionIntempo>(t => t.SerialVersionUid == "" && t.ModalidadServicio == "");
        sin.AccidentRate.Should().ContainSingle().Which.Campos.Should().BeEmpty("accidentRate=[{}]");
        sin.Solicitudes.Should().BeEmpty("solicitudes siempre []");
        sin.GarantiasMobiliarias.Should().BeEmpty();

        var con = Interpretar("""
            { "informacionTarjetaOperacion": { "modalidadServicio": "ESPECIAL" },
              "AccidenteVehiculo": { "fecha": "01/01/2025", "gravedad": 2, "conHeridos": false, "detalle": null },
              "accidenteVehiculo": { "ignorado": "SI" } }
            """);
        con.TarjetaOperacion.Single().ModalidadServicio.Should().Be("ESPECIAL");
        con.AccidentRate.Should().ContainSingle().Which.Campos.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["fecha"] = "01/01/2025",
            ["gravedad"] = "2",
            ["conHeridos"] = "false",
            ["detalle"] = "",
        }, "AccidenteVehiculo en PascalCase, valores como texto");
    }

    [Fact]
    public void TecnoMecanica_copia_nroRTM_y_la_placa_de_la_raiz()
    {
        var v = Interpretar("""
            { "noPlaca": "TST204", "revisiones": [
              { "nroRTM": "RTM-1", "tipoRevision": "REVISION TECNICO-MECANICO", "fechaExpedicion": "01/01/2025", "fechaVigente": "01/01/2026",
                "cdaExpide": "CDA PRUEBA", "vigente": "NO", "url": "http://ignorado.invalid" },
              { "nroRTM": "RTM-2", "vigente": "SI" } ] }
            """);

        v.TecnoMecanica.Should().HaveCount(2);
        var r = v.TecnoMecanica[0];
        (r.SerialVersionUid, r.NroCertificado, r.NumeroPlaca).Should().Be(("RTM-1", "RTM-1", "TST204"));
        (r.TipoRevision, r.FechaExpedicion, r.FechaVigente, r.CdaExpide, r.Vigente).Should()
            .Be(("REVISION TECNICO-MECANICO", "01/01/2025", "01/01/2026", "CDA PRUEBA", "NO"));
        (r.InformacionConsistente, r.Url).Should().Be(("", ""), "fijos");
        v.TecnoMecanica[1].NumeroPlaca.Should().Be("TST204");
        v.TecnoMecanica[1].NroCertificado.Should().Be("RTM-2");
    }

    private const string DosPropietarios = """
        { "noPlaca": "TST205", "propietariosActuales": [
          { "id": 51, "idTipoDocumento": "N", "tipoDocumento": "N", "noDocumento": "999990205", "nombreCompleto": "EMPRESA PRUEBA UNO SAS",
            "tipoPropiedad": 3, "detallePropiedad": "LEASING",
            "locatarios": [
              { "tipo": 1, "tdoc": "C", "ndoc": "9999902051", "nombreRazonSocial": "PERSONA PRUEBA UNO" },
              { "tipo": 2, "tdoc": "CC", "ndoc": "9999902052", "nombreCompleto": "JUAN CARLOS PRUEBA EJEMPLO" } ] },
          { "id": 52, "idTipoDocumento": "C", "tipoDocumento": "Cédula Ciudadanía", "noDocumento": "9999902053",
            "nombreCompleto": "PEDRO PRUEBA", "tipoPropiedad": 1, "detallePropiedad": "PROPIO",
            "locatarios": [{ "tipo": 0, "tdoc": "E", "ndoc": "9999902054", "nombreRazonSocial": "LOCATARIO AJENO" }] } ] }
        """;

    [Fact]
    public void Owner_por_propietario_con_mapa_y_partido_del_primero()
    {
        var v = Interpretar(DosPropietarios);

        v.Owner.Should().HaveCount(2);
        v.Owner.Select(o => (o.DocumentType, o.DocumentNumber, o.Name)).Should().Equal(
            ("NIT", "999990205", "EMPRESA PRUEBA UNO SAS"), ("Cédula Ciudadanía", "9999902053", "PEDRO PRUEBA"));
        v.Owner.Should().AllSatisfy(o =>
        {
            (o.FirstName, o.SecondName, o.FirstLastName, o.SecondLastName).Should().Be(("EMPRESA", "PRUEBA", "UNO", "SAS"),
                "el partido del primer propietario se copia a todos");
            o.HasLesseeType.Should().BeFalse();
        });
        // propietariosActuales conserva la forma cruda: el mapa §6.1 no se aplica aquí.
        v.PropietariosActuales.Select(p => (p.Id, p.TipoDocumento, p.TipoPropiedad)).Should().Equal((51L, "N", 3), (52L, "Cédula Ciudadanía", 1));
        v.PropietariosActuales[1].Locatarios.Should().ContainSingle();
    }

    [Fact]
    public void Lessee_solo_de_los_locatarios_del_primer_propietario_con_lesseeType()
    {
        var v = Interpretar(DosPropietarios);

        v.Lessee.Should().HaveCount(2, "los locatarios del segundo propietario no entran");
        v.Lessee.Select(l => (l.DocumentType, l.DocumentNumber, l.Name, l.LesseeType)).Should().Equal(
            ("Cédula de ciudadanía", "9999902051", "PERSONA PRUEBA UNO", 1),
            ("Cédula de ciudadanía", "9999902052", "JUAN CARLOS PRUEBA EJEMPLO", 2));
        v.Lessee.Should().AllSatisfy(l =>
            (l.FirstName, l.SecondName, l.FirstLastName, l.SecondLastName).Should().Be(("PERSONA", "", "PRUEBA", "UNO"),
                "el partido del primer locatario se copia a todos"));
    }

    [Fact]
    public void Sin_locatarios_lessee_vacio_y_sin_documento_de_raiz_queda_vacio()
    {
        var v = Interpretar("""
            { "noPlaca": "TST206", "propietariosActuales": [{ "id": 61, "tipoDocumento": "Cédula Ciudadanía", "noDocumento": "9999902061",
              "nombreCompleto": "PEDRO CARLOS PRUEBA", "tipoPropiedad": 1, "detallePropiedad": "PROPIO" }] }
            """);
        v.Lessee.Should().BeEmpty();
        v.Owner.Should().ContainSingle();
        v.PropietariosActuales.Single().Locatarios.Should().BeEmpty();
        v.PropietariosActuales.Single().Direcciones.Should().BeEmpty();
        (v.DocumentType, v.DocumentNumber).Should().Be(("", ""), "la raíz no trae documento");

        var conRaiz = Interpretar("""{ "tipoDocumento": "C", "noDocumento": "9999902062" }""");
        (conRaiz.DocumentType, conRaiz.DocumentNumber).Should().Be(("Cédula de ciudadanía", "9999902062"),
            "el número pasa por el mapa, no coincide y se copia tal cual");

        MapaTipoDocumentoIntempo.Mapear("N").Should().Be("NIT");
        MapaTipoDocumentoIntempo.Mapear("NIT").Should().Be("NIT");
        MapaTipoDocumentoIntempo.Mapear("E").Should().Be("Cédula de extranjería");
        MapaTipoDocumentoIntempo.Mapear("P").Should().Be("Pasaporte");
        MapaTipoDocumentoIntempo.Mapear("T").Should().Be("Tarjeta de identidad");
        MapaTipoDocumentoIntempo.Mapear("U").Should().Be("N.U.I.P.");
        MapaTipoDocumentoIntempo.Mapear("D").Should().Be("Carne Diplomático");
        MapaTipoDocumentoIntempo.Mapear("X").Should().Be("N.N.");
        MapaTipoDocumentoIntempo.Mapear("Z").Should().Be("Z", "si la clave no está, se deja el original");
        MapaTipoDocumentoIntempo.Mapear(null).Should().Be("");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "noPlaca": "TST207", "soatNacionales": [], "infracciones": [] }""")]
    [InlineData("""{ "noPlaca": null, "datosTecnicos": null, "propietariosActuales": null, "revisiones": "no-es-arreglo", "gravamenes": [null, 3] }""")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Objeto_vacio_no_lanza_y_deja_valores_por_defecto(string crudo)
    {
        VehiculoIntempo? v = null;
        var interpretar = () => v = Interpretar(crudo);
        interpretar.Should().NotThrow();

        v!.AccidentRate.Should().ContainSingle().Which.Campos.Should().BeEmpty();
        v.NormalizacionSaneamiento.Should().ContainSingle();
        v.Owner.Should().BeEmpty();
        v.Lessee.Should().BeEmpty();
        v.TecnoMecanica.Should().BeEmpty();
        (v.DocumentType, v.DocumentNumber, v.InformacionBlindaje.Blindado).Should().Be(("", "", ""));
        using var doc = JsonDocument.Parse(ProtoJson(v));
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(ContratoRuntIntempoTests.ClavesSeccion5,
            "las 22 claves siguen presentes");
    }
}
