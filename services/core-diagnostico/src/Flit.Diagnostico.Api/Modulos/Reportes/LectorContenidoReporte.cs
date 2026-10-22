using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Arma el contenido de los reportes (HU #13847, F07-H2): <see cref="ContenidoReporteVehiculo"/> para el PDF de un vehículo y
/// <see cref="ContenidoReporteLote"/> para el Excel del lote. Lee con el contexto de la empresa (filtro global) y, para la
/// compañía, con la visibilidad de <see cref="AccesoLote"/>: otra compañía, PROPIOS de otro usuario o un vehículo de otro lote →
/// 404 <c>NO_ENCONTRADO</c>. Las celdas del vehículo (o del lote) salen en UNA consulta y las personas en otra; de la celda solo se
/// leen fuente, estado, motivo de N/A, fecha del dato y payload (nunca origen, usuario manual ni tipo de error), y el resultado
/// pasa por la proyección cerrada del detalle de celda (<see cref="ProyectorDetalleCelda"/>). Los documentos de persona se
/// descifran solo dentro de esa proyección para enmascararlos («****» + 4); el documento en claro no llega al contenido.
/// Nombres: creador por <see cref="IResolutorNombresUsuario"/> («No disponible» si Identidad no responde), empresa del claim
/// <c>company_name</c> de quien pide (o la que pase el ZIP, <c>evidencias.empresa_nombre</c>).
/// </summary>
/// <example>
/// <code>
/// var contenido = await lector.VehiculoAsync(usuario, diagnosticoId, vehiculoId, ct);       // PDF (F07-H3)
/// var lote = await lector.LoteAsync(usuario, diagnosticoId, ct);                           // Excel (F07-H4)
/// var delZip = await lector.VehiculoAsync(cabecera, vehiculoId, evidencia.EmpresaNombre, ct); // ZIP (F07-H5)
/// </code>
/// </example>
internal sealed class LectorContenidoReporte(DiagnosticoDb db, IProtectorDocumentoPersona protector, IResolutorNombresUsuario nombres, ICatalogoTiposTramite catalogo)
{
    /// <summary>Texto de cada motivo de N/A (los mismos del detalle de celda de la interfaz).</summary>
    public static readonly IReadOnlyDictionary<MotivoNa, string> TextoMotivoNa = new Dictionary<MotivoNa, string>
    {
        [MotivoNa.ServicioPublico] = "El vehículo es de servicio público: esta consulta no aplica.",
        [MotivoNa.DepartamentoNoSoportado] = "El departamento del vehículo todavía no está soportado.",
        [MotivoNa.SinDocumentoLocatario] = "El vehículo está sin el documento del locatario: no hay a quién consultar.",
    };

    /// <summary>El contenido del vehículo para la compañía del token, con su visibilidad y la empresa del claim.</summary>
    public async Task<ContenidoReporteVehiculo> VehiculoAsync(UsuarioActual usuario, Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        return await VehiculoAsync(cabecera, vehiculoId, usuario.EmpresaNombre, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El contenido del vehículo de una cabecera que ya pasó el control de acceso de quien llama (el ZIP con su contexto de empresa);
    /// 404 si la cabecera o el vehículo (dentro de ese lote) no es de la empresa del contexto.
    /// </summary>
    public async Task<ContenidoReporteVehiculo> VehiculoAsync(CabeceraDiagnostico cabecera, Guid vehiculoId, string? empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        await ExigirCabeceraAsync(cabecera, ct).ConfigureAwait(false);
        var vehiculo = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.Id == vehiculoId && v.DiagnosticoId == cabecera.Id && v.TenantId == cabecera.TenantId)
            .Select(v => new IdentificacionLeida(v.Id, v.Orden, v.IdentificadorNorm, v.TipoIdentificador, v.Placa, v.Vin))
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El vehículo");
        var celdas = await Celdas(cabecera, vehiculoId).ToListAsync(ct).ConfigureAwait(false);
        var personas = await db.Set<PersonaVehiculo>().AsNoTracking()
            .Where(p => p.TenantId == cabecera.TenantId && p.VehiculoId == vehiculoId)
            .OrderBy(p => p.Rol).ThenBy(p => p.Orden)
            .ToListAsync(ct).ConfigureAwait(false);
        var lote = await EncabezadoAsync(cabecera, empresa, ct).ConfigureAwait(false);

        return new ContenidoReporteVehiculo(
            new EncabezadoReporte(
                lote.DiagnosticoId, lote.NombreLote, lote.Empresa, lote.UsuarioCreador, lote.CreadoEn, lote.TipoAnalisis,
                vehiculo.Identificador, CodigosDeEnum.Codigo(vehiculo.Tipo), vehiculo.Placa, vehiculo.Vin),
            Secciones(cabecera, celdas, personas));
    }

    /// <summary>El contenido del lote para la compañía del token, con su visibilidad y la empresa del claim.</summary>
    public async Task<ContenidoReporteLote> LoteAsync(UsuarioActual usuario, Guid diagnosticoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        return await LoteAsync(cabecera, usuario.EmpresaNombre, ct).ConfigureAwait(false);
    }

    /// <summary>El contenido del lote de una cabecera que ya pasó el control de acceso; 404 si no es de la empresa del contexto.</summary>
    public async Task<ContenidoReporteLote> LoteAsync(CabeceraDiagnostico cabecera, string? empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        await ExigirCabeceraAsync(cabecera, ct).ConfigureAwait(false);
        var vehiculosDelLote = db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.DiagnosticoId == cabecera.Id && v.TenantId == cabecera.TenantId);
        var vehiculos = await vehiculosDelLote
            .OrderBy(v => v.Orden)
            .Select(v => new IdentificacionLeida(v.Id, v.Orden, v.IdentificadorNorm, v.TipoIdentificador, v.Placa, v.Vin))
            .ToListAsync(ct).ConfigureAwait(false);
        var celdas = (await Celdas(cabecera, null).ToListAsync(ct).ConfigureAwait(false)).ToLookup(c => c.VehiculoId);
        var personas = (await db.Set<PersonaVehiculo>().AsNoTracking()
                .Where(p => p.TenantId == cabecera.TenantId && vehiculosDelLote.Any(v => v.Id == p.VehiculoId))
                .OrderBy(p => p.Rol).ThenBy(p => p.Orden)
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(p => p.VehiculoId);
        var encabezado = await EncabezadoAsync(cabecera, empresa, ct).ConfigureAwait(false);

        return new ContenidoReporteLote(encabezado, [.. vehiculos.Select(v => new VehiculoReporte(
            v.Id, v.Orden, v.Identificador, CodigosDeEnum.Codigo(v.Tipo), v.Placa, v.Vin,
            Secciones(cabecera, [.. celdas[v.Id]], [.. personas[v.Id]])))]);
    }

    /// <summary>La cabecera debe existir en la empresa del contexto: una de otra compañía pasada a mano no se lee (404).</summary>
    private async Task ExigirCabeceraAsync(CabeceraDiagnostico cabecera, CancellationToken ct)
    {
        if (!await db.Set<CabeceraDiagnostico>().AsNoTracking().AnyAsync(c => c.Id == cabecera.Id && c.TenantId == cabecera.TenantId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
    }

    /// <summary>
    /// Solo lo que el reporte puede mostrar de cada celda del lote (o de un vehículo): ni origen, ni usuario manual, ni tipo de error.
    /// </summary>
    private IQueryable<CeldaLeidaReporte> Celdas(CabeceraDiagnostico cabecera, Guid? vehiculoId)
    {
        var id = cabecera.Id;
        var tenant = cabecera.TenantId;
        return db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == id && c.TenantId == tenant && (vehiculoId == null || c.VehiculoId == vehiculoId))
            .Select(c => new CeldaLeidaReporte(c.VehiculoId, c.Fuente, c.Estado, c.MotivoNa, c.DatoObtenidoEn, c.Payload));
    }

    private async Task<EncabezadoLoteReporte> EncabezadoAsync(CabeceraDiagnostico cabecera, string? empresa, CancellationToken ct)
    {
        var creador = (await nombres.ResolverAsync(cabecera.TenantId, [cabecera.UsuarioCreadorId], ct).ConfigureAwait(false))
            .GetValueOrDefault(cabecera.UsuarioCreadorId, ResolutorNombresUsuario.NoDisponible);
        var tipo = await EtiquetaTipoAnalisis.ResolverAsync(cabecera, catalogo, ct).ConfigureAwait(false);
        return new EncabezadoLoteReporte(
            cabecera.Id, cabecera.Nombre, string.IsNullOrWhiteSpace(empresa) ? ResolutorNombresUsuario.NoDisponible : empresa.Trim(), creador,
            cabecera.CreadoEn.ToOffset(VigenciaDelDia.OffsetColombia), tipo);
    }

    /// <summary>Una sección por fuente del alcance, en el orden de las columnas de la grilla; las celdas de otras fuentes no salen.</summary>
    private List<SeccionReporte> Secciones(CabeceraDiagnostico cabecera, IReadOnlyList<CeldaLeidaReporte> celdas, IReadOnlyList<PersonaVehiculo> personas)
    {
        var porFuente = celdas.GroupBy(c => c.Fuente).ToDictionary(g => g.Key, g => g.First());
        return [.. Alcance.Agrupar(cabecera.Alcance).Fuentes.Select(f => Seccion(f, porFuente.GetValueOrDefault(f), personas))];
    }

    private SeccionReporte Seccion(Fuente fuente, CeldaLeidaReporte? celda, IReadOnlyList<PersonaVehiculo> personas)
    {
        var codigo = CodigosDeEnum.Codigo(fuente);
        var grupo = CodigosDeEnum.Codigo(GruposFuente.De(fuente));
        var estado = celda?.Estado switch
        {
            EstadoCelda.Completado => EstadoSeccion.Completado,
            EstadoCelda.Na => EstadoSeccion.NoAplica,
            EstadoCelda.Error => EstadoSeccion.NoDisponible,
            _ => EstadoSeccion.Pendiente,
        };
        var porPersona = EjecutorPorPersonas.EsPorPersona(fuente);
        IReadOnlyList<PersonaReporte>? deLaFuente = null;
        if (porPersona)
        {
            var rol = EjecutorPorPersonas.RolDe(fuente);
            deLaFuente = estado is EstadoSeccion.Completado or EstadoSeccion.NoDisponible
                ? [.. ProyectorDetalleCelda.Personas(personas.Where(p => p.Rol == rol), celda!.Payload, protector).Select(Persona)]
                : [];
        }

        return estado switch
        {
            EstadoSeccion.Completado => new SeccionReporte(
                codigo, grupo, estado, null, null, celda!.DatoObtenidoEn,
                porPersona ? null : ProyectorDetalleCelda.ResultadoVehiculo(fuente, celda.Payload), deLaFuente),
            EstadoSeccion.NoAplica => new SeccionReporte(
                codigo, grupo, estado, celda!.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : null,
                celda.MotivoNa is { } motivo ? TextoMotivoNa.GetValueOrDefault(motivo) : null, null, null, deLaFuente),
            EstadoSeccion.NoDisponible => new SeccionReporte(codigo, grupo, estado, null, SeccionReporte.ConsultaNoDisponible, null, null, deLaFuente),
            _ => new SeccionReporte(codigo, grupo, estado, null, null, null, null, deLaFuente),
        };
    }

    /// <summary>La persona del detalle para el reporte: sin tipo de error; su error es «consulta no disponible».</summary>
    private static PersonaReporte Persona(PersonaDetalleDto p) => new(
        p.Orden, p.Rol, p.TipoDocumento, p.DocumentoEnmascarado, p.DeclaradoPorCliente,
        p.DeclaradoPorCliente ? PersonaReporte.Declarado : null, p.Consulta,
        p.Estado switch
        {
            EjecutorPorPersonas.EstadoOk => EstadoPersonaReporte.Ok,
            DetalleCelda.SinResultado => EstadoPersonaReporte.SinResultado,
            _ => EstadoPersonaReporte.NoDisponible,
        },
        p.ConsultadoEn, p.Resultado);

    private sealed record IdentificacionLeida(Guid Id, int Orden, string Identificador, Domain.Vehiculos.TipoIdentificador Tipo, string? Placa, string? Vin);

    private sealed record CeldaLeidaReporte(Guid VehiculoId, Fuente Fuente, EstadoCelda Estado, MotivoNa? MotivoNa, DateTimeOffset? DatoObtenidoEn, string? Payload);
}
