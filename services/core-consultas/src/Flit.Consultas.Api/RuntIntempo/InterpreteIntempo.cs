using System.Globalization;
using System.Text.Json;
using Flit.Consultas.Api.Comparendos;
using Flit.Consultas.Grpc.V1;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// Intérprete del crudo plano de Intempo (§4 de la especificación de migración) al <see cref="VehiculoIntempo"/> tipado
/// con las 22 claves del normalizado (§5). HU #13874 (F10-H2). Puerto de <c>IntempoApiProxy.getResponseVehicleSpanish</c>
/// (BackCrudTransfer_master <c>src/infrastructure/proxies/intempoApiProxy.ts</c>, líneas 161-453), con el mapa de
/// documentos (<see cref="MapaTipoDocumentoIntempo"/>, §6.1) y el partido de nombre (<see cref="PartidoNombre"/>, §6.2).
/// <para>
/// Regla pura y sin estado: nunca lanza por una clave que falta o con otro tipo (queda <c>""</c>, <c>0</c>, arreglo u
/// objeto vacío, según §5) y no registra nada; el resultado trae datos personales (Ley 1581). Las claves se leen con
/// mayúsculas exactas, como el original (<c>AccidenteVehiculo</c> y <c>DatosBlindaje</c> en PascalCase).
/// </para>
/// <para>
/// Semántica de JavaScript conservada: <c>a || b</c> salta los valores «falsy» (ausente, <c>null</c>, <c>""</c>,
/// <c>0</c>, <c>false</c>); <c>a ?? b</c> solo salta ausente y <c>null</c>. Los números y booleanos que llegan a un campo
/// de texto se copian como texto.
/// </para>
/// </summary>
internal static class InterpreteIntempo
{
    /// <summary>Interpreta el objeto raíz de la respuesta de Intempo. Una raíz que no es objeto se trata como <c>{}</c>.</summary>
    public static VehiculoIntempo Interpretar(JsonElement raiz)
    {
        var d = raiz.ValueKind == JsonValueKind.Object ? raiz : default;
        var tecnico = Obj(d, "datosTecnicos");
        var propietarios = Arr(d, "propietariosActuales").ToList();
        var primero = propietarios.Count > 0 ? propietarios[0] : default;
        var locatariosPrimero = Arr(primero, "locatarios").ToList();

        // Partido del primer propietario y del primer locatario de ese propietario; se copia a todos (§5.5).
        var partidoOwner = Falsy(primero, "nombreCompleto") is { } nombreOwner ? PartidoNombre.Partir(nombreOwner) : Vacio;
        var partidoLessee = locatariosPrimero.Count > 0
            ? PartidoNombre.Partir(O(locatariosPrimero[0], "nombreRazonSocial", "nombreCompleto"))
            : Vacio;

        var v = new VehiculoIntempo
        {
            Plate = T(d, "noPlaca"),
            Vin = T(d, "noVin"),
            DocumentType = MapaTipoDocumentoIntempo.Mapear(Txt(d, "tipoDocumento")),
            // El original pasa también el número por el mapa: no coincide y se copia tal cual (§5.1).
            DocumentNumber = MapaTipoDocumentoIntempo.Mapear(Txt(d, "noDocumento")),
            InformacionGeneral = General(d, tecnico),
            DatosTecnicos = new DatosTecnicosIntempo
            {
                CapacidadCarga = T(tecnico, "capacidadCarga"),
                NoEjes = T(tecnico, "noEjes"),
                SerialVersionUid = O(d, "serialVersionUID"),
                PesoBrutoVehicular = O(tecnico, "pesoBrutoVehicular", "peso"),
                // Raíz capacidadPasajerosSentados, no el objeto técnico.
                PasajerosSentados = O(d, "capacidadPasajerosSentados"),
                Alto = O(tecnico, "alto"),
                Ancho = O(tecnico, "ancho"),
                Largo = O(tecnico, "largo"),
                Rodaje = O(tecnico, "rodaje"),
            },
            InformacionBlindaje = Blindaje(Obj(d, "DatosBlindaje")),
        };

        foreach (var s in Arr(d, "soatNacionales"))
            v.Soat.Add(new SoatIntempo
            {
                // nitEntidad del modelo de dominio no se copia.
                NoPoliza = T(s, "noPoliza"),
                FechaExpedicion = T(s, "fechaExpedicion"),
                FechaVigencia = T(s, "fechaVigencia"),
                FechaVencimiento = T(s, "fechaVencimiento"),
                EntidadExpideSoat = T(s, "entidadExpideSoat"),
                Estado = T(s, "estado"),
            });

        foreach (var r in Arr(d, "revisiones"))
            v.TecnoMecanica.Add(new TecnoMecanicaIntempo
            {
                SerialVersionUid = T(r, "nroRTM"),
                NroCertificado = T(r, "nroRTM"),
                TipoRevision = T(r, "tipoRevision"),
                FechaExpedicion = T(r, "fechaExpedicion"),
                FechaVigente = T(r, "fechaVigente"),
                CdaExpide = T(r, "cdaExpide"),
                Vigente = T(r, "vigente"),
                NumeroPlaca = T(d, "noPlaca"),
                InformacionConsistente = string.Empty,
                Url = string.Empty,
            });

        foreach (var g in Arr(d, "gravamenes"))
            v.GarantiasFavorDe.Add(new GarantiaFavorDeIntempo
            {
                TipoDocumentoAcreedor = T(g, "tipoDocumentoAcreedor"),
                NumeroDocumentoAcreedor = T(g, "numeroDocumentoAcreedor"),
                Acreedor = Txt(g, "acreedor") ?? T(g, "nombreAcreedor"),
                FechaInscripcion = T(g, "fechaInscripcion"),
                Confecamaras = T(g, "confecamaras"),
            });

        foreach (var p in Arr(d, "prendasGM"))
            v.GarantiasMobiliarias.Add(new GarantiaMobiliariaIntempo
            {
                IdPrenda = T(p, "idPrenda"),
                IdVehiculoPrenda = T(p, "idVehiculoPrenda"),
                FechaRegistro = T(p, "fechaRegistro"),
                TipoDocumentoEntidad = T(p, "tipoDocumentoEntidad"),
                NumeroDocumentoEntidad = T(p, "numeroDocumentoEntidad"),
                Entidad = T(p, "entidad"),
                Estado = T(p, "estado"),
            });

        foreach (var l in Arr(d, "limitacionesPropiedad"))
        {
            var limitacion = new LimitacionPropiedadIntempo
            {
                TipoLimitacion = T(l, "tipoLimitacion"),
                NoDocumento = T(l, "noDocumento"),
                EntidadJuridica = T(l, "entidadJuridica"),
                TipoDocumentoDemandante = O(l, "tipoDocumentoDemandante"),
                NoDocumentoDemandante = O(l, "noDocumentoDemandante"),
                FechaExpedicion = T(l, "fechaExpedicion"),
                FechaRadicacion = T(l, "fechaRadicacion"),
                Departamento = O(l, "departamento"),
                Municipio = O(l, "municipio"),
            };
            if (Txt(l, "serialVersionUID") is { } uid)
                limitacion.SerialVersionUid = uid;
            v.LimitacionPropiedad.Add(limitacion);
        }

        // Arreglos casi vacíos (§5.4): el origen no llega o llega como objeto suelto.
        v.NormalizacionSaneamiento.Add(new NormalizacionSaneamientoIntempo());
        v.PolizasResponsabilidadCivil.Add(new PolizaResponsabilidadCivilIntempo
        {
            SerialVersionUid = O(Obj(d, "polizasResponsabilidadCivil"), "serialVersionUID"),
        });
        // solicitudes siempre [] (§5.4).
        var tarjeta = Obj(d, "informacionTarjetaOperacion");
        v.TarjetaOperacion.Add(new TarjetaOperacionIntempo
        {
            SerialVersionUid = O(tarjeta, "serialVersionUID"),
            ModalidadServicio = O(tarjeta, "modalidadServicio"),
        });

        foreach (var p in propietarios)
            v.PropietariosActuales.Add(Propietario(p));

        foreach (var h in Arr(d, "historicoPropietarios"))
        {
            var dir = Obj(h, "direccion");
            var direccion = Direccion(dir);
            direccion.Email = O(dir, "email");
            direccion.FechaActualizacion = O(dir, "fechaActualizacion");
            v.HistoricoPropietarios.Add(new HistoricoPropietarioIntempo
            {
                NombreCompletoPropietario = T(h, "nombreCompletoPropietario"),
                TipoDocumento = T(h, "tipoDocumento"),
                NumeroDocumento = T(h, "numeroDocumento"),
                FechaInicioPropiedad = T(h, "fechaInicioPropiedad"),
                Direccion = direccion,
            });
        }

        foreach (var p in propietarios)
            v.Owner.Add(Persona(MapaTipoDocumentoIntempo.Mapear(Txt(p, "tipoDocumento")), T(p, "noDocumento"), T(p, "nombreCompleto"), partidoOwner));

        // lessee sale solo de los locatarios del primer propietario (§5.5).
        foreach (var l in locatariosPrimero)
        {
            var lessee = Persona(MapaTipoDocumentoIntempo.Mapear(Txt(l, "tdoc")), T(l, "ndoc"), O(l, "nombreRazonSocial", "nombreCompleto"), partidoLessee);
            if (Entero(l, "tipo") is { } tipo)
                lessee.LesseeType = (int)tipo;
            v.Lessee.Add(lessee);
        }

        v.AccidentRate.Add(Accidente(Obj(d, "AccidenteVehiculo")));

        foreach (var i in Arr(d, "infracciones"))
        {
            var infraccion = new InfraccionIntempo
            {
                NroComparendo = T(i, "nroComparendo"),
                Fecha = T(i, "fecha"),
                Infraccion = T(i, "infraccion"),
                Descripcion = T(i, "descripcion"),
                Estado = T(i, "estado"),
                ValorInfraccion = Pesos(i, "valorInfraccion"),
                Placa = T(i, "placa"),
                ReportaFuga = T(i, "reportaFuga"),
                Accidente = T(i, "accidente"),
            };
            if (Txt(i, "secretariaTransito") is { } secretaria)
                infraccion.SecretariaTransito = secretaria;
            v.Infracciones.Add(infraccion);
        }

        return v;
    }

