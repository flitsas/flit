using System.Collections;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13876 (ADR-0075, U1 = b; corrección C3 de la revisión de seguridad, Ley 1581) — cada <c>*Response</c> que se publica en
/// <c>consultas.resultado.diagnostico</c> es una PROYECCIÓN POSITIVA por rpc: solo los campos que leen los mapeos de
/// core-diagnostico. Todo lo demás (nombres, direcciones, contacto, texto libre del proveedor, terceros, claves nuevas) queda fuera,
/// incluido lo que el proto agregue mañana. <see cref="Clasificacion"/> es el contrato: cada campo alcanzable de cada respuesta está
/// clasificado como permitido o descartado, y un campo nuevo del proto rompe <see cref="Ningun_campo_del_proto_queda_sin_clasificar"/>.
/// Datos inventados (rango ficticio TST / 99999…).
/// <para>Uso de ejemplo: <c>MinimizadorRespuesta.Minimizar(RpcConsultas.PorPlaca, respuestaIntempo)</c>.</para>
/// </summary>
public sealed class MinimizadorRespuestaTests
{
    // ── Clasificación: permitido (lo lee core-diagnostico) o descartado ──────────────────────────────────────────────

    private sealed record Regla(string[] Permitidos, string[] Descartados);

    private sealed record Plan(MessageDescriptor Respuesta, IReadOnlyDictionary<string, Regla> Mensajes, string[] Claves);

    private static Dictionary<string, Regla> Reglas(params (string Mensaje, string[] Permitidos, string[] Descartados)[] reglas) =>
        reglas.ToDictionary(r => r.Mensaje, r => new Regla(r.Permitidos, r.Descartados), StringComparer.Ordinal);

    private static readonly Plan PlanIntempo = new(ConsultarVehiculoIntempoResponse.Descriptor, Reglas(
        ("ConsultarVehiculoIntempoResponse", ["resultado", "vehiculo", "tipo_error", "http_status"], ["proveedor", "mensaje_error", "duracion_ms", "errores"]),
        ("VehiculoIntempo", ["plate", "vin", "informacion_general", "datos_tecnicos", "propietarios_actuales", "lessee"],
            ["document_type", "document_number", "soat", "tecno_mecanica", "garantias_favor_de", "garantias_mobiliarias", "informacion_blindaje",
             "limitacion_propiedad", "normalizacion_saneamiento", "polizas_responsabilidad_civil", "solicitudes", "tarjeta_operacion",
             "historico_propietarios", "owner", "accident_rate", "infracciones"]),
        ("InformacionGeneralIntempo",
            ["cilidraje", "clase_vehiculo", "linea", "marca", "modelo", "no_placa", "no_vin", "organismo_transito", "tipo_combustible", "tipo_servicio"],
            ["clasificacion", "color", "fecha_matricula", "no_chasis", "no_licencia_transito", "no_motor", "puertas", "tipo_carroceria",
             "es_regrabado_chasis", "es_regrabado_motor", "es_regrabado_serie", "es_regrabado_vin", "prendas", "tiene_gravamenes", "repotenciado",
             "clasico_antiguo", "estado_del_vehiculo", "mostrar_solicitudes", "seguridad_estado", "tarjeta_servicio", "ver_valida_dian",
             "validacion_dian", "tiene_lt_importacion", "serial_version_uid"]),
        ("DatosTecnicosIntempo", ["pasajeros_sentados"],
            ["capacidad_carga", "no_ejes", "peso_bruto_vehicular", "alto", "ancho", "largo", "rodaje", "serial_version_uid"]),
        ("PropietarioActualIntempo", ["id_tipo_documento", "tipo_documento", "no_documento", "tipo_propiedad", "detalle_propiedad", "locatarios"],
            ["id", "nombre_completo", "direcciones"]),
        ("LocatarioIntempo", ["tdoc", "ndoc"], ["tipo", "nombre_razon_social", "direcciones"]),
        ("PersonaIntempo", ["document_type", "document_number"], ["name", "first_name", "second_name", "first_last_name", "second_last_name", "lessee_type"])),
        []);

    private static (string, string[], string[]) ChequeoSolo() =>
        ("Chequeo", ["clave", "estado"], ["etiqueta", "fuente", "mensaje", "comparendos", "datos"]);

    private static readonly Plan PlanConductor = new(ConsultarConductorResponse.Descriptor, Reglas(
        ("ConsultarConductorResponse", ["resultado"], []),
        ("ResultadoConsulta", ["proveedor", "chequeos", "campos"], ["semaforo", "desde_cache", "consultado_en", "certificaciones", "respuesta_cruda"]),
        ChequeoSolo(),
        ("Campo", ["clave", "valor_texto"], ["valor_json"])),
        ["person_license_status", "person_license_categories"]);

    private static readonly Plan PlanMultas = new(ConsultarMultasResponse.Descriptor, Reglas(
        ("ConsultarMultasResponse", ["resultado"], []),
        ("ResultadoConsulta", ["proveedor", "semaforo", "chequeos"], ["campos", "desde_cache", "consultado_en", "certificaciones", "respuesta_cruda"]),
        ("Chequeo", ["clave", "estado", "comparendos"], ["etiqueta", "fuente", "mensaje", "datos"]),
        ("Comparendo", ["numero", "fecha", "valor", "organismo", "estado"], ["infraccion"])),
        []);

