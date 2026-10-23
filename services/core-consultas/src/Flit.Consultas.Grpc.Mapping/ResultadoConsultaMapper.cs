using System.Globalization;
using Flit.Consultas.Grpc.V1;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Google.Protobuf.WellKnownTypes;

namespace Flit.Consultas.Grpc.Mapping;

/// <summary>
/// <see cref="ConsultationResult"/> ⇄ <c>flit.consultas.v1.ResultadoConsulta</c> (HU #13343/#13346). Traducción 1:1 sin
/// reglas: el semáforo, los chequeos, los campos y las certificaciones ya vienen normalizados por los mappers del
/// módulo. Los textos que en C# pueden ser null viajan como <c>optional</c>: un null llega null y un "" llega "", porque
/// Trámites no hidrata igual los dos.
/// </summary>
public static class ResultadoConsultaMapper
{
    private const string FormatoFecha = "yyyy-MM-dd";

    // ── Módulo → proto (core-consultas) ─────────────────────────────────────────────────────────────────────────────

    public static ResultadoConsulta ToProto(ConsultationResult r, bool incluirCruda)
    {
        ArgumentNullException.ThrowIfNull(r);
        var resultado = new ResultadoConsulta
        {
            Proveedor = r.Provider ?? string.Empty,
            Semaforo = Semaforo(r.Overall),
            DesdeCache = r.FromCache,
        };
        if (r.QueriedAt is { } consultado)
            resultado.ConsultadoEn = Timestamp.FromDateTimeOffset(consultado);

        foreach (var check in r.Checks)
            resultado.Chequeos.Add(ToProto(check));
        foreach (var field in r.HydratedFields)
        {
            var campo = new Campo { Clave = field.FieldKey };
            if (field.ValueText is not null)
                campo.ValorTexto = field.ValueText;
            if (field.ValueJson is not null)
                campo.ValorJson = field.ValueJson;
            resultado.Campos.Add(campo);
        }

        if (r.Certifications is { } bundle)
            resultado.Certificaciones = ToProto(bundle);
        if (incluirCruda && r.RawPayload is { } raw)
        {
            resultado.RespuestaCruda = new RespuestaCruda { Proveedor = raw.ProviderKey, TipoSujeto = raw.SubjectKind, Json = raw.PayloadJson };
            if (raw.SubjectKey is not null)
                resultado.RespuestaCruda.ClaveSujeto = raw.SubjectKey;
        }

        return resultado;
    }

    public static ConsultarAvaluosResponse ToProto(SuggestedCommercialValue v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var response = new ConsultarAvaluosResponse { ValorSugerido = v.Sugerido ?? 0, FuentePrincipal = v.FuentePrincipal ?? string.Empty };
        foreach (var s in v.Sources)
        {
            var avaluo = new Avaluo
            {
                Fuente = s.Source,
                Estado = s.Status switch
                {
                    "ok" => EstadoAvaluo.Ok,
                    "error" => EstadoAvaluo.Error,
                    _ => EstadoAvaluo.SinDatos,
                },
                Valor = s.Value ?? 0,
                Muestras = s.Muestras ?? 0,
            };
            if (s.Message is not null)
                avaluo.Mensaje = s.Message;
            response.Avaluos.Add(avaluo);
        }

        return response;
    }

    /// <summary>proto → módulo (core-api, HU #13348): el inverso de <see cref="ToProto(SuggestedCommercialValue)"/>.</summary>
    public static SuggestedCommercialValue FromProto(ConsultarAvaluosResponse r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var fuentes = r.Avaluos.Select(a => new AvaluoResult(
                a.Fuente,
                a.Estado switch
                {
                    EstadoAvaluo.Ok => "ok",
                    EstadoAvaluo.Error => "error",
                    _ => "no_data",
                },
                a.Estado == EstadoAvaluo.Ok ? a.Valor : null,
                "COP",
                a.HasMensaje ? a.Mensaje : null,
                a.Muestras > 0 ? a.Muestras : null))
            .ToList();
        return new SuggestedCommercialValue(
            r.ValorSugerido > 0 ? r.ValorSugerido : null,
            string.IsNullOrEmpty(r.FuentePrincipal) ? null : r.FuentePrincipal,
            fuentes);
    }

    private static Chequeo ToProto(ConsultationCheck c)
    {
        var chequeo = new Chequeo { Clave = c.Key, Etiqueta = c.Label, Estado = Estado(c.Status), Fuente = c.Source ?? string.Empty };
        if (c.Message is not null)
            chequeo.Mensaje = c.Message;
        foreach (var f in c.Details ?? [])
        {
            var comparendo = new Comparendo();
            if (f.Numero is not null) comparendo.Numero = f.Numero;
            if (f.Fecha is not null) comparendo.Fecha = f.Fecha;
            if (f.Valor is { } valor) comparendo.Valor = valor.ToString(CultureInfo.InvariantCulture);
            if (f.Organismo is not null) comparendo.Organismo = f.Organismo;
            if (f.Estado is not null) comparendo.Estado = f.Estado;
            if (f.Infraccion is not null) comparendo.Infraccion = f.Infraccion;
            chequeo.Comparendos.Add(comparendo);
        }

        foreach (var d in c.Datos ?? [])
            chequeo.Datos.Add(new Dato { Etiqueta = d.Etiqueta, Valor = d.Valor });
        return chequeo;
    }

