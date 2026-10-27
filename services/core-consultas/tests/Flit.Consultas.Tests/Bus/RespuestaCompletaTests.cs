using System.Text.Json;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #14087 AC8 (decisión del usuario 2026-10-27; enmienda de ADR-0075 en Propuesto, revierte U1 = b / C3 para el bus de
/// Diagnóstico) — <see cref="PublicadorResultado"/> publica en <c>consultas.resultado.diagnostico</c> la respuesta COMPLETA del rpc,
/// sin proyección: en proto-JSON con los nombres de campo del .proto (snake_case) y con los valores por defecto incluidos
/// (<c>false</c>, <c>0</c>, <c>""</c>, listas vacías), así que lo que antes descartaba la minimización (color, SOAT, nombres de
/// propietarios, direcciones, histórico, mensajes del proveedor) ahora viaja. Un ERROR sigue sin respuesta ni mensaje: solo el
/// <c>grpcStatus</c>. Datos 100 % ficticios (TST###, 99999…).
/// <para>Uso de ejemplo: <c>new PublicadorResultado(outbox).Publicar(empresa, trabajo, new EjecucionTrabajo(respuesta, null, false, TimeSpan.Zero))</c>.</para>
/// </summary>
public sealed class RespuestaCompletaTests
{
    private const string NombrePropietario = "PROPIETARIO FICTICIO SA";
    private const string NombreHistorico = "HISTORICO FICTICIO UNO";
    private const string Direccion = "CALLE FICTICIA 123";
    private const string MensajeProveedor = "mensaje del proveedor ficticio";

    private readonly OutboxCapturada _outbox = new();

    [Fact]
    public void RUNT_Intempo_viaja_completo_con_los_campos_que_antes_se_descartaban()
    {
        var respuesta = Publicar(RpcConsultas.PorPlaca, Intempo());

        var vehiculo = respuesta.GetProperty("vehiculo");
        vehiculo.GetProperty("informacion_general").GetProperty("color").GetString().Should().Be("ROJO FICTICIO");
        vehiculo.GetProperty("soat").EnumerateArray().Single().GetProperty("no_poliza").GetString().Should().Be("POL-TST-0001");
        var propietario = vehiculo.GetProperty("propietarios_actuales").EnumerateArray().Single();
        propietario.GetProperty("nombre_completo").GetString().Should().Be(NombrePropietario);
        propietario.GetProperty("direcciones").EnumerateArray().Single().GetProperty("direccion").GetString().Should().Be(Direccion);
        vehiculo.GetProperty("historico_propietarios").EnumerateArray().Single().GetProperty("nombre_completo_propietario").GetString()
            .Should().Be(NombreHistorico);
        respuesta.GetProperty("mensaje_error").GetString().Should().Be(MensajeProveedor);
        respuesta.GetProperty("errores").EnumerateArray().Single().GetProperty("description").GetString().Should().Be(MensajeProveedor);
    }

    [Fact]
    public void Los_nombres_son_los_del_proto_y_los_valores_por_defecto_no_desaparecen()
    {
        var respuesta = Publicar(RpcConsultas.PorPlaca, Intempo());

        respuesta.TryGetProperty("informacionGeneral", out _).Should().BeFalse("ni en la raíz ni en el vehículo hay nombres lowerCamelCase");
        var vehiculo = respuesta.GetProperty("vehiculo");
        vehiculo.TryGetProperty("informacionGeneral", out _).Should().BeFalse();
        vehiculo.TryGetProperty("informacion_general", out _).Should().BeTrue();
        vehiculo.GetProperty("soat").EnumerateArray().Single().GetProperty("estado").GetString().Should().BeEmpty("un texto vacío viaja");
        vehiculo.GetProperty("garantias_mobiliarias").ValueKind.Should().Be(JsonValueKind.Array, "una lista vacía viaja");
        vehiculo.GetProperty("garantias_mobiliarias").GetArrayLength().Should().Be(0);
        vehiculo.GetProperty("informacion_general").GetProperty("linea").GetString().Should().BeEmpty("un texto vacío viaja");
        respuesta.GetProperty("vehiculo").GetProperty("propietarios_actuales")[0].GetProperty("tipo_propiedad").GetInt32().Should().Be(0, "un 0 viaja");

        var impuesto = Publicar(RpcConsultas.Impuesto, Impuesto());
        impuesto.GetProperty("vigencias")[0].GetProperty("pagada").ValueKind.Should().Be(JsonValueKind.False, "un booleano false viaja");
        impuesto.GetProperty("resultado").GetProperty("desde_cache").ValueKind.Should().Be(JsonValueKind.False);
    }

    [Theory]
    [MemberData(nameof(Rpcs))]
    public void Cada_rpc_publica_su_Response_entero_y_se_lee_de_vuelta_igual(string rpc, IMessage original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var respuesta = Publicar(rpc, original);

        var leida = JsonParser.Default.Parse(respuesta.GetRawText(), original.Descriptor);
        leida.Should().Be(original, $"{rpc}: sin proyección, la respuesta publicada es la del rpc");
    }

    [Fact]
    public void RUES_y_SIMIT_llevan_terceros_y_texto_libre_del_proveedor()
    {
        var rues = Publicar(RpcConsultas.Rues, Rues()).GetRawText();
        rues.Should().Contain(NombrePropietario).And.Contain("9999900055").And.Contain("razon_social");

        var multas = Publicar(RpcConsultas.Multas, Multas()).GetRawText();
        multas.Should().Contain("infraccion").And.Contain(MensajeProveedor);
    }