    private static readonly Plan PlanRues = new(ConsultarRuesResponse.Descriptor, Reglas(
        ("ConsultarRuesResponse", ["resultado"], []),
        ("ResultadoConsulta", ["proveedor", "chequeos", "campos", "certificaciones"], ["semaforo", "desde_cache", "consultado_en", "respuesta_cruda"]),
        ChequeoSolo(),
        ("Campo", ["clave", "valor_texto"], ["valor_json"]),
        ("Certificaciones", ["matriculas_mercantiles"], ["soat", "rtm", "vehiculo"]),
        ("MatriculaMercantil", ["estado", "renovada_en"],
            ["nit", "razon_social", "numero_matricula", "matriculada_en", "camara_comercio", "categoria", "direccion", "ciudad", "representantes_legales"]),
        ("EstadoVigencia", ["valor", "crudo"], []),
        ("FechaCertificada", ["valor"], ["crudo"])),
        ["rues_estado", "rues_fecha_renovacion", "rues_ultimo_ano_renovado"]);

    private static readonly Plan PlanImpuesto = new(ConsultarImpuestoVehicularResponse.Descriptor, Reglas(
        ("ConsultarImpuestoVehicularResponse", ["resultado", "monto_total", "vigencias"], []),
        ("ResultadoConsulta", ["proveedor", "chequeos"], ["semaforo", "campos", "desde_cache", "consultado_en", "certificaciones", "respuesta_cruda"]),
        ChequeoSolo(),
        ("VigenciaImpuesto", ["anio", "valor", "pagada"], [])),
        []);

    private static readonly Plan PlanAvaluos = new(ConsultarAvaluosResponse.Descriptor, Reglas(
        ("ConsultarAvaluosResponse", ["valor_sugerido", "fuente_principal", "avaluos"], []),
        ("Avaluo", ["fuente", "estado", "valor", "muestras"], ["mensaje"])),
        []);

    private static readonly Dictionary<string, Plan> Clasificacion = new(StringComparer.Ordinal)
    {
        [RpcConsultas.PorPlaca] = PlanIntempo,
        [RpcConsultas.PorVin] = PlanIntempo,
        [RpcConsultas.Conductor] = PlanConductor,
        [RpcConsultas.Multas] = PlanMultas,
        [RpcConsultas.Rues] = PlanRues,
        [RpcConsultas.Impuesto] = PlanImpuesto,
        [RpcConsultas.Avaluos] = PlanAvaluos,
    };

    /// <summary>Claves de <see cref="Campo"/> que ningún rpc publica: las personales de hoy, las de M2 y una clave nueva cualquiera.</summary>
    private static readonly string[] ClavesProhibidas =
    [
        "person_full_name", "person_first_name", "person_second_name", "person_last_name", "person_first_last_name", "person_second_last_name",
        "person_type", "owner_full_name", "vehicle_owner_name", "vehicle_owner_phone", "vehicle_owner_city", "vehicle_owner_address",
        "lessee_full_name", "lessee_phone", "lessee_email", "lessee_city", "lessee_address", "company_name", "rues_razon_social",
        "rues_direccion", "rues_email", "rues_representacion_legal", "rues_snapshot", "runt_nombre_acreedor", "runt_gravamenes",
        "clave_nueva_de_un_proveedor", "respondio",
    ];

    public static TheoryData<string> Rpcs()
    {
        var datos = new TheoryData<string>();
        foreach (var rpc in RpcConsultas.Todos)
            datos.Add(rpc);
        return datos;
    }

    [Fact]
    public void Cada_rpc_del_contrato_esta_clasificado() =>
        Clasificacion.Keys.Should().BeEquivalentTo(RpcConsultas.Todos);

    [Theory]
    [MemberData(nameof(Rpcs))]
    public void Ningun_campo_del_proto_queda_sin_clasificar(string rpc)
    {
        var plan = Clasificacion[rpc];
        plan.Respuesta.Should().Be(MinimizadorRespuesta.RespuestaVacia(rpc).Descriptor, rpc);
        var visitados = new HashSet<string>(StringComparer.Ordinal);
        var pendientes = new Queue<MessageDescriptor>([plan.Respuesta]);
        while (pendientes.TryDequeue(out var mensaje))
        {
            if (!visitados.Add(mensaje.Name))
                continue;
            plan.Mensajes.Should().ContainKey(mensaje.Name, $"{rpc}: {mensaje.Name} es alcanzable por un campo permitido y debe estar clasificado");
            var regla = plan.Mensajes[mensaje.Name];
            regla.Permitidos.Intersect(regla.Descartados).Should().BeEmpty($"{rpc}: {mensaje.Name} no puede permitir y descartar el mismo campo");
            var campos = mensaje.Fields.InFieldNumberOrder().Select(f => f.Name).ToList();
            campos.Should().BeEquivalentTo(regla.Permitidos.Concat(regla.Descartados),
                $"{rpc}: cada campo de {mensaje.Name} está clasificado (permitido o descartado) y no hay nombres que no existan");
            foreach (var campo in mensaje.Fields.InFieldNumberOrder().Where(f => f.FieldType == FieldType.Message && regla.Permitidos.Contains(f.Name)))
            {
                campo.IsMap.Should().BeFalse($"{rpc}: {mensaje.Name}.{campo.Name} es un mapa clave-valor libre y no se permite");
                pendientes.Enqueue(campo.MessageType);
            }
        }

        plan.Mensajes.Keys.Should().BeEquivalentTo(visitados, $"{rpc}: no hay reglas de mensajes que no se alcanzan");
    }

