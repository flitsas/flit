using System.Globalization;
using Flit.Consultas.Grpc.V1;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Google.Protobuf.WellKnownTypes;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// <see cref="ConsultationResult"/> del módulo → <c>flit.consultas.v1.ResultadoConsulta</c>. Traducción 1:1, sin
/// reglas: el semáforo, los chequeos, los campos y las certificaciones ya vienen normalizados por los mappers.
/// </summary>
internal static class ResultadoMapper
{
    public static ResultadoConsulta ToProto(ConsultationResult r, bool incluirCruda)
    {
        ArgumentNullException.ThrowIfNull(r);
        var resultado = new ResultadoConsulta
        {
            Proveedor = r.Provider ?? string.Empty,
            Semaforo = Semaforo(r.Overall),
            DesdeCache = r.FromCache,
            ConsultadoEn = Timestamp.FromDateTimeOffset(r.QueriedAt ?? DateTimeOffset.UtcNow),
        };

        foreach (var check in r.Checks)
            resultado.Chequeos.Add(ToProto(check));
        foreach (var field in r.HydratedFields)
            resultado.Campos.Add(new Campo { Clave = field.FieldKey, ValorTexto = field.ValueText ?? string.Empty, ValorJson = field.ValueJson ?? string.Empty });
        if (r.Certifications is { } bundle)
            resultado.Certificaciones = ToProto(bundle);
        if (incluirCruda && r.RawPayload is { } raw)
        {
            resultado.RespuestaCruda = new RespuestaCruda
            {
                Proveedor = raw.ProviderKey,
                TipoSujeto = raw.SubjectKind,
                ClaveSujeto = raw.SubjectKey ?? string.Empty,
                Json = raw.PayloadJson,
            };
        }

        return resultado;
    }

    public static ConsultarAvaluosResponse ToProto(SuggestedCommercialValue v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var response = new ConsultarAvaluosResponse { ValorSugerido = v.Sugerido ?? 0, FuentePrincipal = v.FuentePrincipal ?? string.Empty };
        foreach (var s in v.Sources)
        {
            response.Avaluos.Add(new Avaluo
            {
                Fuente = s.Source,
                Estado = s.Status switch
                {
                    "ok" => EstadoAvaluo.Ok,
                    "error" => EstadoAvaluo.Error,
                    _ => EstadoAvaluo.SinDatos,
                },
                Valor = s.Value ?? 0,
                Mensaje = s.Message ?? string.Empty,
                Muestras = s.Muestras ?? 0,
            });
        }

        return response;
    }

    private static Chequeo ToProto(ConsultationCheck c)
    {
        var chequeo = new Chequeo
        {
            Clave = c.Key,
            Etiqueta = c.Label,
            Estado = c.Status?.ToUpperInvariant() switch
            {
                "OK" => EstadoChequeo.Ok,
                "WARN" => EstadoChequeo.Warn,
                "FAIL" => EstadoChequeo.Fail,
                "ERROR" => EstadoChequeo.Error,
                _ => EstadoChequeo.Unknown,
            },
            Fuente = c.Source ?? string.Empty,
            Mensaje = c.Message ?? string.Empty,
        };
        foreach (var f in c.Details ?? [])
        {
            chequeo.Comparendos.Add(new Comparendo
            {
                Numero = f.Numero ?? string.Empty,
                Fecha = f.Fecha ?? string.Empty,
                Valor = f.Valor?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Organismo = f.Organismo ?? string.Empty,
                Estado = f.Estado ?? string.Empty,
                Infraccion = f.Infraccion ?? string.Empty,
            });
        }

        foreach (var d in c.Datos ?? [])
            chequeo.Datos.Add(new Dato { Etiqueta = d.Etiqueta, Valor = d.Valor });
        return chequeo;
    }

    private static Flit.Consultas.Grpc.V1.Semaforo Semaforo(string? overall) => overall?.ToUpperInvariant() switch
    {
        "GREEN" or "VERDE" => Flit.Consultas.Grpc.V1.Semaforo.Verde,
        "YELLOW" or "AMARILLO" => Flit.Consultas.Grpc.V1.Semaforo.Amarillo,
        "RED" or "ROJO" => Flit.Consultas.Grpc.V1.Semaforo.Rojo,
        _ => Flit.Consultas.Grpc.V1.Semaforo.Unspecified,
    };

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
            c.Rtm.Add(new CertificacionRtm
            {
                NumeroCertificado = Valor(r.CertificateNumber.Value, r.CertificateNumber.Raw),
                Cda = Valor(r.Cda.Value, r.Cda.Raw),
                Expedicion = Fecha(r.IssuedOn),
                VigenteDesde = Fecha(r.ValidFrom),
                VigenteHasta = Fecha(r.ValidUntil),
                Estado = Estado(r.Status),
                TipoRevision = Valor(r.InspectionType, r.InspectionType),
            });
        }

        foreach (var m in b.MerchantRegistrations)
        {
            var matricula = new MatriculaMercantil
            {
                Nit = Valor(m.Nit, m.Nit),
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
                    Nombre = Valor(rep.Name, rep.Name),
                    TipoDocumento = Valor(rep.DocumentType, rep.DocumentType),
                    NumeroDocumento = Valor(rep.DocumentNumber, rep.DocumentNumber),
                    Cargo = Valor(rep.Role, rep.Role),
                    Facultades = Valor(rep.Powers, rep.Powers),
                });
            }

            c.MatriculasMercantiles.Add(matricula);
        }

        return c;
    }

    private static ValorCertificado Valor(string? valor, string? crudo) => new() { Valor = valor ?? string.Empty, Crudo = crudo ?? string.Empty };

    private static FechaCertificada Fecha(CertifiedDate d) =>
        new() { Valor = d.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty, Crudo = d.Raw ?? string.Empty };

    private static EstadoVigencia Estado(CertifiedStatus s) => new()
    {
        Valor = s.Value switch
        {
            VigencyStatus.Vigente => Vigencia.Vigente,
            VigencyStatus.Vencido => Vigencia.Vencido,
            VigencyStatus.NoAplica => Vigencia.NoAplica,
            _ => Vigencia.Unspecified,
        },
        Crudo = s.Raw ?? string.Empty,
    };
}
