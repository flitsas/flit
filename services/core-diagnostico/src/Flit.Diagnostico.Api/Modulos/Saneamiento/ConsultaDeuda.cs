using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Panel de deuda de un vehículo terminado (plan §6.5, HU #14576 AC36/AC37) y su lectura para varias placas (Excel múltiple, AC38).
/// Orden de validación (SP-9): (1) el lote se ve (<see cref="AccesoLote"/>: otra compañía o PROPIOS ajeno, 404); (2) el vehículo
/// es de ese lote (404); solo después (3) 409 <see cref="EmisionPdf.CodigoVehiculoNoTerminado"/> si alguna celda no está en
/// COMPLETADO o NA (<see cref="AvanceVehiculo"/>, el mismo «terminado» de la grilla, C1).
/// <para>
/// Los datos salen de la proyección cerrada de los reportes (<see cref="LectorContenidoReporte"/>, la misma del detalle de la celda),
/// nunca del crudo: sin nombre de persona (SP-11), con el documento completo en el JSON y el PDF y enmascarado en el Excel. El
/// estado de cada fuente usa la MISMA lectura de hechos del dictamen (<see cref="LectorHechosDictamen.Leer"/>) y los mismos
/// predicados de deuda (C4): SIMIT con deuda = comparendos, sin paz y salvo o acuerdo con saldo; impuesto con deuda =
/// <c>tiene_deuda</c> o monto positivo; SOAT y RTM con la cobertura y la exigibilidad por clase del evaluador.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var panel = await consulta.ObtenerAsync(diagnosticoId, vehiculoId, ct);          // GET …/deuda
/// var varios = await consulta.VariosAsync([vid1, vid2], ct);                       // POST /vehiculos/deuda/excel
/// </code>
/// </example>
internal sealed class ConsultaDeuda(DiagnosticoDb db, UsuarioActual usuario, LectorContenidoReporte lector, TimeProvider reloj)
{
    /// <summary>Tipo de trámite con el que se leen los hechos cuando el lote no tiene uno (C5): no cambia ningún predicado de deuda.</summary>
    public const string TipoTramitePorDefecto = "TRASPASO";

    /// <summary>El panel del vehículo visible y terminado; 404 o 409 en el orden de SP-9.</summary>
    public async Task<PanelDeudaDto> ObtenerAsync(Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        return await DeLoteAsync(cabecera, vehiculoId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El panel de un vehículo de una cabecera que ya pasó el control de acceso de quien llama: el backoffice (SP-7, HU #14576) la lee
    /// con un contexto fijado a la empresa explícita. 404 si el vehículo no es de ese lote; 409 si no está terminado.
    /// </summary>
    internal async Task<PanelDeudaDto> DeLoteAsync(CabeceraDiagnostico cabecera, Guid vehiculoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        var tenant = cabecera.TenantId;
        if (!await db.Set<VehiculoDiagnostico>().AsNoTracking()
                .AnyAsync(v => v.Id == vehiculoId && v.DiagnosticoId == cabecera.Id && v.TenantId == tenant, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");
        var estados = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == cabecera.Id && c.TenantId == tenant && c.VehiculoId == vehiculoId)
            .Select(c => c.Estado)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!AvanceVehiculo.Terminado(estados))
            throw ReglaException.Conflicto(EmisionPdf.CodigoVehiculoNoTerminado, "El vehículo todavía tiene consultas sin terminar: su deuda se ve cuando terminen.");
        return await PanelAsync(cabecera, vehiculoId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Los paneles de <paramref name="vehiculoIds"/> (ya sin repetidos) que el usuario ve y que están terminados, en el orden pedido.
    /// Parte de <see cref="AccesoLote.VisiblesAsync"/> unido por <c>(tenant_id, diagnostico_id)</c> (SP-5, SP-9): un id inexistente,
    /// de otra compañía, de un lote PROPIOS ajeno o de un vehículo sin terminar se omite sin revelarse y solo suma a <c>Omitidos</c>.
    /// </summary>
    public async Task<PanelesDeuda> VariosAsync(IReadOnlyList<Guid> vehiculoIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vehiculoIds);
        var visibles = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>().AsNoTracking(), db, usuario, ct).ConfigureAwait(false);
        var ids = vehiculoIds.ToArray();
        var filas = await (
                from v in db.Set<VehiculoDiagnostico>().AsNoTracking()
                join c in visibles on new { v.TenantId, v.DiagnosticoId } equals new { c.TenantId, DiagnosticoId = c.Id }
                where ids.Contains(v.Id)
                select new { v.Id, Cabecera = c })
            .ToListAsync(ct).ConfigureAwait(false);
        var visiblesIds = filas.Select(f => f.Id).ToArray();
        var terminados = (await db.Set<CeldaConsulta>().AsNoTracking()
                .Where(c => visiblesIds.Contains(c.VehiculoId))
                .Select(c => new { c.VehiculoId, c.Estado })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.VehiculoId)
            .Where(g => AvanceVehiculo.Terminado(g.Select(c => c.Estado)))
            .Select(g => g.Key)
            .ToHashSet();

        var porId = filas.Where(f => terminados.Contains(f.Id)).ToDictionary(f => f.Id, f => f.Cabecera);
        var paneles = new List<PanelDeudaDto>(porId.Count);
        foreach (var id in ids)
        {
            if (porId.TryGetValue(id, out var cabecera))
                paneles.Add(await PanelAsync(cabecera, id, ct).ConfigureAwait(false));
        }

        return new PanelesDeuda(paneles, ids.Length - paneles.Count);
    }

    private async Task<PanelDeudaDto> PanelAsync(CabeceraDiagnostico cabecera, Guid vehiculoId, CancellationToken ct)
    {
        var contenido = await lector.VehiculoAsync(cabecera, vehiculoId, usuario.EmpresaNombre, ct).ConfigureAwait(false);
        var tenant = cabecera.TenantId;
        var dictamen = cabecera.Modo == ModoDiagnostico.ConAnalisis
            ? await db.Set<DictamenVehiculo>().AsNoTracking()
                .SingleOrDefaultAsync(d => d.VehiculoId == vehiculoId && d.TenantId == tenant && d.Vigente, ct).ConfigureAwait(false)
            : null;
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(VigenciaDelDia.OffsetColombia).DateTime);
        var saneamiento = await SaneamientoAsync(tenant, vehiculoId, ct).ConfigureAwait(false);
        return Armar(vehiculoId, contenido, cabecera.Modo, dictamen, saneamiento, hoy, cabecera.TipoTramiteCodigo) with
        {
            DiagnosticoNombre = cabecera.Nombre,
            DiagnosticoCreadoEn = cabecera.CreadoEn,
        };
    }

    /// <summary>
    /// El saneamiento no anulado del vehículo con sus renglones vigentes (sin la referencia, el nombre ni el sha de la evidencia: solo si
    /// la tiene); null si no hay.
    /// </summary>
    private async Task<SaneamientoDeudaDto?> SaneamientoAsync(Guid tenant, Guid vehiculoId, CancellationToken ct)
    {
        var vigente = await db.Set<Saneamiento>().AsNoTracking()
            .Where(s => s.TenantId == tenant && s.VehiculoId == vehiculoId && s.Estado != EstadoSaneamiento.Anulado)
            .Select(s => new { s.Id, s.Estado })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (vigente is null)
            return null;
        var renglones = await db.Set<RenglonSaneamiento>().AsNoTracking()
            .Where(r => r.TenantId == tenant && r.SaneamientoId == vigente.Id && r.QuitadoEn == null)
            .OrderBy(r => r.CreadoEn).ThenBy(r => r.Id)
            .Select(r => new { r.Fuente, r.Hallazgo, r.Resultado, r.Descripcion, r.Nota, ConEvidencia = r.EvidenciaReferencia != null })
            .ToListAsync(ct).ConfigureAwait(false);
        return new SaneamientoDeudaDto(CodigosDeEnum.Codigo(vigente.Estado))
        {
            Renglones = [.. renglones.Select(r => new RenglonDeudaDto(
                CodigosDeEnum.Codigo(r.Fuente), r.Hallazgo is { } h ? CodigosDeEnum.Codigo(h) : string.Empty, CodigosDeEnum.Codigo(r.Resultado),
                r.Descripcion, r.Nota, r.ConEvidencia))],
        };
    }

    /// <summary>
    /// El panel a partir del contenido del reporte del vehículo (función pura). Fuentes, en este orden y solo si están en el alcance:
    /// SIMIT_VEHICULO, SIMIT_PROPIETARIO y SIMIT_LOCATARIO (una entrada por persona), IMPUESTO, y SOAT y RTM (con el RUNT).
    /// <paramref name="dictamen"/> se ignora fuera de Con Análisis.
    /// </summary>
    public static PanelDeudaDto Armar(
        Guid vehiculoId, ContenidoReporteVehiculo contenido, ModoDiagnostico modo, DictamenVehiculo? dictamen, SaneamientoDeudaDto? saneamiento,
        DateOnly hoy, string? tipoTramite = null)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var tipo = string.IsNullOrWhiteSpace(tipoTramite) ? TipoTramitePorDefecto : tipoTramite;
        var secciones = contenido.Secciones.GroupBy(s => s.Fuente, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        SeccionReporte? De(Fuente fuente) => secciones.GetValueOrDefault(CodigosDeEnum.Codigo(fuente));
        var runtSeccion = De(Fuente.Runt);
        var runt = HechosRunt(runtSeccion, contenido.DetalleRunt, tipo, hoy);

        var fuentes = new List<FuenteDeudaDto>();
        if (De(Fuente.SimitVehiculo) is { } simitVehiculo)
            fuentes.Add(SimitDelVehiculo(simitVehiculo, tipo, hoy));
        if (De(Fuente.SimitPropietario) is { } propietario)
            fuentes.AddRange(SimitDePersonas(FuentesDeuda.SimitPropietario, propietario, tipo, hoy));
        if (De(Fuente.SimitLocatario) is { } locatario)
        {
            // C4: en leasing sin documento del locatario la consulta exigida no se pudo hacer.
            if (PredicadosDictamen.SimitLocatarioSinVerificar(runt))
                fuentes.Add(new FuenteDeudaDto(FuentesDeuda.SimitLocatario, EstadosDeuda.SinVerificar, locatario.DatoObtenidoEn, locatario.MotivoNa));
            else
                fuentes.AddRange(SimitDePersonas(FuentesDeuda.SimitLocatario, locatario, tipo, hoy));
        }

        if (De(Fuente.ImpuestoDepartamental) is { } impuesto)
            fuentes.Add(Impuesto(impuesto, tipo, hoy));
        if (runtSeccion is not null)
        {
            fuentes.Add(Soat(runtSeccion, runt, contenido.DetalleRunt));
            fuentes.Add(Rtm(runtSeccion, runt, contenido.DetalleRunt, hoy));
        }

        var encabezado = contenido.Encabezado;
        return new PanelDeudaDto(
            new VehiculoDeudaDto(vehiculoId, encabezado.Placa ?? encabezado.Vin ?? encabezado.Identificador),
            CodigosDeEnum.Codigo(modo),
            modo == ModoDiagnostico.ConAnalisis && dictamen is not null ? Dictamen(dictamen) : null,
            fuentes,
            saneamiento);
    }

    private static DictamenDeudaDto Dictamen(DictamenVehiculo fila)
    {
        var compania = ConsultaDictamen.Compania(fila);
        return new DictamenDeudaDto(compania.Veredicto, compania.CatalogoVersion, [.. compania.Alertamientos.Select(a => new MotivoDeudaDto(a.Codigo, a.Efecto, a.Texto))]);
    }

    private static FuenteDeudaDto SimitDelVehiculo(SeccionReporte seccion, string tipo, DateOnly hoy) => seccion.Estado switch
    {
        EstadoSeccion.Completado when seccion.Resultado is ResultadoSimitDetalle simit => Simit(FuentesDeuda.SimitVehiculo, simit, seccion.DatoObtenidoEn, null, tipo, hoy),
        EstadoSeccion.NoAplica => new FuenteDeudaDto(FuentesDeuda.SimitVehiculo, EstadosDeuda.NoAplica, MotivoNa: seccion.MotivoNa),
        _ => new FuenteDeudaDto(FuentesDeuda.SimitVehiculo, EstadosDeuda.SinVerificar, seccion.DatoObtenidoEn),
    };

    /// <summary>Una entrada por persona; sin personas consultadas, una sola SIN_VERIFICAR (como la lista vacía del lector de hechos).</summary>
    private static IEnumerable<FuenteDeudaDto> SimitDePersonas(string fuente, SeccionReporte seccion, string tipo, DateOnly hoy)
    {
        if (seccion.Estado == EstadoSeccion.NoAplica)
        {
            yield return new FuenteDeudaDto(fuente, EstadosDeuda.NoAplica, MotivoNa: seccion.MotivoNa);
            yield break;
        }

        var personas = seccion.Personas ?? [];
        if (seccion.Estado is not (EstadoSeccion.Completado or EstadoSeccion.NoDisponible) || personas.Count == 0)
        {
            yield return new FuenteDeudaDto(fuente, EstadosDeuda.SinVerificar, seccion.DatoObtenidoEn);
            yield break;
        }

        foreach (var p in personas)
        {
            var persona = new PersonaDeudaDto(p.TipoDocumento, p.Documento ?? p.DocumentoEnmascarado, p.DocumentoEnmascarado);
            var consultado = p.ConsultadoEn ?? seccion.DatoObtenidoEn;
            yield return p is { Estado: EstadoPersonaReporte.Ok, Resultado: ResultadoSimitDetalle simit }
                ? Simit(fuente, simit, consultado, persona, tipo, hoy)
                : new FuenteDeudaDto(fuente, EstadosDeuda.SinVerificar, consultado, Persona: persona);
        }
    }

    private static FuenteDeudaDto Simit(string fuente, ResultadoSimitDetalle simit, DateTimeOffset? consultado, PersonaDeudaDto? persona, string tipo, DateOnly hoy)
    {
        var hechos = LectorHechosDictamen.Leer(new PedidoLectura(
            tipo, hoy, null, null, new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Completado, [simit]), null, null, null, null, null)).SimitVehiculo;
        var deuda = hechos is not null && (PredicadosDictamen.TieneMultas(hechos) || PredicadosDictamen.TieneAcuerdoConSaldo(hechos));
        return new FuenteDeudaDto(
            fuente, deuda ? EstadosDeuda.ConDeuda : EstadosDeuda.SinHallazgo, consultado,
            Persona: persona,
            PazYSalvo: simit.PazYSalvo,
            Comparendos: [.. (simit.Comparendos ?? []).Select(c => new ComparendoDeudaDto(c.Numero, c.Fecha, c.Valor, c.Estado))],
            ComparendosTruncados: simit.ComparendosTruncados,
            AcuerdosPago: [.. (simit.Acuerdos ?? []).Select(a => new AcuerdoDeudaDto(a.Estado, a.Pendiente))]);
    }

    private static FuenteDeudaDto Impuesto(SeccionReporte seccion, string tipo, DateOnly hoy)
    {
        if (seccion.Estado == EstadoSeccion.NoAplica)
            return new FuenteDeudaDto(FuentesDeuda.Impuesto, EstadosDeuda.NoAplica, MotivoNa: seccion.MotivoNa);
        if (seccion is not { Estado: EstadoSeccion.Completado, Resultado: ResultadoImpuestoDetalle impuesto })
            return new FuenteDeudaDto(FuentesDeuda.Impuesto, EstadosDeuda.SinVerificar, seccion.DatoObtenidoEn);
        var hechos = LectorHechosDictamen.Leer(new PedidoLectura(
            tipo, hoy, null, null, null, null, null, null, null, new LecturaCelda<ResultadoImpuestoDetalle>(EstadoCelda.Completado, impuesto)));
        var deuda = PredicadosDictamen.ImpuestoConDeuda(hechos);
        return new FuenteDeudaDto(
            FuentesDeuda.Impuesto, deuda ? EstadosDeuda.ConDeuda : EstadosDeuda.SinHallazgo, seccion.DatoObtenidoEn,
            MontoTotal: impuesto.MontoTotal,
            Vigencias: [.. (impuesto.Vigencias ?? []).Select(v => new VigenciaDeudaDto(v.Anio, v.Valor, v.Pagada))]);
    }

    /// <summary>SOAT: cubierto (VIGENTE o NO REQUIERE) sin hallazgo; si no, NO_VIGENTE salvo el remolque, que no lo exige (C4).</summary>
    private static FuenteDeudaDto Soat(SeccionReporte runtSeccion, HechosRuntVehiculo? runt, DetalleRuntReporte? detalle)
    {
        if (runt is null || detalle is null)
            return SinRunt(FuentesDeuda.Soat, runtSeccion);
        var vence = (detalle.Soat.FirstOrDefault(s => Cubre(s.Estado)) ?? (detalle.Soat.Count > 0 ? detalle.Soat[0] : null))?.FechaVencimiento;
        var estado = PredicadosDictamen.SoatNoVigente(runt) ? EstadosDeuda.NoVigente
            : !PredicadosDictamen.Cubre(runt.EstadosSoat) ? EstadosDeuda.NoAplica
            : EstadosDeuda.SinHallazgo;
        return new FuenteDeudaDto(FuentesDeuda.Soat, estado, runtSeccion.DatoObtenidoEn, Vence: vence);
    }

    /// <summary>RTM: no aplica a maquinaria ni remolque; aún no exigible o cubierta, sin hallazgo; si no, NO_VIGENTE (C4).</summary>
    private static FuenteDeudaDto Rtm(SeccionReporte runtSeccion, HechosRuntVehiculo? runt, DetalleRuntReporte? detalle, DateOnly hoy)
    {
        if (runt is null || detalle is null)
            return SinRunt(FuentesDeuda.Rtm, runtSeccion);
        var vence = (detalle.Rtm.FirstOrDefault(r => Cubre(r.Estado)) ?? (detalle.Rtm.Count > 0 ? detalle.Rtm[0] : null))?.FechaVigencia;
        var estado = ClasificadorVehiculoDictamen.AniosPrimeraRtm(runt.Clase, runt.Servicio) is null ? EstadosDeuda.NoAplica
            : PredicadosDictamen.RtmNoVigente(runt, hoy) ? EstadosDeuda.NoVigente
            : EstadosDeuda.SinHallazgo;
        return new FuenteDeudaDto(FuentesDeuda.Rtm, estado, runtSeccion.DatoObtenidoEn, Vence: vence);
    }

    private static FuenteDeudaDto SinRunt(string fuente, SeccionReporte runt) =>
        runt.Estado == EstadoSeccion.NoAplica
            ? new FuenteDeudaDto(fuente, EstadosDeuda.NoAplica, MotivoNa: runt.MotivoNa)
            : new FuenteDeudaDto(fuente, EstadosDeuda.SinVerificar, runt.DatoObtenidoEn);

    /// <summary>Hechos del RUNT del vehículo con el mismo lector del dictamen; null sin RUNT completado con detalle.</summary>
    private static HechosRuntVehiculo? HechosRunt(SeccionReporte? seccion, DetalleRuntReporte? detalle, string tipo, DateOnly hoy)
    {
        if (seccion is not { Estado: EstadoSeccion.Completado } || detalle is null)
            return null;
        return LectorHechosDictamen.Leer(new PedidoLectura(
            tipo, hoy,
            new LecturaCelda<DetalleRuntReporte>(EstadoCelda.Completado, detalle),
            new LecturaCelda<VehiculoRunt>(EstadoCelda.Completado, Normalizado(seccion.Resultado as ResultadoRuntDetalle)),
            null, null, null, null, null, null)).Runt;
    }

    /// <summary>
    /// El RUNT normalizado que espera el lector, desde la proyección del RUNT (tipo de propiedad y cantidad de locatarios); el mismo
    /// mapeo de <c>EmisorDictamen</c>: un locatario informado cuenta como documento disponible.
    /// </summary>
    private static VehiculoRunt? Normalizado(ResultadoRuntDetalle? detalle)
    {
        if (detalle is null)
            return null;
        var propietarios = (detalle.Propietarios ?? []).Select(p => new PropietarioRunt(
            p.TipoDocumento ?? string.Empty,
            string.Empty,
            CodigosDeEnum.TryLeer<TipoPropiedad>(p.TipoPropiedad, out var tipo) ? tipo : TipoPropiedad.Desconocido,
            [])).ToArray();
        var locatarios = detalle.Locatarios > 0 ? new[] { new LocatarioRunt(string.Empty, "1") } : Array.Empty<LocatarioRunt>();
        return new VehiculoRunt(detalle.Placa, detalle.Vin, detalle.Servicio, detalle.Clase, detalle.Organismo, propietarios, locatarios, string.Empty, null);
    }

    /// <summary>La póliza o revisión cubre (mismo criterio de <see cref="PredicadosDictamen.Cubre"/>), para elegir la fecha que se muestra.</summary>
    private static bool Cubre(string? estado) => PredicadosDictamen.Cubre([estado]);
}

/// <summary>Los paneles del Excel de varias placas y cuántos ids pedidos se omitieron (no visibles, inexistentes o sin terminar).</summary>
internal sealed record PanelesDeuda(IReadOnlyList<PanelDeudaDto> Paneles, int Omitidos);
