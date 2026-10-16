using System.Globalization;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plantillas;
using Flit.Comparendos.Domain.Plazos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Plantillas;

/// <summary>Formatos de §6.15: «1 de octubre de 2026» y «$ 1.234.567».</summary>
public static class Formato
{
    private static readonly string[] Meses = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static string? Fecha(DateOnly? d) => d is { } x ? $"{x.Day} de {Meses[x.Month - 1]} de {x.Year}" : null;

    public static string? Pesos(decimal? v) =>
        v is { } x ? "$ " + Math.Round(x, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.') : null;

    public static string EstadoLegal(EstadoLegal e) => e switch
    {
        Domain.Plazos.EstadoLegal.EnTiempo => "En tiempo",
        Domain.Plazos.EstadoLegal.Vencido => "Vencido",
        Domain.Plazos.EstadoLegal.Caducado => "Caducado",
        Domain.Plazos.EstadoLegal.Prescrito => "Prescrito",
        _ => "Sin notificación",
    };
}

/// <summary>Lo que se le puede agregar al contexto de un comparendo según el documento (DP, portal…).</summary>
public sealed record ExtrasContexto(
    RepresentanteLegal? Representante = null,
    byte[]? FirmaPng = null,
    string? Radicado = null,
    DateOnly? FechaDp = null,
    TipologiaDp? Tipologia = null,
    string? PortalEnlace = null,
    DateOnly? PortalVence = null);

/// <summary>
/// Valores de las variables (§6.15) para un comparendo concreto. El peticionario es el titular del comparendo (su
/// nombre y documento, o el titular del NIT monitoreado) y nunca FLIT [Excel P6].
/// </summary>
public sealed class ContextoPlantilla(ComparendosDb db, CalendarioService calendario, Reloj reloj)
{
    public async Task<Dictionary<string, string?>> DeAsync(Comparendo c, ExtrasContexto? extras, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        extras ??= new ExtrasContexto();
        var hoy = reloj.Hoy;
        var motor = await calendario.MotorAsync(ct).ConfigureAwait(false);
        var s = motor.Evaluar(Plazos.Fechas(c), hoy);
        var config = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == c.TenantId, ct).ConfigureAwait(false);
        var ot = c.OrganismoTransitoId is { } otId ? await db.OrganismosTransito.AsNoTracking().FirstOrDefaultAsync(o => o.Id == otId, ct).ConfigureAwait(false) : null;
        var nit = c.IdentificacionMonitoreadaId is { } nid ? await db.IdentificacionesMonitoreadas.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nid, ct).ConfigureAwait(false) : null;
        var conductor = c.Contraventor ?? await db.Contraventores.AsNoTracking().FirstOrDefaultAsync(k => k.ComparendoId == c.Id, ct).ConfigureAwait(false);
        var rl = extras.Representante;

        var peticionarioDocumento = c.NumeroDocumentoInfractor ?? nit?.NumeroBase;
        var peticionarioTipo = c.TipoDocumentoInfractor ?? nit?.TipoDocumento ?? TipoDocumento.NIT;
        if (peticionarioTipo == TipoDocumento.NIT)
            peticionarioDocumento = Nit.ConDigito(peticionarioDocumento, nit?.NumeroBase, config?.EmpresaNit);

