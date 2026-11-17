using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>Vehículo a solicitar, con la empresa y el lote ya resueltos por quien llama (visibilidad incluida).</summary>
internal sealed record VehiculoParaSolicitar(Guid TenantId, Guid DiagnosticoId, Guid VehiculoId);

/// <summary>Lo que dejó <see cref="EscritorSaneamiento.SolicitarAsync"/>: los saneamientos nuevos y los vehículos que ya tenían uno no anulado.</summary>
internal sealed record SolicitudesAplicadas(IReadOnlyList<Saneamiento> Creados, IReadOnlyList<Guid> YaSolicitados);

/// <summary>
/// Una transición aplicada (sin guardar): el saneamiento, el evento del historial y, si la acción es de FLIT y avisa al solicitante,
/// el tipo del aviso. Es el gancho de los avisos agrupados (BE-B): <see cref="Evento"/>.<c>Id</c> es el <c>referencia_id</c> del aviso
/// (C18, SP-22) y el destinatario es <see cref="Saneamiento.SolicitadoPor"/>, con <c>datos</c> de solo cantidad y resultado (SP-23).
/// </summary>
internal sealed record TransicionAplicada(Saneamiento Saneamiento, EventoSaneamiento Evento, string? TipoAviso);

/// <summary>
/// Único punto de escritura del saneamiento (HU #14576, plan §4.4 y §8, schema.md §6, SP-3): cada transición deja en el contexto, sin
/// guardar, el estado nuevo, su evento en <c>saneamiento_historial</c>, la copia <c>vehiculos.saneamiento_estado</c> y la versión de
/// fila del tablero, para que la grilla incremental recoja la fila. Ningún otro código escribe esas columnas ni el historial.
/// <list type="bullet">
/// <item>No guarda ni abre el ámbito de escritura entre empresas: quien llama abre la transacción (las versiones y la columna van con
/// <c>ExecuteUpdate</c>, que corre en el acto), guarda y confirma. La compañía guarda con su contexto; el backoffice pasa las filas de
/// la empresa del lote en <c>cabeceras</c> y <c>vehiculos</c> y guarda con <c>GuardarDeLaEmpresaAsync(empresa, TiposDelSaneamiento)</c>.</item>
/// <item>Orden de bloqueo canónico: cabecera → vehículo → saneamiento; el masivo bloquea las cabeceras en orden de <c>diagnostico_id</c>
/// (C19). La columna del vehículo se escribe sin cargarlo (no choca con su <c>xmin</c>).</item>
/// <item>Sin borrados físicos (SP-2): el renglón se quita con <c>quitado_en</c>.</item>
/// </list>
/// </summary>
internal static class EscritorSaneamiento
{
    public const string CodigoTransicionInvalida = "TRANSICION_INVALIDA";

    /// <summary>Tipos que escribe el saneamiento, lista cerrada (SP-3): sin cabecera, dictamen ni archivos.</summary>
    public static readonly IReadOnlySet<Type> TiposDelSaneamiento = new HashSet<Type>
    {
        typeof(Saneamiento), typeof(RenglonSaneamiento), typeof(EventoSaneamiento), typeof(VehiculoDiagnostico), typeof(Notificacion),
    };