    [Theory]
    [MemberData(nameof(Rpcs))]
    public void Con_todos_los_campos_llenos_solo_publica_la_lista_permitida(string rpc)
    {
        var plan = Clasificacion[rpc];
        var semillas = new Semillas();
        var original = semillas.Llenar(plan.Respuesta, plan.Claves);
        var copia = original.Descriptor.Parser.ParseFrom(original.ToByteString());

        var minimizada = MinimizadorRespuesta.Minimizar(rpc, original);

        original.Should().Be(copia, "no modifica el mensaje original");
        minimizada.Descriptor.Should().Be(plan.Respuesta);
        Presentes(minimizada).Should().BeEquivalentTo(Permitidos(plan), $"{rpc}: publica exactamente los campos permitidos, todos llenos");
        var json = JsonFormatter.Default.Format(minimizada);
        foreach (var (valor, campo) in semillas.Sembradas.Where(s => !Permitidos(plan).Contains(s.Campo)))
            json.Should().NotContain(valor, $"{rpc}: {campo} está descartado");
        foreach (var clave in ClavesProhibidas)
            json.Should().NotContain("\"" + clave + "\"", $"{rpc}: la clave {clave} no está permitida");
    }

    [Theory]
    [MemberData(nameof(Rpcs))]
    public void En_ResultadoConsulta_solo_salen_las_claves_permitidas_del_rpc(string rpc)
    {
        var plan = Clasificacion[rpc];
        var original = new Semillas().Llenar(plan.Respuesta, plan.Claves);
        if (original.Descriptor.FindFieldByName("resultado") is not { FieldType: FieldType.Message } campoResultado)
            return; // RUNT por Intempo y avalúos no traen ResultadoConsulta.

        var minimizada = (ResultadoConsulta)campoResultado.Accessor.GetValue(MinimizadorRespuesta.Minimizar(rpc, original));

        minimizada.Campos.Select(c => c.Clave).Should().Equal(plan.Claves, rpc);
        minimizada.Campos.Should().NotContain(c => c.HasValorJson, "valor_json es JSON libre");
    }

    [Fact]
    public void RUNT_Intempo_los_hallazgos_M1_no_viajan_y_los_documentos_y_datos_del_vehiculo_si()
    {
        var original = Intempo();

        var minimizada = (ConsultarVehiculoIntempoResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.PorPlaca, original);

        SinProhibidos(minimizada);
        var v = minimizada.Vehiculo;
        (v.Plate, v.Vin, v.InformacionGeneral.Marca, v.InformacionGeneral.Modelo, v.InformacionGeneral.Cilidraje, v.DatosTecnicos.PasajerosSentados)
            .Should().Be(("TST003", "9ZZPRUEBA00000003", "MARCA PRUEBA", "2020", "1600", "5"));
        var propietario = v.PropietariosActuales.Should().ContainSingle().Subject;
        (propietario.IdTipoDocumento, propietario.TipoDocumento, propietario.NoDocumento, propietario.DetallePropiedad, propietario.TipoPropiedad)
            .Should().Be(("N", "NIT", "999990003", "LEASING", 3));
        propietario.Locatarios.Should().ContainSingle().Which.Should().Be(new LocatarioIntempo { Tdoc = "C", Ndoc = "9999900013" });
        v.Lessee.Should().ContainSingle().Which.Should().Be(new PersonaIntempo { DocumentType = "Cédula de ciudadanía", DocumentNumber = "9999900013" });
        (v.Owner.Count, v.HistoricoPropietarios.Count, v.AccidentRate.Count, v.LimitacionPropiedad.Count, v.GarantiasFavorDe.Count, v.GarantiasMobiliarias.Count)
            .Should().Be((0, 0, 0, 0, 0, 0));
        (minimizada.Resultado, minimizada.HasMensajeError, minimizada.Errores.Count).Should().Be((ResultadoRuntIntempo.Encontrado, false, 0));
    }