        var porcentaje = s.DescuentoVigente switch { Descuento.Cincuenta => 50, Descuento.Veinticinco => 25, _ => 0 };
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["comparendo.numero"] = c.Numero,
            ["comparendo.placa"] = c.Placa,
            ["comparendo.fecha_imposicion"] = Formato.Fecha(c.FechaImposicion),
            ["comparendo.fecha_notificacion"] = Formato.Fecha(c.FechaNotificacion) ?? "(sin fecha de notificación)",
            ["comparendo.codigo_infraccion"] = c.CodigoInfraccion,
            ["comparendo.descripcion_infraccion"] = c.DescripcionInfraccion?.TrimEnd().TrimEnd('.'), // va entre paréntesis o a mitad de frase: sin punto final (B9-3)
            ["comparendo.direccion_infraccion"] = c.DireccionInfraccion,
            ["comparendo.valor_total"] = Formato.Pesos(c.ValorTotal),
            ["comparendo.secretaria"] = ot?.Nombre ?? c.SecretariaTexto,
            ["comparendo.es_fotomulta"] = c.EsFotomulta == false ? "comparendo físico" : "fotomulta",
            ["comparendo.fecha_caducidad"] = Formato.Fecha(c.FechaCaducidad),
            ["comparendo.fecha_prescripcion"] = Formato.Fecha(c.FechaPrescripcion),
            ["comparendo.estado_legal"] = Formato.EstadoLegal(s.Estado),
            ["descuento.ventana"] = porcentaje > 0 ? $"{porcentaje}% de descuento" : "sin descuento",
            ["descuento.porcentaje"] = porcentaje.ToString(CultureInfo.InvariantCulture),
            ["descuento.fecha_fin_50"] = Formato.Fecha(c.FechaFinDescuento50),
            ["descuento.fecha_fin_25"] = Formato.Fecha(c.FechaFinDescuento25),
            ["ot.nombre"] = ot?.Nombre ?? c.SecretariaTexto,
            ["ot.codigo"] = ot?.Codigo,
            ["ot.municipio"] = ot?.Municipio,
            ["ot.departamento"] = ot?.Departamento,
            ["peticionario.nombre"] = c.NombreInfractor ?? nit?.NombreTitular ?? config?.EmpresaNombre,
            ["peticionario.tipo_documento"] = peticionarioTipo.ToString(),
            ["peticionario.numero_documento"] = peticionarioDocumento,
            ["peticionario.direccion"] = rl?.DireccionNotificacion,
            ["rl.nombres"] = rl?.Nombres,
            ["rl.tipo_documento"] = rl?.TipoDocumento.ToString(),
            ["rl.numero_documento"] = rl?.NumeroDocumento,
            ["rl.cargo"] = rl?.Cargo ?? (rl is null ? null : "Representante legal"),
            ["rl.correo"] = rl?.Correo,
            ["rl.firma_imagen"] = extras.FirmaPng is { Length: > 0 } firma ? $"<img src=\"data:image/png;base64,{Convert.ToBase64String(firma)}\" width=\"180\" />" : string.Empty,
            ["empresa.razon_social"] = config?.EmpresaNombre,
            ["empresa.nit"] = config?.EmpresaNit is { } empresaNit ? $"{empresaNit}-{Nit.DigitoVerificacion(empresaNit)}" : null,
            ["empresa.correo_respuesta"] = config?.CorreoRespuesta ?? rl?.Correo,
            ["dp.radicado"] = extras.Radicado,
            ["dp.fecha"] = Formato.Fecha(extras.FechaDp ?? hoy),
            ["dp.tipologia_nombre"] = extras.Tipologia?.Nombre,
            ["dp.fundamento"] = extras.Tipologia?.DescripcionJuridica,
            ["conductor.nombres"] = conductor?.Nombres,
            ["conductor.tipo_documento"] = conductor?.TipoDocumento.ToString(),
            ["conductor.numero_documento"] = conductor?.NumeroDocumento,
            ["conductor.correo"] = conductor?.Correo,
            ["conductor.telefono"] = conductor?.Telefono,
            ["conductor.direccion"] = conductor?.Direccion,
            ["conductor.ciudad"] = conductor?.Ciudad,
            ["portal.enlace"] = extras.PortalEnlace,
            ["portal.vence"] = Formato.Fecha(extras.PortalVence),
            ["hoy"] = Formato.Fecha(hoy),
            ["anio"] = hoy.Year.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static Dictionary<string, string?> Ejemplo() =>
        VariablesPlantilla.Todas.ToDictionary(v => v.Nombre, v => v.Nombre == "rl.firma_imagen" ? string.Empty : (string?)v.Ejemplo, StringComparer.Ordinal);
}