    /// <summary>
    /// Solicitudes de la compañía (plan §3.1, §6.1). Por lote, en orden de <c>diagnostico_id</c>: sube la versión del lote (bloquea su
    /// cabecera hasta el commit), relee dentro de la transacción qué vehículos ya tienen un saneamiento no anulado (esos quedan en
    /// <see cref="SolicitudesAplicadas.YaSolicitados"/>, sin escribir nada de ellos: así la segunda de dos marcas simultáneas sale como
    /// YA_SOLICITADO), agrega cada saneamiento SOLICITADO con su evento y marca las filas con la columna y la versión en un solo
    /// <c>ExecuteUpdate</c>. La elegibilidad (terminado, visibilidad) ya la resolvió quien llama. No guarda.
    /// </summary>
    public static async Task<SolicitudesAplicadas> SolicitarAsync(
        DiagnosticoDb db, IReadOnlyList<VehiculoParaSolicitar> vehiculos, Guid usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(vehiculos);
        var creados = new List<Saneamiento>();
        var yaSolicitados = new List<Guid>();
        foreach (var lote in vehiculos.GroupBy(v => (v.TenantId, v.DiagnosticoId)).OrderBy(g => g.Key.DiagnosticoId))
        {
            var (tenant, diagnostico) = lote.Key;
            var ids = lote.Select(v => v.VehiculoId).Distinct().ToList();
            var version = await EscritorCeldas.SubirVersionTableroAsync(db, diagnostico, ct).ConfigureAwait(false);
            var vigentes = (await db.Set<Saneamiento>()
                    .Where(s => s.TenantId == tenant && ids.Contains(s.VehiculoId) && s.Estado != EstadoSaneamiento.Anulado)
                    .Select(s => s.VehiculoId)
                    .ToListAsync(ct).ConfigureAwait(false))
                .ToHashSet();
            yaSolicitados.AddRange(ids.Where(vigentes.Contains));
            var nuevos = ids.Where(id => !vigentes.Contains(id)).ToList();
            if (nuevos.Count == 0)
                continue;

            var transicion = TransicionesSaneamiento.Resolver(null, AccionSaneamiento.Solicitar, ActorSaneamiento.Compania)!;
            foreach (var vehiculo in nuevos)
            {
                var saneamiento = new Saneamiento
                {
                    Id = Guid.CreateVersion7(ahora),
                    TenantId = tenant,
                    DiagnosticoId = diagnostico,
                    VehiculoId = vehiculo,
                    Estado = transicion.Hasta,
                    SolicitadoPor = usuarioId,
                    SolicitadoEn = ahora,
                };
                db.Add(saneamiento);
                db.Add(NuevoEvento(saneamiento, transicion.Evento, null, transicion.Hasta, usuarioId, false, null, ahora));
                creados.Add(saneamiento);
            }

            var estado = transicion.Hasta;
            await db.Set<VehiculoDiagnostico>()
                .Where(v => v.TenantId == tenant && v.DiagnosticoId == diagnostico && nuevos.Contains(v.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.VersionTablero, version).SetProperty(v => v.SaneamientoEstado, estado), ct)
                .ConfigureAwait(false);
        }

        return new SolicitudesAplicadas(creados, yaSolicitados);
    }