    private static Certificaciones ToProto(CertificationBundle b)
    {
        var c = new Certificaciones { Vehiculo = new HechosMatriculaVehiculo { FechaMatricula = Fecha(b.Vehicle.FechaMatricula) } };
        foreach (var s in b.SoatHistory)
        {
            c.Soat.Add(new CertificacionSoat
            {
                NumeroPoliza = Valor(s.PolicyNumber.Value, s.PolicyNumber.Raw),
                Aseguradora = Valor(s.Insurer.Value, s.Insurer.Raw),
                Expedicion = Fecha(s.IssuedOn),
                VigenteDesde = Fecha(s.ValidFrom),
                VigenteHasta = Fecha(s.ValidUntil),
                Estado = Estado(s.Status),
            });
        }

        foreach (var r in b.RtmHistory)
        {
            var rtm = new CertificacionRtm
            {
                NumeroCertificado = Valor(r.CertificateNumber.Value, r.CertificateNumber.Raw),
                Cda = Valor(r.Cda.Value, r.Cda.Raw),
                Expedicion = Fecha(r.IssuedOn),
                VigenteDesde = Fecha(r.ValidFrom),
                VigenteHasta = Fecha(r.ValidUntil),
                Estado = Estado(r.Status),
            };
            if (r.InspectionType is not null)
                rtm.TipoRevision = Valor(r.InspectionType, null);
            c.Rtm.Add(rtm);
        }

        foreach (var m in b.MerchantRegistrations)
        {
            var matricula = new MatriculaMercantil
            {
                Nit = Valor(m.Nit, null),
                RazonSocial = Valor(m.BusinessName.Value, m.BusinessName.Raw),
                NumeroMatricula = Valor(m.RegistrationNumber.Value, m.RegistrationNumber.Raw),
                Estado = Estado(m.Status),
                MatriculadaEn = Fecha(m.RegisteredOn),
                RenovadaEn = Fecha(m.RenewedOn),
                CamaraComercio = Valor(m.ChamberOfCommerce.Value, m.ChamberOfCommerce.Raw),
                Categoria = Valor(m.Category.Value, m.Category.Raw),
                Direccion = Valor(m.Address.Value, m.Address.Raw),
                Ciudad = Valor(m.City.Value, m.City.Raw),
            };
            foreach (var rep in m.LegalRepresentatives)
            {
                matricula.RepresentantesLegales.Add(new RepresentanteLegal
                {
                    Nombre = Valor(rep.Name, null),
                    TipoDocumento = Valor(rep.DocumentType, null),
                    NumeroDocumento = Valor(rep.DocumentNumber, null),
                    Cargo = Valor(rep.Role, null),
                    Facultades = Valor(rep.Powers, null),
                });
            }

            c.MatriculasMercantiles.Add(matricula);
        }

        return c;
    }

    private static ValorCertificado Valor(string? valor, string? crudo)
    {
        var v = new ValorCertificado();
        if (valor is not null) v.Valor = valor;
        if (crudo is not null) v.Crudo = crudo;
        return v;
    }

    private static FechaCertificada Fecha(CertifiedDate d)
    {
        var f = new FechaCertificada();
        if (d.Value is { } valor) f.Valor = valor.ToString(FormatoFecha, CultureInfo.InvariantCulture);
        if (d.Raw is not null) f.Crudo = d.Raw;
        return f;
    }

    private static EstadoVigencia Estado(CertifiedStatus s)
    {
        var e = new EstadoVigencia
        {
            Valor = s.Value switch
            {
                VigencyStatus.Vigente => Vigencia.Vigente,
                VigencyStatus.Vencido => Vigencia.Vencido,
                VigencyStatus.NoAplica => Vigencia.NoAplica,
                _ => Vigencia.Unspecified,
            },
        };
        if (s.Raw is not null) e.Crudo = s.Raw;
        return e;
    }

    private static EstadoChequeo Estado(string? status) => status?.ToUpperInvariant() switch
    {
        "OK" => EstadoChequeo.Ok,
        "WARN" => EstadoChequeo.Warn,
        "FAIL" => EstadoChequeo.Fail,
        "ERROR" => EstadoChequeo.Error,
        _ => EstadoChequeo.Unknown,
    };

    private static Semaforo Semaforo(string? overall) => overall?.ToUpperInvariant() switch
    {
        "GREEN" or "VERDE" => Grpc.V1.Semaforo.Verde,
        "YELLOW" or "AMARILLO" => Grpc.V1.Semaforo.Amarillo,
        "RED" or "ROJO" => Grpc.V1.Semaforo.Rojo,
        _ => Grpc.V1.Semaforo.Unspecified,
    };

    // ── proto → módulo (core-api) ───────────────────────────────────────────────────────────────────────────────────

