using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Contraventor;

/// <summary>Conductor de un comparendo y sus documentos (HU #13478, §7.4).</summary>
public static class ContraventorEndpoints
{
    public sealed record Asignacion(string TipoDocumento, string NumeroDocumento, string Nombres, string? Correo, string? Telefono, string? Direccion,
        string? Ciudad, string? Sucursal, string? TipoContrato, string? Motivo, bool DeclaracionTratamientoDatos);

    public sealed record NotificacionFisica(bool Valor);

    public sealed record DocumentoConductor(string TipoDocumento, string NumeroDocumento);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPut("/comparendos/{id:guid}/contraventor", Asignar).RequirePermiso(Permisos.ContraventorAsignar);
        api.MapPost("/comparendos/{id:guid}/contraventor/consultar", Consultar).RequirePermiso(Permisos.ContraventorConsultar);
        api.MapPut("/comparendos/{id:guid}/contraventor/documento", CompletarDocumento).RequirePermiso(Permisos.ContraventorAsignar);
        api.MapPost("/comparendos/{id:guid}/documentos", SubirDocumento).RequirePermiso(Permisos.ContraventorDocumentos).DisableAntiforgery();
        api.MapPut("/comparendos/{id:guid}/notificado-fisicamente", MarcarNotificado).RequirePermiso(Permisos.ContraventorAsignar);
    }

    private static async Task<IResult> Asignar(Guid id, Asignacion a, AccesoComparendo acceso, ServicioContraventor servicio, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        servicio.Asignar(c, new DatosConductor(a.TipoDocumento, a.NumeroDocumento, a.Nombres, a.Correo, a.Telefono, a.Direccion, a.Ciudad, a.Sucursal, a.TipoContrato),
            OrigenContraventor.Manual, Actor.De(usuario), a.Motivo, a.DeclaracionTratamientoDatos);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> CompletarDocumento(Guid id, DocumentoConductor d, AccesoComparendo acceso, ServicioContraventor servicio, UsuarioActual usuario,
        ComparendosDb db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(d);
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        servicio.CompletarDocumento(c, d.TipoDocumento, d.NumeroDocumento, Actor.De(usuario));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Consulta manual a la API de contratos. Si encuentra otro conductor y ya hay uno, pide confirmar el reemplazo.</summary>
    private static async Task<IResult> Consultar(Guid id, bool? confirmarReemplazo, AccesoComparendo acceso, ServicioContraventor servicio, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var r = await servicio.ConsultarAsync(c, Actor.De(usuario), confirmarReemplazo == true, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var pendienteConfirmar = r.Resultado == ResultadoContrato.Encontrado && c.Contraventor is { } k && r.Conductor is { } nuevo
            && (k.NumeroDocumento != nuevo.NumeroDocumento || !string.Equals(k.Nombres, nuevo.Nombres, StringComparison.OrdinalIgnoreCase));
        return Results.Ok(new { resultado = r.Resultado.ToString(), r.Mensaje, conductor = r.Conductor, requiereConfirmarReemplazo = pendienteConfirmar });
    }

    private static async Task<IResult> SubirDocumento(Guid id, string tipo, IFormFile archivo, AccesoComparendo acceso, RegistroArchivos archivos, Timeline timeline,
        UsuarioActual usuario, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoDocumentoSoporte>(tipo, true, out var t) || t is TipoDocumentoSoporte.RespuestaOt or TipoDocumentoSoporte.SoporteAceptacionOt or TipoDocumentoSoporte.ComprobantePago)
            throw new ReglaException("TIPO_DOCUMENTO_INVALIDO", "Elige el tipo: Cedula, Licencia u Otro.");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var archivoId = await archivos.SubirAsync(c.TenantId, "documento_conductor", archivo, RegistroArchivos.Documentos, ct, usuario.Id).ConfigureAwait(false);
        if (t != TipoDocumentoSoporte.Otro)
        {
            await db.DocumentosSoporte.Where(d => d.ComparendoId == id && d.Tipo == t && d.Vigente)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.Vigente, false), ct).ConfigureAwait(false);
        }

        var doc = new DocumentoSoporte { Id = Guid.CreateVersion7(), TenantId = c.TenantId, ComparendoId = id, Tipo = t, ArchivoId = archivoId, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.DocumentosSoporte.Add(doc);
        timeline.Agregar(c, TipoEventoTimeline.DocumentoConductorCargado, $"Se cargó {Nombre(t)} ({archivo.FileName}).", Actor.De(usuario), relacionadoTipo: "documento_soporte", relacionadoId: doc.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { doc.Id, archivoId });
    }

    /// <summary>El conductor ya fue notificado en persona: sale de los avisos automáticos (S-21).</summary>
    private static async Task<IResult> MarcarNotificado(Guid id, NotificacionFisica pedido, AccesoComparendo acceso, Timeline timeline, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        if (c.NotificadoFisicamente != pedido.Valor)
        {
            c.NotificadoFisicamente = pedido.Valor;
            timeline.Agregar(c, TipoEventoTimeline.NotaManual, pedido.Valor ? "Se marcó que el conductor fue notificado en persona: no recibe avisos automáticos." : "Se quitó la marca de notificación en persona.", Actor.De(usuario));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    public static string Nombre(TipoDocumentoSoporte t) => t switch
    {
        TipoDocumentoSoporte.Cedula => "la cédula del conductor",
        TipoDocumentoSoporte.Licencia => "la licencia de conducción",
        _ => "un documento",
    };
}

/// <summary>Trabajo diario: reintenta la consulta de contraventor de lo pendiente o con error, hasta 3 intentos (S-35).</summary>
public sealed partial class ReintentoContraventor(ComparendosDb db, ServicioContraventor servicio, IProductosHabilitados productos, ILogger<ReintentoContraventor> logger) : IEjecutorTrabajo
{
    public bool Atiende(TipoTrabajo tipo) => tipo == TipoTrabajo.ReintentoContraventor;

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking()
            .Where(e => e.AllowsContravenerConsultation && e.TipoIntegracionConductor == TipoIntegracionConductor.ApiRenting)
            .Where(e => solicitud.TenantId == null || e.TenantId == solicitud.TenantId).Select(e => e.TenantId).ToListAsync(ct).ConfigureAwait(false);
        var habilitadas = new List<Guid>();
        foreach (var e in empresas)
        {
            if (await productos.ComparendosHabilitadoAsync(e, ct).ConfigureAwait(false))
                habilitadas.Add(e);
        }

        empresas = habilitadas;
        var ids = await db.Comparendos.AsNoTracking()
            .Where(c => empresas.Contains(c.TenantId) && c.Activo && c.Contraventor == null && c.IntentosConsultaContraventor < 3
                && (c.EstadoConsultaContraventor == EstadoConsultaContraventor.Pendiente || c.EstadoConsultaContraventor == EstadoConsultaContraventor.Error))
            .OrderBy(c => c.CreadoEn).Select(c => c.Id).Take(2000).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in ids)
        {
            try
            {
                var c = await db.Comparendos.Include(x => x.Contraventor).FirstAsync(x => x.Id == id, ct).ConfigureAwait(false);
                await servicio.ConsultarAsync(c, Actor.Api("API de contratos"), confirmarReemplazo: false, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (ReglaException ex)
            {
                LogOmitido(logger, id, ex.Codigo);
                db.ChangeTracker.Clear();
                // §4.7: si el OT bloquea la consulta, faltan placa/fecha o el NIT no usa la API (HU #13712), no aplica: el
                // comparendo pasa a «Sin contraventor (manual)».
                if (ex.Codigo is "OT_BLOQUEADO" or "SIN_DATOS_CONSULTA" or "NIT_SIN_API")
                {
                    await db.Comparendos.Where(c => c.Id == id)
                        .ExecuteUpdateAsync(u => u.SetProperty(c => c.EstadoConsultaContraventor, EstadoConsultaContraventor.NoAplica), ct).ConfigureAwait(false);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consulta de contraventor omitida para {Comparendo}: {Codigo}.")]
    private static partial void LogOmitido(ILogger logger, Guid comparendo, string codigo);
}