    /// <summary>
    /// Aplica una transición de §3.2 a <paramref name="saneamiento"/> (seguido por el contexto, con su <c>xmin</c>): 409
    /// <see cref="CodigoTransicionInvalida"/> fuera de la tabla o con el actor equivocado; 422 <c>MOTIVO_REQUERIDO</c> o
    /// <c>TEXTO_INVALIDO</c> (campo <c>motivo</c>, mensaje fijo) si el motivo exigido no sirve; 422 <c>CIERRE_INCOMPLETO</c> al cerrar
    /// SANEADO sin cumplir <see cref="ReglasCierre"/> con <paramref name="renglones"/> (los resultados de los renglones no quitados). En
    /// la primera toma crea un renglón automático por cada <paramref name="hallazgos"/>; al reabrir no regenera. Luego sube la versión
    /// de fila, escribe la columna del vehículo y agrega el evento. No guarda. El backoffice pasa las filas de la empresa del lote en
    /// <paramref name="cabeceras"/> y <paramref name="vehiculos"/>.
    /// </summary>
    public static async Task<TransicionAplicada> TransicionarAsync(
        DiagnosticoDb db, Saneamiento saneamiento, AccionSaneamiento accion, ActorSaneamiento actor, Guid usuarioId, string? motivo,
        DateTimeOffset ahora, CancellationToken ct, IReadOnlyCollection<ResultadoRenglonSaneamiento>? renglones = null,
        IReadOnlyList<HallazgoFuente>? hallazgos = null, IQueryable<CabeceraDiagnostico>? cabeceras = null, IQueryable<VehiculoDiagnostico>? vehiculos = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(saneamiento);
        var transicion = TransicionesSaneamiento.Resolver(saneamiento.Estado, accion, actor)
            ?? throw ReglaException.Conflicto(CodigoTransicionInvalida, "La acción no es válida en el estado actual del saneamiento.");
        if (transicion.ExigeMotivo && TextoLibreSaneamiento.CodigoMotivo(motivo) is { } codigo)
            throw new ReglaException(codigo, "El motivo debe tener entre 10 y 500 caracteres, sin espacios al inicio o al final ni caracteres de control.") { Campo = "motivo" };
        if (accion == AccionSaneamiento.CerrarSaneado
            && ReglasCierre.ErrorCierreSaneado(renglones ?? throw new ArgumentNullException(nameof(renglones), "Cerrar como SANEADO exige los renglones.")) is { } cierre)
            throw new ReglaException(cierre, "Para cerrar como saneado todos los renglones deben estar resueltos y al menos uno saneado.");

        var anterior = saneamiento.Estado;
        var primeraToma = accion == AccionSaneamiento.Tomar && saneamiento.TomadoEn is null;
        Aplicar(saneamiento, transicion, usuarioId, motivo, ahora);
        if (primeraToma && hallazgos is not null)
        {
            foreach (var hallazgo in hallazgos)
            {
                db.Add(new RenglonSaneamiento
                {
                    Id = Guid.CreateVersion7(ahora),
                    TenantId = saneamiento.TenantId,
                    SaneamientoId = saneamiento.Id,
                    Fuente = hallazgo.Fuente,
                    Hallazgo = hallazgo.Hallazgo,
                    Automatico = true,
                    Resultado = ResultadoRenglonSaneamiento.Pendiente,
                    CreadoPor = usuarioId,
                    CreadoEn = ahora,
                });
            }
        }

        await MarcarVehiculoAsync(db, saneamiento, cabeceras, vehiculos, ct).ConfigureAwait(false);
        var evento = NuevoEvento(
            saneamiento, transicion.Evento, anterior, transicion.Hasta, usuarioId, actor == ActorSaneamiento.Flit,
            transicion.ExigeMotivo ? motivo : null, ahora);
        db.Add(evento);
        return new TransicionAplicada(saneamiento, evento, TipoAviso(transicion.Evento));
    }

    /// <summary>
    /// Evento de edición de FLIT sin transición (renglón agregado, quitado o actualizado, evidencia cargada o quitada, observación
    /// editada) para BE-B. Los de renglón exigen <paramref name="renglon"/>; copia su fuente. No guarda.
    /// </summary>
    public static EventoSaneamiento RegistrarEdicion(
        DiagnosticoDb db, Saneamiento saneamiento, TipoEventoSaneamiento evento, Guid usuarioId, DateTimeOffset ahora, RenglonSaneamiento? renglon = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(saneamiento);
        var deRenglon = evento is TipoEventoSaneamiento.RenglonAgregado or TipoEventoSaneamiento.RenglonQuitado or TipoEventoSaneamiento.RenglonActualizado
            or TipoEventoSaneamiento.EvidenciaCargada or TipoEventoSaneamiento.EvidenciaQuitada;
        if (!deRenglon && evento != TipoEventoSaneamiento.ObservacionEditada)
            throw new ArgumentOutOfRangeException(nameof(evento), evento, "Las transiciones van por TransicionarAsync.");
        if (deRenglon != renglon is not null)
            throw new ArgumentException("Los eventos de renglón llevan el renglón; los demás, no.", nameof(renglon));
        var fila = new EventoSaneamiento
        {
            Id = Guid.CreateVersion7(ahora),
            TenantId = saneamiento.TenantId,
            SaneamientoId = saneamiento.Id,
            Evento = evento,
            RenglonId = renglon?.Id,
            Fuente = renglon?.Fuente,
            UsuarioId = usuarioId,
            EsFlit = true,
            OcurridoEn = ahora,
        };
        db.Add(fila);
        return fila;
    }

    /// <summary>Tipo del aviso al solicitante para un evento de FLIT (§4.5); null si el evento no avisa.</summary>
    public static string? TipoAviso(TipoEventoSaneamiento evento) => evento switch
    {
        TipoEventoSaneamiento.Tomado => Notificacion.SaneamientoTomado,
        TipoEventoSaneamiento.CerradoSaneado or TipoEventoSaneamiento.CerradoNoProcede => Notificacion.SaneamientoCerrado,
        TipoEventoSaneamiento.Reabierto => Notificacion.SaneamientoReabierto,
        TipoEventoSaneamiento.AnuladoFlit => Notificacion.SaneamientoAnulado,
        _ => null,
    };

