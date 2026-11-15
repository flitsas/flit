using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Reporte <c>vehiculos</c> (HU #14579, BE-2; contrato-api §4; SP-16, O10): un renglón por vehículo de los diagnósticos creados en el
/// rango, con la placa, el VIN y el documento <b>completo</b> de propietarios y locatario de <c>personas_vehiculo</c> (cifrado hasta
/// después del registro). Sin nombre de persona: no se guarda y la respuesta completa de la fuente no se descifra. El locatario es el
/// del RUNT si lo hay; si no, el declarado (persona o, mientras el RUNT no cierra, la captura del vehículo).
/// </summary>
internal sealed class ReporteVehiculos(NombresTramite tramites) : IReporte
{
    public TipoReporte Tipo => TipoReporte.Vehiculos;

    public Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) => Consulta(contexto, filtro).CountAsync(ct);

    public async Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var consulta = Consulta(contexto, filtro).OrderByDescending(f => f.CreadoEn).ThenBy(f => f.Id).Skip(saltar);
        if (tomar is { } t)
            consulta = consulta.Take(t);
        var leidas = await consulta.ToListAsync(ct).ConfigureAwait(false);
        var personas = await PersonasAsync(contexto, leidas, ct).ConfigureAwait(false);
        var nombres = await tramites.NombresAsync(leidas.Select(l => l.TipoTramiteCodigo), ct).ConfigureAwait(false);
        return [.. leidas.Select(l => Fila(l, personas[(l.Empresa, l.Id)], nombres))];
    }

    private static FilaReporte Fila(FilaLeida l, IEnumerable<PersonaLeida> personas, IReadOnlyDictionary<string, string> tramites)
    {
        var propias = personas.ToList();
        var propietarios = propias.Where(p => p.Rol == RolPersona.Propietario).OrderBy(p => p.Orden)
            .Select(p => new DocumentoCifrado(p.Id, p.DocTipo, p.DocCifrado)).ToList();
        var locatarios = propias.Where(p => p.Rol == RolPersona.Locatario).OrderBy(p => p.Origen == OrigenDocumento.Proveedor ? 0 : 1).ThenBy(p => p.Orden).ToList();
        (DocumentoCifrado Documento, OrigenDocumento Origen)? locatario = locatarios.Count > 0
            ? (new DocumentoCifrado(locatarios[0].Id, locatarios[0].DocTipo, locatarios[0].DocCifrado), locatarios[0].Origen)
            : l.LocatarioDocOrigen == OrigenDocumento.DeclaradoCliente && l.LocatarioDocTipo is { } tipo && l.LocatarioDocCifrado is { } cifrado
                ? (new DocumentoCifrado(null, tipo, cifrado), OrigenDocumento.DeclaradoCliente)
                : null;

        var fila = new FilaReporte { Empresa = l.Empresa };
        var v = fila.Valores;
        v["fecha_diagnostico"] = ValoresReporte.Instante(l.CreadoEn);
        v["diagnostico_nombre"] = l.DiagnosticoNombre;
        v["modalidad"] = l.EsExpress ? CatalogoReportes.Express : CatalogoReportes.Masivo;
        v["tipo_analisis"] = ValoresReporte.Codigo<ModoDiagnostico>(l.Modo);
        v["tipo_tramite"] = l.TipoTramiteCodigo is { } codigo ? tramites.GetValueOrDefault(codigo, codigo) : null;
        v["placa"] = l.Placa;
        v["vin"] = l.Vin;
        v["servicio"] = l.Servicio;
        v["organismo"] = l.Organismo;
        v["departamento"] = l.Departamento;
        v["propietarios_documentos"] = null;
        v["locatario_documento"] = null;
        v["locatario_origen"] = locatario is { } lo ? (lo.Origen == OrigenDocumento.Proveedor ? "RUNT" : "DECLARADO") : null;
        v["veredicto"] = l.Veredicto;
        v["estado_consultas"] = l.EstadoConsultas;
        v["saneamiento_estado"] = ValoresReporte.Codigo(l.SaneamientoEstado);
        v["tramite_numero"] = l.TramiteNumero;
        fila.Cifrados.Add(new DocumentosPorDescifrar("propietarios_documentos", propietarios));
        fila.Cifrados.Add(new DocumentosPorDescifrar("locatario_documento", locatario is { } d ? [d.Documento] : []));
        return fila;
    }

    /// <summary>Personas de los vehículos leídos, por lotes de 500 ids y con la compañía en la consulta y en la clave.</summary>
    private static async Task<ILookup<(Guid, Guid), PersonaLeida>> PersonasAsync(ContextoReporte contexto, IReadOnlyList<FilaLeida> leidas, CancellationToken ct)
    {
        var todas = new List<PersonaLeida>();
        foreach (var lote in ValoresReporte.Lotes(leidas))
        {
            var ids = lote.Select(l => l.Id).ToList();
            var empresas = lote.Select(l => l.Empresa).Distinct().ToList();
            todas.AddRange(await contexto.Fuentes.Personas
                .Where(p => empresas.Contains(p.TenantId) && ids.Contains(p.VehiculoId))
                .Select(p => new PersonaLeida(p.Id, p.TenantId, p.VehiculoId, p.Rol, p.Orden, p.DocTipo, p.DocCifrado, p.Origen))
                .ToListAsync(ct).ConfigureAwait(false));
        }

        var claves = leidas.Select(l => (l.Empresa, l.Id)).ToHashSet();
        return todas.Where(p => claves.Contains((p.Empresa, p.VehiculoId))).ToLookup(p => (p.Empresa, p.VehiculoId));
    }

    private static IQueryable<FilaLeida> Consulta(ContextoReporte contexto, FiltroReporte filtro)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        var f = contexto.Fuentes;
        var (desde, hasta) = (filtro.DesdeInstante, filtro.HastaInstante);
        var cabeceras = f.Cabeceras.Where(c => c.CreadoEn >= desde && c.CreadoEn < hasta);
        foreach (var modalidad in filtro.Valores(CatalogoReportes.FiltroModalidad))
        {
            var express = modalidad == CatalogoReportes.Express;
            cabeceras = cabeceras.Where(c => c.EsExpress == express);
        }

        foreach (var codigo in filtro.Valores(CatalogoReportes.FiltroTipoAnalisis))
        {
            Persistence.Configuraciones.CodigosDeEnum.TryLeer<ModoDiagnostico>(codigo, out var modo);
            cabeceras = cabeceras.Where(c => c.Modo == modo);
        }

        var vehiculos = f.Vehiculos;
        if (filtro.Placa is { } placa)
            vehiculos = vehiculos.Where(v => v.IdentificadorNorm.StartsWith(placa));
        var vigentes = f.Dictamenes.Where(d => d.Vigente);
        foreach (var veredicto in filtro.Valores(CatalogoReportes.FiltroVeredicto))
        {
            vehiculos = veredicto == CatalogoReportes.SinDictamen
                ? vehiculos.Where(v => !vigentes.Any(d => d.TenantId == v.TenantId && d.VehiculoId == v.Id))
                : vehiculos.Where(v => vigentes.Any(d => d.TenantId == v.TenantId && d.VehiculoId == v.Id && d.Veredicto == veredicto));
        }

        return from v in vehiculos
               join c in cabeceras on new { v.TenantId, Id = v.DiagnosticoId } equals new { c.TenantId, c.Id }
               select new FilaLeida
               {
                   Id = v.Id,
                   Empresa = v.TenantId,
                   CreadoEn = c.CreadoEn,
                   DiagnosticoNombre = c.Nombre,
                   EsExpress = c.EsExpress,
                   Modo = c.Modo,
                   TipoTramiteCodigo = c.TipoTramiteCodigo,
                   Placa = v.Placa,
                   Vin = v.Vin,
                   Servicio = v.Servicio,
                   Organismo = v.OrganismoNombre,
                   Departamento = v.DepartamentoCodigo,
                   LocatarioDocTipo = v.LocatarioDocTipo,
                   LocatarioDocCifrado = v.LocatarioDocCifrado,
                   LocatarioDocOrigen = v.LocatarioDocOrigen,
                   Veredicto = vigentes.Where(d => d.TenantId == v.TenantId && d.VehiculoId == v.Id).Select(d => d.Veredicto).FirstOrDefault(),
                   EstadoConsultas = v.EstadoConsultas,
                   SaneamientoEstado = v.SaneamientoEstado,
                   TramiteNumero = v.TramiteNumero,
               };
    }

    private sealed record PersonaLeida(Guid Id, Guid Empresa, Guid VehiculoId, RolPersona Rol, int Orden, string DocTipo, byte[] DocCifrado, OrigenDocumento Origen);

    /// <summary>Fila leída de la base (inicializador de miembros, para que EF ordene y pagine).</summary>
    private sealed class FilaLeida
    {
        public Guid Id { get; init; }
        public Guid Empresa { get; init; }
        public DateTimeOffset CreadoEn { get; init; }
        public string DiagnosticoNombre { get; init; } = string.Empty;
        public bool EsExpress { get; init; }
        public ModoDiagnostico Modo { get; init; }
        public string? TipoTramiteCodigo { get; init; }
        public string? Placa { get; init; }
        public string? Vin { get; init; }
        public string? Servicio { get; init; }
        public string? Organismo { get; init; }
        public string? Departamento { get; init; }
        public string? LocatarioDocTipo { get; init; }
        public byte[]? LocatarioDocCifrado { get; init; }
        public OrigenDocumento? LocatarioDocOrigen { get; init; }
        public string? Veredicto { get; init; }
        public string EstadoConsultas { get; init; } = string.Empty;
        public EstadoSaneamiento? SaneamientoEstado { get; init; }
        public string? TramiteNumero { get; init; }
    }
}

/// <summary>
/// Nombre de los tipos de trámite del catálogo de Trámites (caché del adaptador): código → nombre, una vez por petición; si el catálogo
/// no responde, el nombre es el código (contrato-api §4).
/// </summary>
internal sealed class NombresTramite(ICatalogoTiposTramite catalogo)
{
    private IReadOnlyDictionary<string, string>? _nombres;

    public async Task<IReadOnlyDictionary<string, string>> NombresAsync(IEnumerable<string?> codigos, CancellationToken ct)
    {
        if (!codigos.Any(c => c is not null))
            return new Dictionary<string, string>();
        if (_nombres is not null)
            return _nombres;
        try
        {
            var publicados = await catalogo.ListarAsync(ct).ConfigureAwait(false);
            _nombres = publicados.GroupBy(t => t.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.Ordinal);
        }
        catch (CatalogoTiposTramiteNoDisponibleException)
        {
            _nombres = new Dictionary<string, string>();
        }

        return _nombres;
    }
}