    [Fact]
    public void Un_ERROR_sigue_sin_respuesta_ni_mensaje_solo_el_grpcStatus()
    {
        var trabajo = Trabajo(RpcConsultas.PorPlaca);

        var sobre = new PublicadorResultado(_outbox).Publicar(Guid.NewGuid(), trabajo, new EjecucionTrabajo(null, StatusCode.Unavailable, false, TimeSpan.FromSeconds(1)));

        var datos = sobre.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.GrpcStatus, datos.Respuesta).Should().Be(("ERROR", "Unavailable", (JsonElement?)null));
    }

    public static TheoryData<string, IMessage> Rpcs() => new()
    {
        { RpcConsultas.PorPlaca, Intempo() },
        { RpcConsultas.PorVin, Intempo() },
        { RpcConsultas.Conductor, Conductor() },
        { RpcConsultas.Multas, Multas() },
        { RpcConsultas.Rues, Rues() },
        { RpcConsultas.Impuesto, Impuesto() },
        { RpcConsultas.Avaluos, Avaluos() },
    };

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private JsonElement Publicar(string rpc, IMessage respuesta)
    {
        var sobre = new PublicadorResultado(_outbox).Publicar(Guid.NewGuid(), Trabajo(rpc), new EjecucionTrabajo(respuesta, null, false, TimeSpan.FromMilliseconds(5)));
        var datos = sobre.DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be("OK");
        return datos.Respuesta!.Value;
    }

    private static TrabajoConsultaDiagnostico Trabajo(string rpc) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), null, FuentesBus.Runt, rpc, JsonDocument.Parse("{}").RootElement.Clone());

    private static ConsultarVehiculoIntempoResponse Intempo()
    {
        var r = new ConsultarVehiculoIntempoResponse
        {
            Proveedor = "intempo",
            Resultado = ResultadoRuntIntempo.Encontrado,
            MensajeError = MensajeProveedor,
            DuracionMs = 120,
            Vehiculo = new VehiculoIntempo
            {
                Plate = "TST003",
                Vin = "9ZZPRUEBA00000003",
                InformacionGeneral = new InformacionGeneralIntempo { Marca = "MARCA PRUEBA", Modelo = "2020", Color = "ROJO FICTICIO", NoPlaca = "TST003" },
                DatosTecnicos = new DatosTecnicosIntempo { PasajerosSentados = "5" },
            },
        };
        r.Errores.Add(new ErrorIntempo { Code = 404, Description = MensajeProveedor });
        var propietario = new PropietarioActualIntempo
        {
            Id = 1,
            IdTipoDocumento = "N",
            TipoDocumento = "NIT",
            NoDocumento = "999990003",
            NombreCompleto = NombrePropietario,
            DetallePropiedad = "LEASING",
        };
        propietario.Direcciones.Add(new DireccionIntempo { Id = 1, Direccion = Direccion, Ciudad = "CIUDAD FICTICIA", Telefono = "3009999999" });
        r.Vehiculo.PropietariosActuales.Add(propietario);
        r.Vehiculo.HistoricoPropietarios.Add(new HistoricoPropietarioIntempo { NombreCompletoPropietario = NombreHistorico, NumeroDocumento = "9999900099" });
        r.Vehiculo.Soat.Add(new SoatIntempo { NoPoliza = "POL-TST-0001" });
        return r;
    }

    private static ResultadoConsulta Resultado(string proveedor)
    {
        var r = new ResultadoConsulta { Proveedor = proveedor, Semaforo = Semaforo.Verde };
        r.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = "PEPITO FICTICIO" });
        r.Chequeos.Add(new Chequeo { Clave = "principal", Estado = EstadoChequeo.Ok, Mensaje = MensajeProveedor });
        return r;
    }

    private static ConsultarConductorResponse Conductor() => new() { Resultado = Resultado("kyverum_runt") };

    private static ConsultarMultasResponse Multas()
    {
        var r = new ConsultarMultasResponse { Resultado = Resultado("verifik_simit") };
        r.Resultado.Chequeos[0].Comparendos.Add(new Comparendo { Numero = "SIM-0001", Valor = "604100.00", Infraccion = MensajeProveedor });
        return r;
    }

    private static ConsultarRuesResponse Rues()
    {
        var r = new ConsultarRuesResponse { Resultado = Resultado("verifik_rues") };
        var matricula = new MatriculaMercantil { RazonSocial = new ValorCertificado { Valor = NombrePropietario } };
        matricula.RepresentantesLegales.Add(new RepresentanteLegal { NumeroDocumento = new ValorCertificado { Valor = "9999900055" } });
        r.Resultado.Certificaciones = new Certificaciones();
        r.Resultado.Certificaciones.MatriculasMercantiles.Add(matricula);
        return r;
    }

    private static ConsultarImpuestoVehicularResponse Impuesto()
    {
        var r = new ConsultarImpuestoVehicularResponse { Resultado = Resultado("impuestos_prueba"), MontoTotal = 400_000 };
        r.Vigencias.Add(new VigenciaImpuesto { Anio = 2026, Valor = 190_000, Pagada = false });
        return r;
    }

    private static ConsultarAvaluosResponse Avaluos()
    {
        var r = new ConsultarAvaluosResponse { ValorSugerido = 52_000_000, FuentePrincipal = "fasecolda" };
        r.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 52_000_000, Muestras = 3, Mensaje = MensajeProveedor });
        return r;
    }
}