    private static readonly NombrePartido Vacio = new("", "", "", "");

    private static InformacionGeneralIntempo General(JsonElement d, JsonElement tecnico)
    {
        var g = new InformacionGeneralIntempo
        {
            Cilidraje = T(d, "cilindraje"),
            ClaseVehiculo = T(d, "claseVehiculo"),
            ClasicoAntiguo = O(d, "clasicoAntiguo"),
            Clasificacion = T(d, "clasificacion"),
            Color = T(d, "color"),
            EsRegrabadoChasis = T(d, "esRegrabadoChasis"),
            EsRegrabadoMotor = T(d, "esRegrabadoMotor"),
            EsRegrabadoSerie = T(d, "esRegrabadoSerie"),
            EsRegrabadoVin = T(d, "esRegrabadoVin"),
            EstadoDelVehiculo = O(d, "estadoDelVehiculo"),
            FechaMatricula = T(d, "fechaMatricula"),
            Linea = T(d, "linea"),
            Marca = T(d, "marca"),
            Modelo = T(d, "modelo"),
            MostrarSolicitudes = O(d, "mostrarSolicitudes"),
            NoChasis = T(d, "noChasis"),
            NoLicenciaTransito = T(d, "noLicenciaTransito"),
            NoPlaca = T(d, "noPlaca"),
            NoVin = T(d, "noVin"),
            OrganismoTransito = T(d, "organismoTransito"),
            Prendas = T(d, "prendas"),
            Repotenciado = T(d, "repotenciado"),
            SeguridadEstado = O(d, "seguridadEstado"),
            TarjetaServicio = O(d, "tarjetaServicio"),
            TieneGravamenes = T(d, "tieneGravamenes"),
            TieneLtImportacion = O(d, "tieneLTImportacion", "tipoImportacion"),
            TipoCarroceria = T(d, "tipoCarroceria"),
            TipoCombustible = T(d, "tipoCombustible"),
            TipoServicio = O(d, "tipoServicio", "tipoServicioNombre"),
            VerValidaDian = O(d, "verValidaDIAN"),
            ValidacionDian = O(d, "validacionDIAN"),
        };
        // Optional: undefined en el original no se serializa (§5).
        if (Txt(d, "noMotor") is { } motor) g.NoMotor = motor;
        if (Txt(tecnico, "puertas") is { } puertas) g.Puertas = puertas;
        if (Txt(d, "serialVersionUID") is { } uid) g.SerialVersionUid = uid;
        return g;
    }

