using System.Reflection;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// C3-1 — contrato aditivo de <c>ConsultarImpuestoVehicular</c>. El request lleva el documento sin homologar.
/// Un <c>optional</c> ausente no es cero. El proto-JSON no tiene las claves de error del servicio de impuesto.
/// Datos inventados; el test lee el descriptor generado.
/// </summary>
public sealed class ContratoImpuestoTests
{
    private const string Paquete = "flit.consultas.v1";

    private static readonly Assembly Contratos = typeof(ConsultasService).Assembly;

    private static readonly string[] ClavesProhibidas =
    [
        "descriptionError", "descriptionErrorDevelopment", "pendingValidity", "description_error",
        "description_error_development", "pending_validity",
    ];

    private static FileDescriptor Archivo()
    {
        var reflexion = Contratos.GetType("Flit.Consultas.Grpc.V1.ConsultasReflection");
        reflexion.Should().NotBeNull();
        return (FileDescriptor)reflexion!.GetProperty("Descriptor")!.GetValue(null)!;
    }

    private static MessageDescriptor Mensaje(string nombre)
    {
        var mensaje = Archivo().MessageTypes.FirstOrDefault(x => x.Name == nombre);
        mensaje.Should().NotBeNull($"consultas.proto debe declarar {nombre}");
        return mensaje!;
    }

    private static void Campo(MessageDescriptor mensaje, string nombre, int numero, FieldType tipo, bool repetido = false,
        string? tipoMensaje = null, bool opcional = false)
    {
        var campo = mensaje.FindFieldByName(nombre);
        campo.Should().NotBeNull($"{mensaje.Name} debe tener {nombre}");
        campo!.FieldNumber.Should().Be(numero);
        campo.FieldType.Should().Be(tipo);
        campo.IsRepeated.Should().Be(repetido);
        if (tipoMensaje is not null)
            campo.MessageType.FullName.Should().Be(tipoMensaje);
        if (opcional)
            campo.HasPresence.Should().BeTrue($"{mensaje.Name}.{nombre} es optional: ausente no es cero");
    }

    [Fact]
    public void El_request_lleva_documento_y_los_opcionales_distinguen_ausencia()
    {
        var request = Mensaje("ConsultarImpuestoVehicularRequest");
        Campo(request, "opciones", 1, FieldType.Message, tipoMensaje: $"{Paquete}.OpcionesConsulta");
        Campo(request, "placa", 2, FieldType.Message, tipoMensaje: "flit.platform.v1.Placa");
        Campo(request, "departamento_codigo", 3, FieldType.String);
        Campo(request, "documento", 4, FieldType.Message, tipoMensaje: "flit.platform.v1.DocumentoIdentidad");
        Campo(request, "organismo_nombre", 5, FieldType.String, opcional: true);
        Campo(request, "municipio", 6, FieldType.String, opcional: true);
        Campo(request, "departamento_nombre", 7, FieldType.String, opcional: true);
        Campo(request, "modelo", 8, FieldType.Int32, opcional: true);
        Campo(request, "propietario_nombre", 9, FieldType.String, opcional: true);
        request.Fields.InFieldNumberOrder().Should().HaveCount(9);
    }

    [Fact]
    public void Un_modelo_ausente_no_es_cero()
    {
        var tipo = Contratos.GetType("Flit.Consultas.Grpc.V1.ConsultarImpuestoVehicularRequest");
        tipo.Should().NotBeNull();
        var tieneModelo = tipo!.GetProperty("HasModelo");
        tieneModelo.Should().NotBeNull("modelo es optional int32");
        var instancia = Activator.CreateInstance(tipo)!;
        ((bool)tieneModelo!.GetValue(instancia)!).Should().BeFalse();
        tipo.GetProperty("Modelo")!.SetValue(instancia, 0);
        ((bool)tieneModelo.GetValue(instancia)!).Should().BeTrue("cero informado no es lo mismo que ausente");
    }

    [Fact]
    public void La_respuesta_no_guarda_trazas_del_servicio()
    {
        var respuesta = Mensaje("ConsultarImpuestoVehicularResponse");
        Campo(respuesta, "resultado", 1, FieldType.Message, tipoMensaje: $"{Paquete}.ResultadoConsulta");
        Campo(respuesta, "monto_total", 2, FieldType.Int64);
        Campo(respuesta, "vigencias", 3, FieldType.Message, repetido: true, tipoMensaje: $"{Paquete}.VigenciaImpuesto");
        Campo(respuesta, "tiene_deuda", 4, FieldType.Bool);
        Campo(respuesta, "monto_informado", 5, FieldType.Bool);
        Campo(respuesta, "intereses_mora", 6, FieldType.Int64, opcional: true);
        Campo(respuesta, "sanciones", 7, FieldType.Int64, opcional: true);
        Campo(respuesta, "avaluo_comercial", 8, FieldType.Int64, opcional: true);
        Campo(respuesta, "ultimo_pago", 9, FieldType.Message, tipoMensaje: $"{Paquete}.UltimoPagoImpuesto", opcional: true);
        Campo(respuesta, "liquidacion_consolidada_id", 10, FieldType.String, opcional: true);
        Campo(respuesta, "periodo_certificacion", 11, FieldType.String, opcional: true);
        Campo(respuesta, "fecha_consulta_proveedor", 12, FieldType.String, opcional: true);
        Campo(respuesta, "dato_parcial", 13, FieldType.Bool);
        respuesta.FindFieldByName("monto_total")!.HasPresence.Should().BeFalse("el campo 2 sigue sin optional");

        var nombres = respuesta.Fields.InFieldNumberOrder()
            .SelectMany(f => new[] { f.Name, f.JsonName })
            .ToArray();
        nombres.Should().NotContain(ClavesProhibidas);

        var vigencia = Mensaje("VigenciaImpuesto");
        Campo(vigencia, "anio", 1, FieldType.Int32);
        Campo(vigencia, "valor", 2, FieldType.Int64);
        Campo(vigencia, "pagada", 3, FieldType.Bool);
        Campo(vigencia, "liquidacion_id", 4, FieldType.String, opcional: true);
        Campo(vigencia, "avaluo", 5, FieldType.Int64, opcional: true);

        var pago = Mensaje("UltimoPagoImpuesto");
        Campo(pago, "anio", 1, FieldType.Int32, opcional: true);
        Campo(pago, "valor", 2, FieldType.Int64, opcional: true);
        Campo(pago, "fecha", 3, FieldType.String, opcional: true);

        var tipo = Contratos.GetType("Flit.Consultas.Grpc.V1.ConsultarImpuestoVehicularResponse")!;
        var json = JsonFormatter.Default.Format((IMessage)Activator.CreateInstance(tipo)!);
        json.Should().NotContain("descriptionError");
        json.Should().NotContain("descriptionErrorDevelopment");
        json.Should().NotContain("pendingValidity");
    }
}
