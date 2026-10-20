using System.Globalization;
using System.Net;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>
/// Alertas por fallo de integración (§6.5, RN-041): a los destinatarios activos según el tipo (todas, solo críticas,
/// solo SIMIT, solo municipal, solo OCR), con el interruptor general y el umbral del ciclo. Umbral 0: un correo por
/// cada clave fallida; N &gt; 0: un consolidado si fallaron más de N. El fallo del trabajo completo es crítico e ignora
/// el umbral. El NIT de una persona va enmascarado; nunca se alteran los datos de la empresa.
/// </summary>
public sealed class AlertasIntegracion(ComparendosDb db, ServicioCorreo correo, Reloj reloj) : IAlertasIntegracion
{
    public async Task FallosDelCicloAsync(CicloIngesta ciclo, IReadOnlyList<(ConsultaFuente Consulta, IReadOnlyList<Guid> Empresas)> fallos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        ArgumentNullException.ThrowIfNull(fallos);
        var config = await db.AlertaConfiguraciones.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (config is not { AlertasHabilitadas: true } || fallos.Count == 0)
            return;

        if (config.UmbralFallosCiclo == 0)
        {
            foreach (var fallo in fallos)
                await EnviarAsync(ciclo, config, [fallo], critica: false, ct).ConfigureAwait(false);
        }
        else if (fallos.Count > config.UmbralFallosCiclo)
        {
            await EnviarAsync(ciclo, config, fallos, critica: false, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task FalloDelTrabajoAsync(CicloIngesta ciclo, Exception falla, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        ArgumentNullException.ThrowIfNull(falla);
        var config = await db.AlertaConfiguraciones.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (config is not { AlertasHabilitadas: true })
            return;

        await EnviarAsync(ciclo, config, [], critica: true, ct, falla.Message).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnviarAsync(CicloIngesta ciclo, AlertaConfiguracion config, IReadOnlyList<(ConsultaFuente Consulta, IReadOnlyList<Guid> Empresas)> fallos, bool critica, CancellationToken ct, string? errorTrabajo = null)
    {
        var simit = ciclo.Canal is CanalIngesta.SimitNit or CanalIngesta.SimitPlaca;
        var destinatarios = await db.AlertaDestinatarios.AsNoTracking().Where(d => d.Activo).ToListAsync(ct).ConfigureAwait(false);
        var para = destinatarios.Where(d => d.TipoAlerta == TipoAlertaDestinatario.Todas
                || (d.TipoAlerta == TipoAlertaDestinatario.SoloCriticas && critica)
                || (d.TipoAlerta == TipoAlertaDestinatario.SoloSimit && simit)
                || (d.TipoAlerta == TipoAlertaDestinatario.SoloMunicipal && !simit))
            .Select(d => new Destinatario(d.Correo, d.Nombre)).ToList();
        if (para.Count == 0)
            return;

        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking().ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre ?? e.TenantId.ToString(), ct).ConfigureAwait(false);
        var momento = ColombiaTime.From(reloj.Ahora).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var fuente = Reconciliador.Etiqueta(ciclo.Canal);
        var asunto = config.PlantillaAsunto.Replace("{FUENTE}", fuente, StringComparison.Ordinal).Replace("{TIMESTAMP}", momento, StringComparison.Ordinal);
        if (critica)
            asunto = "[CRÍTICA] " + asunto;

        var filas = fallos.Count == 0
            ? [("Clave afectada", "Todas (falló el trabajo completo)"), ("Error", errorTrabajo)]
            : fallos.Select(f => (
                $"{Clave(f.Consulta)}{(f.Consulta.MunicipioCodigo is { } m ? $" · {m}" : string.Empty)}",
                (string?)$"{f.Consulta.TipoError}: {f.Consulta.MensajeError} — gestoras: {string.Join(", ", f.Empresas.Select(e => empresas.GetValueOrDefault(e, e.ToString())))}")).ToList();
        var cuerpo = $"""
            <p>Falló la consulta a <b>{WebUtility.HtmlEncode(fuente)}</b> en el ciclo del {momento}.</p>
            {HtmlCorreo.Tabla(filas)}
            <p><b>Acción tomada:</b> datos existentes preservados — no se modificó ningún registro de las gestoras afectadas.</p>
            <p><a href="{correo.AppUrl}/admin/log-migracion?ciclo={ciclo.Id}">Ver el log de migración</a></p>
            """;
        await correo.RegistrarAsync(new CorreoNuevo
        {
            Tipo = TipoComunicacion.AlertaFalloIntegracion,
            Plantilla = "comparendos.alerta_fallo_integracion",
            Para = para,
            Asunto = asunto,
            Html = HtmlCorreo.Envolver(critica ? "Fallo crítico de integración" : "Fallo de integración", cuerpo),
            RemitenteNombre = config.RemitenteNombre,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Un NIT de empresa se muestra; el documento de una persona va enmascarado (RN-011).</summary>
    private static string Clave(ConsultaFuente c) =>
        c.TipoDocumento is null or TipoDocumento.NIT ? c.ClaveConsultada : $"{c.TipoDocumento} {HtmlCorreo.Enmascarar(c.ClaveConsultada)}";
}