    private static InformacionBlindajeIntempo Blindaje(JsonElement b) => new()
    {
        SerialVersionUid = O(b, "serialVersionUID"),
        // El proxy escribe blindado; el historial trae también esBlindado (7 muestras reales): un solo campo (§5.4).
        Blindado = O(b, "blindado", "esBlindado"),
        NivelBlindaje = O(b, "nivelBlindaje"),
        NivelBlindajeNumero = O(b, "nivelBlindajeNumero"),
        FechaBlindaje = O(b, "fechaBlindaje"),
        NumeroResolucion = O(b, "numeroResolucion"),
        TipoBlindajeNombre = O(b, "tipoBlindajeNombre"),
        FechaExpedicionCertificado = O(b, "fechaExpedicionCertificado"),
        FechaExpedicionCertificadoFormatoWs = O(b, "fechaExpedicionCertificadoFormatoWS"),
        Autorizacion = O(b, "autorizacion"),
    };

    /// <summary>propietariosActuales conserva la forma cruda (§5.5): el mapa §6.1 no se aplica aquí.</summary>
    private static PropietarioActualIntempo Propietario(JsonElement p)
    {
        var r = new PropietarioActualIntempo
        {
            Id = Entero(p, "id") ?? 0,
            IdTipoDocumento = T(p, "idTipoDocumento"),
            TipoDocumento = T(p, "tipoDocumento"),
            NoDocumento = T(p, "noDocumento"),
            NombreCompleto = T(p, "nombreCompleto"),
            TipoPropiedad = (int)(Entero(p, "tipoPropiedad") ?? 0),
            DetallePropiedad = T(p, "detallePropiedad"),
        };
        foreach (var dir in Arr(p, "direcciones"))
        {
            var direccion = Direccion(dir);
            direccion.Email = O(dir, "email");
            direccion.FechaActualizacion = O(dir, "fechaActualizacion");
            r.Direcciones.Add(direccion);
        }

        foreach (var l in Arr(p, "locatarios"))
        {
            var locatario = new LocatarioIntempo
            {
                Tipo = (int)(Entero(l, "tipo") ?? 0),
                Tdoc = O(l, "tdoc"),
                Ndoc = O(l, "ndoc"),
                NombreRazonSocial = O(l, "nombreRazonSocial"),
            };
            // Las direcciones del locatario no traen email ni fechaActualizacion.
            foreach (var dir in Arr(l, "direcciones"))
                locatario.Direcciones.Add(Direccion(dir));
            r.Locatarios.Add(locatario);
        }

        return r;
    }