    public static ConsultationResult FromProto(ResultadoConsulta r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new ConsultationResult(
            r.Proveedor,
            r.Semaforo switch
            {
                Grpc.V1.Semaforo.Verde => "green",
                Grpc.V1.Semaforo.Amarillo => "yellow",
                Grpc.V1.Semaforo.Rojo => "red",
                _ => string.Empty,
            },
            [.. r.Chequeos.Select(FromProto)],
            [.. r.Campos.Select(c => new HydratedField(c.Clave, c.HasValorTexto ? c.ValorTexto : null, c.HasValorJson ? c.ValorJson : null))],
            r.DesdeCache,
            r.ConsultadoEn?.ToDateTimeOffset(),
            r.Certificaciones is { } cert ? FromProto(cert) : null,
            r.RespuestaCruda is { } raw
                ? new RawProviderPayload(raw.Proveedor, raw.TipoSujeto, raw.HasClaveSujeto ? raw.ClaveSujeto : null, raw.Json,
                    r.ConsultadoEn?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow)
                : null);
    }

    private static ConsultationCheck FromProto(Chequeo c) => new(
        c.Clave,
        c.Etiqueta,
        c.Estado switch
        {
            EstadoChequeo.Ok => "ok",
            EstadoChequeo.Warn => "warn",
            EstadoChequeo.Fail => "fail",
            EstadoChequeo.Error => "error",
            _ => "unknown",
        },
        c.Fuente,
        c.HasMensaje ? c.Mensaje : null,
        c.Comparendos.Count == 0
            ? null
            : [.. c.Comparendos.Select(f => new FineDetail(
                f.HasNumero ? f.Numero : null,
                f.HasFecha ? f.Fecha : null,
                f.HasValor && decimal.TryParse(f.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) ? valor : null,
                f.HasOrganismo ? f.Organismo : null,
                f.HasEstado ? f.Estado : null,
                f.HasInfraccion ? f.Infraccion : null))],
        c.Datos.Count == 0 ? null : [.. c.Datos.Select(d => new CheckDato(d.Etiqueta, d.Valor))]);

    private static CertificationBundle FromProto(Certificaciones c) => new(
        [.. c.Soat.Select(s => new SoatCertification(
            Numero(s.NumeroPoliza), Nombre(s.Aseguradora), Fecha(s.Expedicion), Fecha(s.VigenteDesde), Fecha(s.VigenteHasta), Estado(s.Estado)))],
        [.. c.Rtm.Select(r => new RtmCertification(
            Numero(r.NumeroCertificado), Nombre(r.Cda), Fecha(r.Expedicion), Fecha(r.VigenteDesde), Fecha(r.VigenteHasta), Estado(r.Estado),
            r.TipoRevision is { HasValor: true } tipo ? tipo.Valor : null))],
        [.. c.MatriculasMercantiles.Select(m => new MerchantRegistration(
            m.Nit?.Valor ?? string.Empty,
            Nombre(m.RazonSocial), Numero(m.NumeroMatricula), Estado(m.Estado), Fecha(m.MatriculadaEn), Fecha(m.RenovadaEn),
            Nombre(m.CamaraComercio), Nombre(m.Categoria), Nombre(m.Direccion), Nombre(m.Ciudad),
            [.. m.RepresentantesLegales.Select(rep => new LegalRepresentative(
                Texto(rep.Nombre), Texto(rep.TipoDocumento), Texto(rep.NumeroDocumento), Texto(rep.Cargo), Texto(rep.Facultades)))]))],
        new VehicleRegistrationFacts(Fecha(c.Vehiculo)));

    private static string? Texto(ValorCertificado? v) => v is { HasValor: true } ? v.Valor : null;

    private static CertifiedNumber Numero(ValorCertificado? v) =>
        v is null ? CertifiedNumber.Empty : new(v.HasValor ? v.Valor : null, v.HasCrudo ? v.Crudo : null);

    private static CertifiedName Nombre(ValorCertificado? v) =>
        v is null ? CertifiedName.Empty : new(v.HasValor ? v.Valor : null, v.HasCrudo ? v.Crudo : null);

    private static CertifiedDate Fecha(HechosMatriculaVehiculo? h) => h?.FechaMatricula is { } f ? Fecha(f) : CertifiedDate.Empty;

    private static CertifiedDate Fecha(FechaCertificada? f) =>
        f is null
            ? CertifiedDate.Empty
            : new(f.HasValor && DateOnly.TryParseExact(f.Valor, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                f.HasCrudo ? f.Crudo : null);

    private static CertifiedStatus Estado(EstadoVigencia? e) =>
        e is null
            ? CertifiedStatus.Empty
            : new(e.Valor switch
            {
                Vigencia.Vigente => VigencyStatus.Vigente,
                Vigencia.Vencido => VigencyStatus.Vencido,
                Vigencia.NoAplica => VigencyStatus.NoAplica,
                _ => VigencyStatus.Unknown,
            }, e.HasCrudo ? e.Crudo : null);
}