    private static void Aplicar(Saneamiento saneamiento, TransicionSaneamiento transicion, Guid usuarioId, string? motivo, DateTimeOffset ahora)
    {
        saneamiento.Estado = transicion.Hasta;
        switch (transicion.Accion)
        {
            case AccionSaneamiento.Desmarcar:
                saneamiento.AnuladoPor = usuarioId;
                saneamiento.AnuladoEn = ahora;
                saneamiento.AnuladoPorFlit = false;
                saneamiento.MotivoAnulacion = null;
                break;
            case AccionSaneamiento.AnularFlit:
                saneamiento.AnuladoPor = usuarioId;
                saneamiento.AnuladoEn = ahora;
                saneamiento.AnuladoPorFlit = true;
                saneamiento.MotivoAnulacion = motivo;
                break;
            case AccionSaneamiento.Tomar:
                if (saneamiento.TomadoEn is null)
                {
                    saneamiento.TomadoPor = usuarioId;
                    saneamiento.TomadoEn = ahora;
                }

                break;
            case AccionSaneamiento.CerrarSaneado:
                saneamiento.CerradoPor = usuarioId;
                saneamiento.CerradoEn = ahora;
                break;
            case AccionSaneamiento.CerrarNoProcede:
                saneamiento.CerradoPor = usuarioId;
                saneamiento.CerradoEn = ahora;
                saneamiento.MotivoCierre = motivo;
                break;
            case AccionSaneamiento.Reabrir:
                // El motivo del cierre queda en el historial; tomado_* no cambia (§3.1 de schema.md).
                saneamiento.CerradoPor = null;
                saneamiento.CerradoEn = null;
                saneamiento.MotivoCierre = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transicion), transicion.Accion, "La solicitud va por SolicitarAsync.");
        }
    }

    /// <summary>Versión de fila (cabecera → vehículo) y la copia del estado en el vehículo (null si quedó ANULADO), sin cargarlo.</summary>
    private static async Task MarcarVehiculoAsync(
        DiagnosticoDb db, Saneamiento saneamiento, IQueryable<CabeceraDiagnostico>? cabeceras, IQueryable<VehiculoDiagnostico>? vehiculos, CancellationToken ct)
    {
        await EscritorCeldas.SubirVersionFilaAsync(db, saneamiento.DiagnosticoId, saneamiento.VehiculoId, ct, cabeceras, vehiculos).ConfigureAwait(false);
        EstadoSaneamiento? copia = saneamiento.Estado == EstadoSaneamiento.Anulado ? null : saneamiento.Estado;
        var seguido = db.ChangeTracker.Entries<VehiculoDiagnostico>().FirstOrDefault(e => e.Entity.Id == saneamiento.VehiculoId);
        if (seguido is not null)
        {
            seguido.Entity.SaneamientoEstado = copia;
            return;
        }

        var (tenant, vehiculo) = (saneamiento.TenantId, saneamiento.VehiculoId);
        await (vehiculos ?? db.Set<VehiculoDiagnostico>())
            .Where(v => v.TenantId == tenant && v.Id == vehiculo)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.SaneamientoEstado, copia), ct)
            .ConfigureAwait(false);
    }

    private static EventoSaneamiento NuevoEvento(
        Saneamiento saneamiento, TipoEventoSaneamiento evento, EstadoSaneamiento? anterior, EstadoSaneamiento nuevo, Guid usuarioId, bool esFlit,
        string? motivo, DateTimeOffset ahora) => new()
        {
            Id = Guid.CreateVersion7(ahora),
            TenantId = saneamiento.TenantId,
            SaneamientoId = saneamiento.Id,
            Evento = evento,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            UsuarioId = usuarioId,
            EsFlit = esFlit,
            Motivo = motivo,
            OcurridoEn = ahora,
        };
}