    [Fact]
    public void RUNT_persona_solo_identidad_y_licencias()
    {
        var original = new ConsultarConductorResponse { Resultado = Resultado("kyverum_runt") };
        original.Resultado.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Estado = EstadoChequeo.Ok, Mensaje = DetalleProveedor, Etiqueta = "Persona" });
        original.Resultado.Chequeos[0].Datos.Add(new Dato { Etiqueta = "Nombre", Valor = NombrePropietario });
        Campos(original.Resultado, ("person_license_status", "ACTIVO"), ("person_license_categories", "B1,C1"));

        var minimizada = (ConsultarConductorResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Conductor, original);

        SinProhibidos(minimizada);
        minimizada.Resultado.Chequeos.Should().ContainSingle().Which.Should().Be(new Chequeo { Clave = "conductor_identidad", Estado = EstadoChequeo.Ok });
        minimizada.Resultado.Campos.Select(c => (c.Clave, c.ValorTexto)).Should().Equal(("person_license_status", "ACTIVO"), ("person_license_categories", "B1,C1"));
        minimizada.Resultado.Proveedor.Should().Be("kyverum_runt");
        minimizada.Resultado.RespuestaCruda.Should().BeNull();
    }

    [Fact]
    public void SIMIT_semaforo_y_comparendos_sin_infraccion_ni_mensaje()
    {
        var original = new ConsultarMultasResponse { Resultado = Resultado("verifik_simit") };
        original.Resultado.Semaforo = Semaforo.Rojo;
        var chequeo = new Chequeo { Clave = "multas", Estado = EstadoChequeo.Fail, Mensaje = DetalleProveedor };
        chequeo.Comparendos.Add(new Comparendo
        {
            Numero = "SIM-0001",
            Fecha = "2026-01-10",
            Valor = "604100.00",
            Estado = "PENDIENTE",
            Organismo = "TRANSITO DE PRUEBA",
            Infraccion = DetalleProveedor,
        });
        original.Resultado.Chequeos.Add(chequeo);

        var minimizada = (ConsultarMultasResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Multas, original);

        SinProhibidos(minimizada);
        minimizada.Resultado.Semaforo.Should().Be(Semaforo.Rojo);
        minimizada.Resultado.Chequeos.Single().Comparendos.Should().ContainSingle().Which.Should().Be(new Comparendo
        {
            Numero = "SIM-0001",
            Fecha = "2026-01-10",
            Valor = "604100.00",
            Estado = "PENDIENTE",
            Organismo = "TRANSITO DE PRUEBA",
        });
        minimizada.Resultado.Campos.Should().BeEmpty();
    }

    [Fact]
    public void RUES_solo_estado_y_renovacion_sin_NIT_razon_social_ni_documentos_de_representantes()
    {
        var original = new ConsultarRuesResponse { Resultado = Resultado("verifik_rues") };
        Campos(original.Resultado, ("rues_estado", "ACTIVA"), ("rues_fecha_renovacion", "2026-03-31"));
        var matricula = new MatriculaMercantil
        {
            Nit = new ValorCertificado { Valor = "999990002" },
            RazonSocial = new ValorCertificado { Valor = NombrePropietario },
            Estado = new EstadoVigencia { Valor = Vigencia.Vigente, Crudo = "ACTIVA" },
            RenovadaEn = new FechaCertificada { Valor = "2026-03-31", Crudo = DetalleProveedor },
            Direccion = new ValorCertificado { Valor = Direccion },
            Ciudad = new ValorCertificado { Valor = Ciudad },
        };
        matricula.RepresentantesLegales.Add(new RepresentanteLegal
        {
            Nombre = new ValorCertificado { Valor = "PEPITO PEREZ" },
            TipoDocumento = new ValorCertificado { Valor = "CC" },
            NumeroDocumento = new ValorCertificado { Valor = "9999900055" },
            Cargo = new ValorCertificado { Valor = "GERENTE" },
        });
        original.Resultado.Certificaciones = new Certificaciones();
        original.Resultado.Certificaciones.MatriculasMercantiles.Add(matricula);

        var minimizada = (ConsultarRuesResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Rues, original);

        SinProhibidos(minimizada);
        JsonFormatter.Default.Format(minimizada).Should().NotContain("9999900055").And.NotContain("999990002");
        minimizada.Resultado.Certificaciones.MatriculasMercantiles.Should().ContainSingle().Which.Should().Be(new MatriculaMercantil
        {
            Estado = new EstadoVigencia { Valor = Vigencia.Vigente, Crudo = "ACTIVA" },
            RenovadaEn = new FechaCertificada { Valor = "2026-03-31" },
        });
        minimizada.Resultado.Campos.Select(c => c.Clave).Should().Equal("rues_estado", "rues_fecha_renovacion");
    }

    [Fact]
    public void Impuesto_monto_y_vigencias()
    {
        var original = new ConsultarImpuestoVehicularResponse { Resultado = Resultado("impuestos_prueba"), MontoTotal = 400_000 };
        original.Vigencias.Add(new VigenciaImpuesto { Anio = 2026, Valor = 190_000, Pagada = false });

        var minimizada = (ConsultarImpuestoVehicularResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Impuesto, original);

        SinProhibidos(minimizada);
        (minimizada.MontoTotal, minimizada.Vigencias.Single().Valor, minimizada.Resultado.Proveedor).Should().Be((400_000, 190_000, "impuestos_prueba"));
        minimizada.Resultado.Campos.Should().BeEmpty();
    }

    [Fact]
    public void Avaluos_sin_el_mensaje_del_proveedor()
    {
        var original = new ConsultarAvaluosResponse { ValorSugerido = 52_000_000, FuentePrincipal = "fasecolda" };
        original.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 52_000_000, Muestras = 3, Mensaje = DetalleProveedor });

        var minimizada = (ConsultarAvaluosResponse)MinimizadorRespuesta.Minimizar(RpcConsultas.Avaluos, original);

        minimizada.Should().Be(new ConsultarAvaluosResponse
        {
            ValorSugerido = 52_000_000,
            FuentePrincipal = "fasecolda",
            Avaluos = { new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 52_000_000, Muestras = 3 } },
        });
    }

    // ── Compatibilidad con lo que lee core-diagnostico ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rutas que leen los mapeos de core-diagnostico (<c>services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Consultas/</c>).
    /// <c>campos[clave]</c> elige el <see cref="Campo"/> por clave.
    /// </summary>
    private static readonly Dictionary<string, (string Ruta, string Lector)[]> LecturasDiagnostico = new(StringComparer.Ordinal)
    {
        [RpcConsultas.PorPlaca] =
        [
            ("resultado", "Mapeos/MapperIntempo.cs:79"),
            ("tipo_error", "Mapeos/MapperIntempo.cs:193"),
            ("http_status", "Mapeos/MapperIntempo.cs:192"),
            ("vehiculo.plate", "Mapeos/MapperIntempo.cs:137"),
            ("vehiculo.informacion_general.no_placa", "Mapeos/MapperIntempo.cs:137"),
            ("vehiculo.vin", "Mapeos/MapperIntempo.cs:138"),
            ("vehiculo.informacion_general.no_vin", "Mapeos/MapperIntempo.cs:138"),
            ("vehiculo.informacion_general.tipo_servicio", "Mapeos/MapperIntempo.cs:139"),
            ("vehiculo.informacion_general.clase_vehiculo", "Mapeos/MapperIntempo.cs:140"),
            ("vehiculo.informacion_general.organismo_transito", "Mapeos/MapperIntempo.cs:141"),
            ("vehiculo.informacion_general.marca", "Mapeos/MapperIntempo.cs:148"),
            ("vehiculo.informacion_general.linea", "Mapeos/MapperIntempo.cs:149"),
            ("vehiculo.informacion_general.modelo", "Mapeos/MapperIntempo.cs:150"),
            ("vehiculo.informacion_general.cilidraje", "Mapeos/MapperIntempo.cs:151"),
            ("vehiculo.informacion_general.tipo_combustible", "Mapeos/MapperIntempo.cs:152"),
            ("vehiculo.datos_tecnicos.pasajeros_sentados", "Mapeos/MapperIntempo.cs:153"),
            ("vehiculo.propietarios_actuales.no_documento", "Mapeos/MapperIntempo.cs:159"),
            ("vehiculo.propietarios_actuales.id_tipo_documento", "Mapeos/MapperIntempo.cs:160"),
            ("vehiculo.propietarios_actuales.tipo_documento", "Mapeos/MapperIntempo.cs:160"),
            ("vehiculo.propietarios_actuales.locatarios.tdoc", "Mapeos/MapperIntempo.cs:164"),
            ("vehiculo.propietarios_actuales.locatarios.ndoc", "Mapeos/MapperIntempo.cs:164"),
            ("vehiculo.propietarios_actuales.detalle_propiedad", "Mapeos/MapperIntempo.cs:178"),
            ("vehiculo.propietarios_actuales.tipo_propiedad", "Mapeos/MapperIntempo.cs:182"),
            ("vehiculo.lessee.document_type", "Mapeos/MapperIntempo.cs:130"),
            ("vehiculo.lessee.document_number", "Mapeos/MapperIntempo.cs:130"),
        ],
        [RpcConsultas.Conductor] =
        [
            ("resultado.proveedor", "Mapeos/MapeoRuntPersona.cs:202"),
            ("resultado.chequeos.clave", "Mapeos/MapeoRuntPersona.cs:215; Fuentes/TraductorErroresConsultas.cs:91"),
            ("resultado.chequeos.estado", "Mapeos/MapeoRuntPersona.cs:215; Fuentes/TraductorErroresConsultas.cs:90"),
            ("resultado.campos[person_license_status].valor_texto", "Mapeos/MapeoRuntPersona.cs:216,226"),
            ("resultado.campos[person_license_categories].valor_texto", "Mapeos/MapeoRuntPersona.cs:217,226"),
        ],
        [RpcConsultas.Multas] =
        [
            ("resultado.proveedor", "Mapeos/MapeoSimit.cs:252"),
            ("resultado.semaforo", "Fuentes/ResultadoSimit.cs:37"),
            ("resultado.chequeos.clave", "Fuentes/TraductorErroresConsultas.cs:91"),
            ("resultado.chequeos.estado", "Fuentes/TraductorErroresConsultas.cs:90"),
            ("resultado.chequeos.comparendos.numero", "Fuentes/ResultadoSimit.cs:29"),
            ("resultado.chequeos.comparendos.fecha", "Fuentes/ResultadoSimit.cs:33"),
            ("resultado.chequeos.comparendos.valor", "Fuentes/ResultadoSimit.cs:33"),
            ("resultado.chequeos.comparendos.estado", "Fuentes/ResultadoSimit.cs:33"),
            ("resultado.chequeos.comparendos.organismo", "Fuentes/ResultadoSimit.cs:34"),
        ],
        [RpcConsultas.Rues] =
        [
            ("resultado.proveedor", "Mapeos/MapeoRues.cs:97"),
            ("resultado.chequeos.clave", "Fuentes/TraductorErroresConsultas.cs:91"),
            ("resultado.chequeos.estado", "Fuentes/TraductorErroresConsultas.cs:90"),
            ("resultado.certificaciones.matriculas_mercantiles.estado.crudo", "Mapeos/MapeoRues.cs:111"),
            ("resultado.certificaciones.matriculas_mercantiles.estado.valor", "Mapeos/MapeoRues.cs:111,116"),
            ("resultado.certificaciones.matriculas_mercantiles.renovada_en.valor", "Mapeos/MapeoRues.cs:112"),
            ("resultado.campos[rues_estado].valor_texto", "Mapeos/MapeoRues.cs:111,126"),
            ("resultado.campos[rues_fecha_renovacion].valor_texto", "Mapeos/MapeoRues.cs:112,126"),
            ("resultado.campos[rues_ultimo_ano_renovado].valor_texto", "Mapeos/MapeoRues.cs:112,126"),
        ],
        [RpcConsultas.Impuesto] =
        [
            ("resultado.proveedor", "Mapeos/MapeoImpuesto.cs:63"),
            ("resultado.chequeos.clave", "Fuentes/TraductorErroresConsultas.cs:91"),
            ("resultado.chequeos.estado", "Fuentes/TraductorErroresConsultas.cs:90"),
            ("monto_total", "Fuentes/ResultadoImpuesto.cs:34"),
            ("vigencias.anio", "Fuentes/ResultadoImpuesto.cs:31-32"),
            ("vigencias.valor", "Fuentes/ResultadoImpuesto.cs:32"),
            ("vigencias.pagada", "Fuentes/ResultadoImpuesto.cs:32"),
        ],
        [RpcConsultas.Avaluos] =
        [
            ("valor_sugerido", "Fuentes/ResultadoAvaluo.cs:50-51"),
            ("fuente_principal", "Fuentes/ResultadoAvaluo.cs:51"),
            ("avaluos.fuente", "Fuentes/ResultadoAvaluo.cs:41,46"),
            ("avaluos.estado", "Fuentes/ResultadoAvaluo.cs:44"),
            ("avaluos.valor", "Fuentes/ResultadoAvaluo.cs:46"),
            ("avaluos.muestras", "Fuentes/ResultadoAvaluo.cs:47"),
        ],
    };

    [Theory]
    [MemberData(nameof(Rpcs))]
    public void Las_respuestas_de_fixtures_minimizadas_conservan_todo_lo_que_lee_diagnostico(string rpc)
    {
        var lecturas = LecturasDiagnostico[rpc == RpcConsultas.PorVin ? RpcConsultas.PorPlaca : rpc];
        var plan = Clasificacion[rpc];
        foreach (var (ruta, lector) in lecturas)
            Permitida(plan, ruta).Should().BeTrue($"{rpc}: {ruta} lo lee core-diagnostico ({lector}) y debe estar permitida");

        var respuestas = Fixtures(rpc).Append(new Semillas().Llenar(plan.Respuesta, plan.Claves)).ToList();
        respuestas.Count.Should().BeGreaterThan(1, $"{rpc}: hay fixtures además de la respuesta sembrada");
        var leidas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var original in respuestas)
        {
            var minimizada = MinimizadorRespuesta.Minimizar(rpc, original);
            foreach (var (ruta, lector) in lecturas)
            {
                var antes = Valores(original, ruta);
                Valores(minimizada, ruta).Should().Equal(antes, $"{rpc}: {ruta} ({lector}) sale igual que en la respuesta del proveedor");
                if (antes.Count > 0)
                    leidas.Add(ruta);
            }
        }

        leidas.Should().BeEquivalentTo(lecturas.Select(l => l.Ruta), $"{rpc}: cada ruta se comprueba con al menos un valor");
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
    private const string Demandante = "9999900044";
    private const string Accidente = "ACCIDENTE CON NOMBRE FICTICIO";

    private static readonly string[] Prohibidos =
    [
        NombrePropietario, NombreLocatario, NombreHistorico, Direccion, Ciudad, Telefono, Correo, Acreedor, Entidad, DetalleProveedor, Demandante,
        Accidente, "PEPITO", "PEREZ",
    ];

    private static void SinProhibidos(IMessage mensaje)
    {
        var json = JsonFormatter.Default.Format(mensaje);
        foreach (var valor in Prohibidos)
            json.Should().NotContain(valor);
        foreach (var clave in new[] { "nombre", "direccion", "telefono", "email", "razon_social", "full_name", "first_name", "last_name", "address", "phone", "city", "mensaje", "demandante" })
            json.Should().NotContain("\"" + clave, "ninguna clave de nombre, dirección, contacto ni texto libre con valor");
    }

    /// <summary>Un resultado con los campos de nombre, dirección y contacto que llenan los proveedores, las claves de M2 y respuesta cruda.</summary>
    private static ResultadoConsulta Resultado(string proveedor)
    {
        var r = new ResultadoConsulta
        {
            Proveedor = proveedor,
            Semaforo = Semaforo.Verde,
            RespuestaCruda = new RespuestaCruda { Proveedor = proveedor, TipoSujeto = "person", Json = $$"""{"nombre":"{{NombrePropietario}}"}""" },
        };
        Campos(r,
            ("person_full_name", "PEPITO PEREZ"), ("person_first_name", "PEPITO"), ("person_last_name", "PEREZ"), ("person_type", "NATURAL"),
            ("owner_full_name", NombrePropietario), ("vehicle_owner_phone", Telefono), ("vehicle_owner_city", Ciudad),
            ("vehicle_owner_address", Direccion), ("lessee_full_name", NombreLocatario), ("lessee_email", Correo), ("company_name", NombrePropietario),
            ("rues_razon_social", NombrePropietario), ("rues_direccion", Direccion), ("rues_representacion_legal", "PEPITO PEREZ"),
            ("runt_nombre_acreedor", Acreedor), ("clave_nueva_de_un_proveedor", NombreHistorico));
        r.Campos.Add(new Campo { Clave = "runt_gravamenes", ValorJson = $$"""[{"acreedor":"{{Acreedor}}","direccion":"{{Direccion}}"}]""" });
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
        r.Vehiculo.LimitacionPropiedad.Add(new LimitacionPropiedadIntempo
        {
            TipoLimitacion = "EMBARGO",
            TipoDocumentoDemandante = "C",
            NoDocumentoDemandante = Demandante,
            EntidadJuridica = Entidad,
        });
        var accidente = new AccidenteIntempo();
        accidente.Campos.Add("conductor", Accidente);
        accidente.Campos.Add("direccion", Direccion);
        r.Vehiculo.AccidentRate.Add(accidente);
        r.Vehiculo.Infracciones.Add(new InfraccionIntempo { NroComparendo = "SIM-0002", Descripcion = DetalleProveedor });
        return r;
    }

    /// <summary>Respuestas reales de los fixtures: los de Intempo y las del simulador de LOCAL/DEV para cada último dígito.</summary>
    private static IEnumerable<IMessage> Fixtures(string rpc)
    {
        if (rpc is RpcConsultas.PorPlaca or RpcConsultas.PorVin)
        {
            var lector = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));
            foreach (var json in FixturesIntempo.Todos().Values)
                yield return lector.Parse<ConsultarVehiculoIntempoResponse>(json);
        }

        var simulador = new SimuladorConsultas(TimeProvider.System);
        foreach (var digito in "0123456789")
        {
            IMessage solicitud = rpc switch
            {
                RpcConsultas.PorPlaca => new ConsultarPorPlacaRequest { Placa = $"TST00{digito}" },
                RpcConsultas.PorVin => new ConsultarPorVinRequest { Vin = $"9ZZPRUEBA0000000{digito}" },
                RpcConsultas.Conductor => new ConsultarConductorRequest { Documento = new DocumentoIdentidad { Tipo = "CC", Numero = $"999990001{digito}" } },
                RpcConsultas.Multas => new ConsultarMultasRequest { TipoPersona = TipoPersona.Natural, Placa = new Placa { Valor = $"TST00{digito}" } },
                RpcConsultas.Rues => new ConsultarRuesRequest { Nit = $"99999000{digito}" },
                RpcConsultas.Avaluos => new ConsultarAvaluosRequest { Placa = new Placa { Valor = $"TST00{digito}" }, Modelo = 2020, Cilindraje = 1600 },
                RpcConsultas.Impuesto => new ConsultarImpuestoVehicularRequest { Placa = new Placa { Valor = $"TST00{digito}" }, DepartamentoCodigo = "05" },
                _ => throw new ArgumentOutOfRangeException(nameof(rpc), rpc, null),
            };
            IMessage? respuesta;
            try
            {
                respuesta = simulador.Responder(rpc, solicitud);
            }
            catch (RpcException)
            {
                respuesta = null;
            }

            if (respuesta is not null)
                yield return respuesta;
        }
    }

    /// <summary>Los campos permitidos del plan como <c>Mensaje.campo</c>.</summary>
    private static HashSet<string> Permitidos(Plan plan) =>
        [.. plan.Mensajes.SelectMany(m => m.Value.Permitidos.Select(c => m.Key + "." + c))];

    /// <summary>Los campos con valor (no por defecto) de <paramref name="mensaje"/> y sus anidados, como <c>Mensaje.campo</c>.</summary>
    private static HashSet<string> Presentes(IMessage mensaje)
    {
        var presentes = new HashSet<string>(StringComparer.Ordinal);
        void Recorrer(IMessage m)
        {
            foreach (var campo in m.Descriptor.Fields.InFieldNumberOrder())
            {
                var valor = campo.Accessor.GetValue(m);
                if (campo.IsRepeated)
                {
                    var lista = ((IEnumerable)valor).Cast<object>().ToList();
                    if (lista.Count == 0)
                        continue;
                    presentes.Add(m.Descriptor.Name + "." + campo.Name);
                    foreach (var hijo in lista.OfType<IMessage>())
                        Recorrer(hijo);
                }
                else if (campo.FieldType == FieldType.Message)
                {
                    if (!campo.Accessor.HasValue(m))
                        continue;
                    presentes.Add(m.Descriptor.Name + "." + campo.Name);
                    Recorrer((IMessage)valor);
                }
                else if (campo.HasPresence ? campo.Accessor.HasValue(m) : !EsPorDefecto(valor))
                {
                    presentes.Add(m.Descriptor.Name + "." + campo.Name);
                }
            }
        }

        Recorrer(mensaje);
        return presentes;
    }

    private static bool EsPorDefecto(object valor) => valor switch
    {
        string s => s.Length == 0,
        bool b => !b,
        int i => i == 0,
        long l => l == 0,
        Enum e => Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture) == 0,
        _ => false,
    };

    /// <summary>Verdadero si cada tramo de <paramref name="ruta"/> está permitido en su mensaje.</summary>
    private static bool Permitida(Plan plan, string ruta)
    {
        var mensaje = plan.Respuesta;
        foreach (var tramo in ruta.Split('.'))
        {
            var nombre = tramo.Split('[')[0];
            if (!plan.Mensajes.TryGetValue(mensaje.Name, out var regla) || !regla.Permitidos.Contains(nombre))
                return false;
            if (tramo.Contains('[') && !plan.Claves.Contains(tramo[(nombre.Length + 1)..^1]))
                return false;
            var campo = mensaje.FindFieldByName(nombre);
            if (campo.FieldType == FieldType.Message)
                mensaje = campo.MessageType;
        }

        return true;
    }

    /// <summary>Los valores en <paramref name="ruta"/> (aplanando los repetidos, en orden); <c>campos[clave]</c> elige por clave.</summary>
    private static List<object> Valores(IMessage raiz, string ruta)
    {
        IEnumerable<IMessage> actuales = [raiz];
        var tramos = ruta.Split('.');
        var valores = new List<object>();
        for (var i = 0; i < tramos.Length; i++)
        {
            var nombre = tramos[i].Split('[')[0];
            var clave = tramos[i].Contains('[') ? tramos[i][(nombre.Length + 1)..^1] : null;
            var siguientes = new List<IMessage>();
            foreach (var m in actuales)
            {
                var campo = m.Descriptor.FindFieldByName(nombre);
                IEnumerable<object> elementos = campo.IsRepeated
                    ? ((IEnumerable)campo.Accessor.GetValue(m)).Cast<object>()
                    : campo.FieldType == FieldType.Message
                        ? campo.Accessor.HasValue(m) ? [campo.Accessor.GetValue(m)] : []
                        : campo.HasPresence && !campo.Accessor.HasValue(m) ? [] : [campo.Accessor.GetValue(m)];
                if (clave is not null)
                    elementos = elementos.OfType<Campo>().Where(c => c.Clave == clave);
                if (i == tramos.Length - 1)
                    valores.AddRange(elementos.Where(e => !EsPorDefecto(e)));
                else
                    siguientes.AddRange(elementos.OfType<IMessage>());
            }

            actuales = siguientes;
        }

        return valores;
    }

    /// <summary>
    /// Llena TODOS los campos de un mensaje y sus anidados (un elemento por repetido, una entrada por mapa) con valores sembrados
    /// únicos; en <see cref="ResultadoConsulta"/> los <c>campos</c> son las claves permitidas del rpc más <see cref="ClavesProhibidas"/>.
    /// </summary>
    private sealed class Semillas
    {
        private int _siguiente;

        /// <summary>Cada texto sembrado y el campo (<c>Mensaje.campo</c>) donde se sembró.</summary>
        public List<(string Valor, string Campo)> Sembradas { get; } = [];

        public IMessage Llenar(MessageDescriptor descriptor, string[] clavesPermitidas)
        {
            var mensaje = Nuevo(descriptor, 0);
            if (descriptor.FindFieldByName("resultado")?.Accessor.GetValue(mensaje) is ResultadoConsulta resultado)
            {
                resultado.Campos.Clear();
                foreach (var clave in clavesPermitidas.Concat(ClavesProhibidas))
                {
                    var campo = new Campo { Clave = clave, ValorTexto = Texto("Campo.valor_texto"), ValorJson = $$"""{"nombre":"{{Texto("Campo.valor_json")}}"}""" };
                    if (!clavesPermitidas.Contains(clave))
                        Sembradas.Add((campo.ValorTexto, "Campo.valor_texto[" + clave + "]"));
                    resultado.Campos.Add(campo);
                }
            }

            return mensaje;
        }

        private string Texto(string campo)
        {
            var valor = $"SEM{++_siguiente:D5}X";
            Sembradas.Add((valor, campo));
            return valor;
        }

        private IMessage Nuevo(MessageDescriptor descriptor, int profundidad)
        {
            var mensaje = (IMessage)Activator.CreateInstance(descriptor.ClrType)!;
            if (profundidad > 8)
                return mensaje;
            foreach (var campo in descriptor.Fields.InFieldNumberOrder())
            {
                var nombre = descriptor.Name + "." + campo.Name;
                if (campo.IsMap)
                {
                    var mapa = (IDictionary)campo.Accessor.GetValue(mensaje);
                    mapa.Add(Texto(nombre), Texto(nombre));
                }
                else if (campo.IsRepeated)
                {
                    ((IList)campo.Accessor.GetValue(mensaje)).Add(Valor(campo, nombre, profundidad));
                }
                else
                {
                    campo.Accessor.SetValue(mensaje, Valor(campo, nombre, profundidad));
                }
            }

            return mensaje;
        }

        private object Valor(FieldDescriptor campo, string nombre, int profundidad) => campo.FieldType switch
        {
            FieldType.String => Texto(nombre),
            FieldType.Bool => true,
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => 7,
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => 7L,
            FieldType.UInt32 or FieldType.Fixed32 => 7u,
            FieldType.UInt64 or FieldType.Fixed64 => 7ul,
            FieldType.Double => 7d,
            FieldType.Float => 7f,
            FieldType.Enum => Enum.ToObject(campo.EnumType.ClrType, campo.EnumType.Values.First(v => v.Number != 0).Number),
            FieldType.Message => Nuevo(campo.MessageType, profundidad + 1),
            _ => throw new NotSupportedException($"{nombre}: tipo {campo.FieldType} sin semilla"),
        };
    }
}
