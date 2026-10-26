using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13876 (ADR-0075, U1 = b) — cada <c>*Response</c> que se publica en <c>consultas.resultado.diagnostico</c> sale sin nombres,
/// direcciones ni contacto (lista cerrada por rpc) y conserva lo que lee Diagnóstico (placa, VIN, documentos, propiedad, datos
/// técnicos, licencias, estado RUES, comparendos, avalúos y vigencias). Datos inventados (rango ficticio TST / 99999…).
/// <para>Uso de ejemplo: <c>MinimizadorRespuesta.Minimizar(RpcConsultas.PorPlaca, respuestaIntempo)</c>.</para>
/// </summary>
public sealed class MinimizadorRespuestaTests
{
    private const string NombrePropietario = "LEASING FICTICIO SA";
    private const string NombreLocatario = "LOCATARIO PRUEBA DOS";
    private const string NombreHistorico = "HISTORICO FICTICIO UNO";
    private const string Direccion = "CALLE FICTICIA 123";
    private const string Ciudad = "CIUDAD FICTICIA";
    private const string Telefono = "3009999999";
    private const string Correo = "persona@prueba.invalid";
    private const string Acreedor = "ACREEDOR FICTICIO SA";
    private const string Entidad = "ENTIDAD PRENDARIA FICTICIA";
    private const string DetalleProveedor = "cuerpo recortado con NOMBRE FICTICIO";

    private static readonly string[] Prohibidos =
        [NombrePropietario, NombreLocatario, NombreHistorico, Direccion, Ciudad, Telefono, Correo, Acreedor, Entidad, DetalleProveedor, "PEPITO", "PEREZ"];