    /// <summary>idDepartamento e idCiudad llegan número o cadena vacía: salen como texto.</summary>
    private static DireccionIntempo Direccion(JsonElement d) => new()
    {
        Id = Entero(d, "id") ?? 0,
        Direccion = O(d, "direccion"),
        IdDepartamento = O(d, "idDepartamento"),
        Departamento = O(d, "departamento"),
        IdCiudad = O(d, "idCiudad"),
        Ciudad = O(d, "ciudad"),
        Telefono = O(d, "telefono"),
    };

    private static PersonaIntempo Persona(string tipo, string numero, string nombre, NombrePartido partido) => new()
    {
        DocumentType = tipo,
        DocumentNumber = numero,
        Name = nombre,
        FirstName = partido.FirstName,
        SecondName = partido.SecondName,
        FirstLastName = partido.FirstLastName,
        SecondLastName = partido.SecondLastName,
    };

    /// <summary>accidentRate = [AccidenteVehiculo] o [{}]; el origen no tiene forma fija: claves y valores como texto.</summary>
    private static AccidenteIntempo Accidente(JsonElement a)
    {
        var r = new AccidenteIntempo();
        if (a.ValueKind != JsonValueKind.Object)
            return r;
        foreach (var p in a.EnumerateObject())
            r.Campos[p.Name] = Texto(p.Value) ?? (p.Value.ValueKind == JsonValueKind.Null ? string.Empty : p.Value.GetRawText());
        return r;
    }