    [Theory]
    [InlineData(RpcConsultas.PorPlaca)]
    [InlineData(RpcConsultas.PorVin)]
    public void RUNT_Intempo_sin_nombres_direcciones_contacto_ni_detalle_y_con_documentos_y_datos_del_vehiculo(string rpc)
    {
        var original = Intempo();
        var copia = original.Clone();

        var minimizada = (ConsultarVehiculoIntempoResponse)MinimizadorRespuesta.Minimizar(rpc, original);

        SinProhibidos(minimizada);
        original.Should().Be(copia, "no modifica el mensaje original");
        var v = minimizada.Vehiculo;
        (v.Plate, v.Vin, v.InformacionGeneral.Marca, v.InformacionGeneral.Modelo, v.InformacionGeneral.Cilidraje, v.DatosTecnicos.PasajerosSentados)
            .Should().Be(("TST003", "9ZZPRUEBA00000003", "MARCA PRUEBA", "2020", "1600", "5"));
        var propietario = v.PropietariosActuales.Should().ContainSingle().Subject;
        (propietario.IdTipoDocumento, propietario.TipoDocumento, propietario.NoDocumento, propietario.DetallePropiedad, propietario.TipoPropiedad)
            .Should().Be(("N", "NIT", "999990003", "LEASING", 3));
        propietario.Direcciones.Should().BeEmpty();
        var locatario = propietario.Locatarios.Should().ContainSingle().Subject;
        (locatario.Tdoc, locatario.Ndoc, locatario.NombreRazonSocial, locatario.Direcciones.Count).Should().Be(("C", "9999900013", "", 0));
        v.Owner.Should().ContainSingle().Which.DocumentNumber.Should().Be("999990003");
        v.Lessee.Should().ContainSingle().Which.Should().BeEquivalentTo(new { DocumentType = "Cédula de ciudadanía", DocumentNumber = "9999900013", Name = "", LesseeType = 1 },
            o => o.ExcludingMissingMembers());
        v.HistoricoPropietarios.Should().ContainSingle().Which.Direccion.Should().BeNull();
        v.GarantiasMobiliarias.Should().ContainSingle().Which.NumeroDocumentoEntidad.Should().Be("999990077");
        (minimizada.Resultado, minimizada.TipoError, minimizada.HasMensajeError).Should().Be((ResultadoRuntIntempo.Encontrado, TipoErrorFuente.Unspecified, false));
        minimizada.Errores.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Code = 404, Description = "" });
    }

    [Fact]
    public void RUNT_persona_sin_nombres_ni_respuesta_cruda_y_con_identidad_y_licencias()
    {
        var original = new ConsultarConductorResponse { Resultado = Resultado("kyverum_runt") };
        original.Resultado.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Estado = EstadoChequeo.Ok });
        Campos(original.Resultado, ("person_license_status", "ACTIVO"), ("person_license_categories", "B1,C1"));

        var minimizada = (ConsultarConductorResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Conductor, original);

        SinProhibidos(minimizada);
        minimizada.Resultado.RespuestaCruda.Should().BeNull();
        minimizada.Resultado.Chequeos.Should().ContainSingle(c => c.Clave == "conductor_identidad" && c.Estado == EstadoChequeo.Ok);
        ClavesCampos(minimizada.Resultado).Should().Equal("person_type", "person_license_status", "person_license_categories");
    }

    [Fact]
    public void SIMIT_sin_datos_del_propietario_y_con_semaforo_y_comparendos()
    {
        var original = new ConsultarMultasResponse { Resultado = Resultado("verifik_simit") };
        original.Resultado.Semaforo = Semaforo.Rojo;
        var chequeo = new Chequeo { Clave = "multas", Estado = EstadoChequeo.Fail };
        chequeo.Comparendos.Add(new Comparendo { Numero = "SIM-0001", Fecha = "2026-01-10", Valor = "604100.00", Estado = "PENDIENTE", Organismo = "TRANSITO DE PRUEBA" });
        original.Resultado.Chequeos.Add(chequeo);

        var minimizada = (ConsultarMultasResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Multas, original);

        SinProhibidos(minimizada);
        minimizada.Resultado.Semaforo.Should().Be(Semaforo.Rojo);
        minimizada.Resultado.Chequeos.Single().Comparendos.Should().ContainSingle().Which.Numero.Should().Be("SIM-0001");
        ClavesCampos(minimizada.Resultado).Should().Equal("person_license_status", "person_license_categories", "person_type");
    }

    [Fact]
    public void RUES_sin_razon_social_direccion_contacto_ni_representantes_y_con_estado_y_renovacion()
    {
        var original = new ConsultarRuesResponse { Resultado = Resultado("verifik_rues") };
        Campos(original.Resultado, ("rues_estado", "ACTIVA"), ("rues_fecha_renovacion", "2026-03-31"));
        var matricula = new MatriculaMercantil
        {
            Nit = new ValorCertificado { Valor = "999990002" },
            RazonSocial = new ValorCertificado { Valor = NombrePropietario },
            Estado = new EstadoVigencia { Valor = Vigencia.Vigente, Crudo = "ACTIVA" },
            RenovadaEn = new FechaCertificada { Valor = "2026-03-31" },
            Direccion = new ValorCertificado { Valor = Direccion },
            Ciudad = new ValorCertificado { Valor = Ciudad },
        };
        matricula.RepresentantesLegales.Add(new RepresentanteLegal { Nombre = new ValorCertificado { Valor = "PEPITO PEREZ" }, Cargo = new ValorCertificado { Valor = "GERENTE" } });
        original.Resultado.Certificaciones = new Certificaciones();
        original.Resultado.Certificaciones.MatriculasMercantiles.Add(matricula);

        var minimizada = (ConsultarRuesResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Rues, original);

        SinProhibidos(minimizada);
        var m = minimizada.Resultado.Certificaciones.MatriculasMercantiles.Single();
        (m.Nit.Valor, m.Estado.Crudo, m.RenovadaEn.Valor).Should().Be(("999990002", "ACTIVA", "2026-03-31"));
        new[] { m.RazonSocial, m.Direccion, m.Ciudad }.Should().AllSatisfy(v => v.Should().BeNull());
        m.RepresentantesLegales.Single().Should().BeEquivalentTo(new { Nombre = (ValorCertificado?)null, Cargo = new ValorCertificado { Valor = "GERENTE" } });
        ClavesCampos(minimizada.Resultado).Should().Equal("person_license_status", "person_license_categories", "person_type", "rues_estado", "rues_fecha_renovacion");
    }

    [Fact]
    public void Impuesto_sin_datos_personales_y_con_monto_y_vigencias()
    {
        var original = new ConsultarImpuestoVehicularResponse { Resultado = Resultado("impuestos_prueba"), MontoTotal = 400_000 };
        original.Vigencias.Add(new VigenciaImpuesto { Anio = 2026, Valor = 190_000, Pagada = false });

        var minimizada = (ConsultarImpuestoVehicularResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Impuesto, original);

        SinProhibidos(minimizada);
        (minimizada.MontoTotal, minimizada.Vigencias.Single().Valor).Should().Be((400_000, 190_000));
    }

    [Fact]
    public void Avaluos_no_tiene_datos_personales_y_sale_igual()
    {
        var original = new ConsultarAvaluosResponse { ValorSugerido = 52_000_000, FuentePrincipal = "fasecolda" };
        original.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 52_000_000 });

        MinimizadorRespuesta.Minimizar(RpcConsultas.Avaluos, original).Should().Be(original);
    }

    [Fact]
    public void Cada_rpc_del_contrato_tiene_su_regla_y_acepta_su_Response_vacio()
    {
        foreach (var rpc in RpcConsultas.Todos)
            FluentActions.Invoking(() => MinimizadorRespuesta.Minimizar(rpc, MinimizadorRespuesta.RespuestaVacia(rpc))).Should().NotThrow(rpc);
    }

    [Fact]
    public void Un_rpc_sin_regla_o_un_Response_de_otro_rpc_no_se_publican()
    {
        FluentActions.Invoking(() => MinimizadorRespuesta.Minimizar("ConsultasService/ConsultarVehiculo", new ConsultarVehiculoResponse()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MinimizadorRespuesta.Minimizar(RpcConsultas.Conductor, new ConsultarMultasResponse()))
            .Should().Throw<ArgumentException>();
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void SinProhibidos(IMessage mensaje)
    {
        var json = JsonFormatter.Default.Format(mensaje);
        foreach (var valor in Prohibidos)
            json.Should().NotContain(valor);
        foreach (var clave in new[] { "nombre", "direccion", "telefono", "email", "razon_social", "full_name", "first_name", "last_name", "address", "phone", "city" })
            json.Should().NotContain("\"" + clave, "ninguna clave de nombre, dirección ni contacto con valor");
    }

    /// <summary>Un resultado con todos los campos de nombre, dirección y contacto que hoy llenan los proveedores, y respuesta cruda.</summary>
    private static ResultadoConsulta Resultado(string proveedor)
    {
        var r = new ResultadoConsulta
        {
            Proveedor = proveedor,
            Semaforo = Semaforo.Verde,
            RespuestaCruda = new RespuestaCruda { Proveedor = proveedor, TipoSujeto = "person", Json = $$"""{"nombre":"{{NombrePropietario}}"}""" },
        };
        Campos(r,
            ("person_full_name", "PEPITO PEREZ"), ("person_first_name", "PEPITO"), ("person_second_name", "PEPITO"),
            ("person_last_name", "PEREZ"), ("person_first_last_name", "PEREZ"), ("person_second_last_name", "PEREZ"),
            ("person_license_status", "ACTIVO"), ("person_license_categories", "B1"), ("person_type", "NATURAL"),
            ("owner_full_name", NombrePropietario), ("vehicle_owner_name", NombrePropietario), ("vehicle_owner_first_last_name", "PEREZ"),
            ("vehicle_owner_second_last_name", "PEREZ"), ("vehicle_owner_phone", Telefono), ("vehicle_owner_city", Ciudad),
            ("vehicle_owner_address", Direccion), ("lessee_full_name", NombreLocatario), ("lessee_phone", Telefono), ("lessee_email", Correo),
            ("lessee_city", Ciudad), ("lessee_address", Direccion), ("company_name", NombrePropietario), ("rues_razon_social", NombrePropietario),
            ("rues_direccion", Direccion), ("rues_email", Correo), ("rues_representacion_legal", "PEPITO PEREZ"),
            ("rues_snapshot", $$"""{"razonSocial":"{{NombrePropietario}}"}"""));
        r.Campos.Single(c => c.Clave == "rues_snapshot").ValorJson = $$"""{"direccion":"{{Direccion}}"}""";
        return r;
    }

    private static void Campos(ResultadoConsulta r, params (string Clave, string Valor)[] campos)
    {
        foreach (var (clave, valor) in campos)
        {
            if (r.Campos.FirstOrDefault(c => c.Clave == clave) is { } anterior)
                r.Campos.Remove(anterior);
            r.Campos.Add(new Campo { Clave = clave, ValorTexto = valor });
        }
    }

    private static List<string> ClavesCampos(ResultadoConsulta r) => [.. r.Campos.Select(c => c.Clave)];

    private static ConsultarVehiculoIntempoResponse Intempo()
    {
        DireccionIntempo Dir() => new() { Id = 1, Direccion = Direccion, Departamento = Ciudad, Ciudad = Ciudad, Telefono = Telefono, Email = Correo };
        var r = new ConsultarVehiculoIntempoResponse
        {
            Proveedor = "intempo",
            Resultado = ResultadoRuntIntempo.Encontrado,
            MensajeError = DetalleProveedor,
            DuracionMs = 120,
            Vehiculo = new VehiculoIntempo
            {
                Plate = "TST003",
                Vin = "9ZZPRUEBA00000003",
                InformacionGeneral = new InformacionGeneralIntempo { Marca = "MARCA PRUEBA", Modelo = "2020", Cilidraje = "1600", NoPlaca = "TST003" },
                DatosTecnicos = new DatosTecnicosIntempo { PasajerosSentados = "5" },
            },
        };
        r.Errores.Add(new ErrorIntempo { Code = 404, Description = DetalleProveedor });
        var propietario = new PropietarioActualIntempo
        {
            Id = 1,
            IdTipoDocumento = "N",
            TipoDocumento = "NIT",
            NoDocumento = "999990003",
            NombreCompleto = NombrePropietario,
            TipoPropiedad = 3,
            DetallePropiedad = "LEASING",
        };
        propietario.Direcciones.Add(Dir());
        var locatario = new LocatarioIntempo { Tipo = 1, Tdoc = "C", Ndoc = "9999900013", NombreRazonSocial = NombreLocatario };
        locatario.Direcciones.Add(Dir());
        propietario.Locatarios.Add(locatario);
        r.Vehiculo.PropietariosActuales.Add(propietario);
        r.Vehiculo.HistoricoPropietarios.Add(new HistoricoPropietarioIntempo
        {
            NombreCompletoPropietario = NombreHistorico,
            TipoDocumento = "Cédula Ciudadanía",
            NumeroDocumento = "9999900099",
            Direccion = Dir(),
        });
        r.Vehiculo.Owner.Add(new PersonaIntempo { DocumentType = "NIT", DocumentNumber = "999990003", Name = NombrePropietario, FirstName = "PEPITO" });
        r.Vehiculo.Lessee.Add(new PersonaIntempo
        {
            DocumentType = "Cédula de ciudadanía",
            DocumentNumber = "9999900013",
            Name = NombreLocatario,
            FirstName = "PEPITO",
            SecondName = "PEPITO",
            FirstLastName = "PEREZ",
            SecondLastName = "PEREZ",
            LesseeType = 1,
        });
        r.Vehiculo.GarantiasFavorDe.Add(new GarantiaFavorDeIntempo { NumeroDocumentoAcreedor = "999990077", Acreedor = Acreedor });
        r.Vehiculo.GarantiasMobiliarias.Add(new GarantiaMobiliariaIntempo { NumeroDocumentoEntidad = "999990077", Entidad = Entidad, Estado = "ACTIVA" });
        return r;
    }
}