    /// <summary>valorInfraccion llega número: texto decimal en pesos, igual que ComparendoDeFuente («604100.00»).</summary>
    private static string Pesos(JsonElement e, string clave)
    {
        if (!Prop(e, clave, out var v))
            return string.Empty;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n))
            return Fuente.Pesos(n);
        var texto = Texto(v) ?? string.Empty;
        return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? Fuente.Pesos(m) : texto;
    }

    // ---- Lectura segura del JSON ----

    private static bool Prop(JsonElement e, string clave, out JsonElement v)
    {
        v = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(clave, out v);
    }

    private static JsonElement Obj(JsonElement e, string clave) =>
        Prop(e, clave, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    /// <summary>Elementos del arreglo; uno que no es objeto se lee como <c>{}</c> (sus campos quedan vacíos).</summary>
    private static IEnumerable<JsonElement> Arr(JsonElement e, string clave) =>
        Prop(e, clave, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object ? x : default)
            : [];

    /// <summary>Escalar como texto; <c>null</c> si falta, es nulo, objeto o arreglo.</summary>
    private static string? Texto(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    /// <summary>Valor tal cual (<c>data?.x</c>): <c>null</c> si falta.</summary>
    private static string? Txt(JsonElement e, string clave) => Prop(e, clave, out var v) ? Texto(v) : null;

    /// <summary><c>data?.x</c> en un campo de texto: ausente queda <c>""</c>.</summary>
    private static string T(JsonElement e, string clave) => Txt(e, clave) ?? string.Empty;

    /// <summary>Primer valor no «falsy» de JavaScript; <c>null</c> si ninguno.</summary>
    private static string? Falsy(JsonElement e, params string[] claves)
    {
        foreach (var c in claves)
        {
            if (!Prop(e, c, out var v))
                continue;
            var falsy = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString()!.Length == 0,
                JsonValueKind.Number => v.TryGetDouble(out var n) && n == 0,
                JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => true,
                _ => false,
            };
            if (!falsy && Texto(v) is { } t)
                return t;
        }

        return null;
    }

    /// <summary><c>a || b || ''</c>.</summary>
    private static string O(JsonElement e, params string[] claves) => Falsy(e, claves) ?? string.Empty;

    /// <summary>Entero de un número o de un texto numérico; <c>null</c> si no lo es.</summary>
    private static long? Entero(JsonElement e, string clave)
    {
        if (!Prop(e, clave, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt64(out var n) ? n : v.TryGetDouble(out var x) && x is >= long.MinValue and <= long.MaxValue ? (long)x : null;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? s
            : null;
    }
}
