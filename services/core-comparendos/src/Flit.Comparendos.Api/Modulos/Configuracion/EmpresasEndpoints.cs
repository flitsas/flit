using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Configuracion;

/// <summary>
/// Parametrizador por empresa (HU #13473; ADO #12977, §7.10-7.11). El SuperAdmin activa las capacidades contratadas, la
/// integración del contraventor, el conjunto de plantillas y si se exige representante legal (RN-006). El administrador
/// de la gestora edita solo su correo: canal, remitente, correo de respuesta, CC/BCC por tipo, destinatarios de reportes
/// y correos de alerta operativa. Todo queda auditado (RN-010).
/// </summary>
public static class EmpresasEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record CopiaDto(string Correo, IReadOnlyList<string> Tipos);

    public sealed record ConfiguracionDto(
        Guid EmpresaId,
        string? EmpresaNombre,
        bool HasRightPetitionModule,
        bool AllowsContravenerConsultation,
        bool AllowsContravenerChange,
        bool HasOcrAccess,
        bool HasReportesModule,
        string NotificationChannel,
        string CanalCorreo,
        string? RemitenteNombre,
        string? CorreoRespuesta,
        IReadOnlyList<CopiaDto> Cc,
        IReadOnlyList<CopiaDto> Bcc,
        string TipoIntegracionConductor,
        string ConjuntoPlantillas,
        bool RequiereRepresentanteLegal,
        IReadOnlyList<string> DestinatariosReportes,
        IReadOnlyList<string> CorreosAlertaOperativa,
        string? EmpresaNit);

    public sealed record NuevaEmpresa(Guid Id, string Nombre);

    public sealed record CambioCapacidades(
        bool HasRightPetitionModule,
        bool AllowsContravenerConsultation,
        bool AllowsContravenerChange,
        bool HasOcrAccess,
        bool HasReportesModule,
        string TipoIntegracionConductor,
        string ConjuntoPlantillas,
        bool RequiereRepresentanteLegal,
        string? EmpresaNombre,
        string? EmpresaNit = null);

    public sealed record CambioCorreo(
        string CanalCorreo,
        string? RemitenteNombre,
        string? CorreoRespuesta,
        IReadOnlyList<CopiaDto>? Cc,
        IReadOnlyList<CopiaDto>? Bcc,
        IReadOnlyList<string>? DestinatariosReportes,
        IReadOnlyList<string>? CorreosAlertaOperativa);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/configuracion", Propia).RequirePermiso(Permisos.ConfiguracionVer);
        api.MapPut("/configuracion/correo", GuardarCorreoPropio).RequirePermiso(Permisos.ConfiguracionEditar);

        var admin = api.MapGroup("/admin/empresas").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapGet("", Listar);
        admin.MapPost("", Registrar);
        admin.MapGet("/{id:guid}/configuracion", DeEmpresa);
        admin.MapPut("/{id:guid}/capacidades", GuardarCapacidades);
        admin.MapPut("/{id:guid}/correo", GuardarCorreoDeEmpresa);
        admin.MapGet("/{id:guid}/auditoria", Historial);
    }

    public static ConfiguracionDto Dto(ConfiguracionEmpresa c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.TenantId, c.EmpresaNombre, c.HasRightPetitionModule, c.AllowsContravenerConsultation, c.AllowsContravenerChange, c.HasOcrAccess,
            c.HasReportesModule, c.NotificationChannel.ToString(), c.CanalCorreo.ToString(), c.RemitenteNombre, c.CorreoRespuesta, Copias(c.Cc), Copias(c.Bcc),
            c.TipoIntegracionConductor.ToString(), c.ConjuntoPlantillas.ToString(), c.RequiereRepresentanteLegal, c.DestinatariosReportes, c.CorreosAlertaOperativa,
            c.EmpresaNit is { } nit ? $"{nit}-{Nit.DigitoVerificacion(nit)}" : null);
    }

    public static IReadOnlyList<CopiaDto> Copias(string json) =>
        JsonSerializer.Deserialize<List<CopiaDto>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? [];

    /// <summary>Correos de copia que aplican a un tipo de comunicación (lista de tipos vacía = todos).</summary>
    public static IReadOnlyList<string> CopiasPara(string json, TipoComunicacion tipo) =>
        Copias(json).Where(c => c.Tipos.Count == 0 || c.Tipos.Contains(tipo.ToString())).Select(c => c.Correo).ToList();

    private static async Task<IResult> Propia(UsuarioActual usuario, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(null, obligatoria: false) ?? throw new ReglaException("EMPRESA_REQUERIDA", "Elige una empresa.", StatusCodes.Status400BadRequest);
        return Results.Ok(Dto(await capacidades.DeAsync(tenant, ct, usuario.EmpresaNombre).ConfigureAwait(false)));
    }

    private static async Task<IResult> GuardarCorreoPropio(CambioCorreo pedido, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var config = await capacidades.DeAsync(tenant, ct, usuario.EmpresaNombre).ConfigureAwait(false);
        AplicarCorreo(config, pedido, auditoria, usuario, reloj);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(config));
    }

    private static async Task<IResult> Listar(string? buscar, ComparendosDb db, CancellationToken ct)
    {
        var q = db.ConfiguracionesEmpresa.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
            q = q.Where(c => c.EmpresaNombre != null && EF.Functions.ILike(c.EmpresaNombre, $"%{buscar.Trim()}%"));
        var filas = await q.OrderBy(c => c.EmpresaNombre).Select(c => new
        {
            id = c.TenantId,
            nombre = c.EmpresaNombre ?? c.TenantId.ToString(),
            c.HasRightPetitionModule,
            c.HasOcrAccess,
            c.HasReportesModule,
            c.AllowsContravenerChange,
            c.AllowsContravenerConsultation,
            comparendosActivos = db.Comparendos.Count(x => x.TenantId == c.TenantId && x.Activo),
            monitoreo = db.IdentificacionesMonitoreadas.Any(i => i.TenantId == c.TenantId && i.Activo) || db.PlacasMonitoreadas.Any(p => p.TenantId == c.TenantId && p.Activo),
            consultas = db.ConfiguracionesConsultas.Where(k => k.TenantId == c.TenantId).Select(k => (EstadoConfiguracionConsultas?)k.Estado).FirstOrDefault(),
            representante = db.RepresentantesLegales.Any(r => r.TenantId == c.TenantId && r.Activo),
            tipologias = db.TipologiasEmpresa.Any(t => t.TenantId == c.TenantId && t.Habilitada),
        }).ToListAsync(ct).ConfigureAwait(false);
        // Estado de configuración (Epic GDC-3): azul activa, gris inactiva (consultas pausadas), naranja pendiente de
        // configuración, con lo que le falta para operar.
        return Results.Ok(filas.Select(f =>
        {
            var faltantes = new List<string>();
            if (!f.monitoreo)
                faltantes.Add("Sin NIT ni placa monitoreados");
            if (f.consultas is null or EstadoConfiguracionConsultas.Borrador)
                faltantes.Add("Consultas sin activar");
            if ((f.HasRightPetitionModule || f.AllowsContravenerChange) && !f.representante)
                faltantes.Add("Sin representante legal");
            if (f.HasRightPetitionModule && !f.tipologias)
                faltantes.Add("Sin tipologías de DP habilitadas");
            var estado = f.consultas == EstadoConfiguracionConsultas.Pausada ? "Inactiva" : faltantes.Count > 0 ? "Pendiente" : "Activa";
            return new
            {
                f.id, f.nombre, f.HasRightPetitionModule, f.HasOcrAccess, f.HasReportesModule, f.AllowsContravenerChange, f.AllowsContravenerConsultation,
                f.comparendosActivos, estadoConfiguracion = estado, faltantes,
            };
        }));
    }

    /// <summary>
    /// Alta de una empresa en Comparendos antes de que alguno de sus usuarios entre (la habilitación del producto vive
    /// en la plataforma; aquí solo se crea su configuración). El id es el de la empresa en FLIT.
    /// </summary>
    private static async Task<IResult> Registrar(NuevaEmpresa pedido, Capacidades capacidades, Auditoria auditoria, ComparendosDb db, CancellationToken ct)
    {
        if (pedido.Id == Guid.Empty || string.IsNullOrWhiteSpace(pedido.Nombre))
            throw new ReglaException("EMPRESA_INVALIDA", "Escribe el id de la empresa en FLIT y su nombre.");
        var existe = await db.ConfiguracionesEmpresa.AnyAsync(c => c.TenantId == pedido.Id, ct).ConfigureAwait(false);
        var config = await capacidades.DeAsync(pedido.Id, ct, pedido.Nombre.Trim()).ConfigureAwait(false);
        if (!existe)
        {
            auditoria.Registrar(pedido.Id, "configuracion_empresa", pedido.Id, "CREAR", new CambioCampo("empresa_nombre", null, pedido.Nombre.Trim()));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(config));
    }

    private static async Task<IResult> DeEmpresa(Guid id, ComparendosDb db, CancellationToken ct)
    {
        var config = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La empresa");
        return Results.Ok(Dto(config));
    }

    private static async Task<IResult> GuardarCapacidades(Guid id, CambioCapacidades pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var c = await db.ConfiguracionesEmpresa.FirstOrDefaultAsync(x => x.TenantId == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La empresa");
        if (!Enum.TryParse<TipoIntegracionConductor>(pedido.TipoIntegracionConductor, true, out var integracion))
            throw new ReglaException("INTEGRACION_INVALIDA", "La integración del contraventor debe ser Manual o ApiRenting.");
        if (!Enum.TryParse<ConjuntoPlantillas>(pedido.ConjuntoPlantillas, true, out var conjunto))
            throw new ReglaException("CONJUNTO_INVALIDO", "El conjunto de plantillas debe ser Flit o Renting.");

        var nombre = string.IsNullOrWhiteSpace(pedido.EmpresaNombre) ? c.EmpresaNombre : pedido.EmpresaNombre.Trim();
        var nit = c.EmpresaNit;
        if (!string.IsNullOrWhiteSpace(pedido.EmpresaNit))
        {
            var separado = Nit.Separar(pedido.EmpresaNit) ?? throw new ReglaException("NIT_INVALIDO", "El NIT de la empresa no es válido.");
            if (separado.Dv is { } dv && dv != Nit.DigitoVerificacion(separado.Base))
                throw new ReglaException("NIT_INVALIDO", $"El dígito de verificación de {separado.Base} es {Nit.DigitoVerificacion(separado.Base)}, no {dv}.");
            nit = separado.Base;
        }

        var cambios = Auditoria.Diferencias(
            ("has_right_petition_module", c.HasRightPetitionModule, pedido.HasRightPetitionModule),
            ("allows_contravener_consultation", c.AllowsContravenerConsultation, pedido.AllowsContravenerConsultation),
            ("allows_contravener_change", c.AllowsContravenerChange, pedido.AllowsContravenerChange),
            ("has_ocr_access", c.HasOcrAccess, pedido.HasOcrAccess),
            ("has_reportes_module", c.HasReportesModule, pedido.HasReportesModule),
            ("tipo_integracion_conductor", c.TipoIntegracionConductor, integracion),
            ("conjunto_plantillas", c.ConjuntoPlantillas, conjunto),
            ("requiere_representante_legal", c.RequiereRepresentanteLegal, pedido.RequiereRepresentanteLegal),
            ("empresa_nombre", c.EmpresaNombre, nombre),
            ("empresa_nit", c.EmpresaNit, nit));
        if (cambios.Length == 0)
            return Results.Ok(Dto(c));

        var activaCambio = !c.AllowsContravenerChange && pedido.AllowsContravenerChange;
        var activaConsulta = (!c.AllowsContravenerConsultation || c.TipoIntegracionConductor != TipoIntegracionConductor.ApiRenting)
            && pedido.AllowsContravenerConsultation && integracion == TipoIntegracionConductor.ApiRenting;
        (c.HasRightPetitionModule, c.AllowsContravenerConsultation, c.AllowsContravenerChange, c.HasOcrAccess, c.HasReportesModule) =
            (pedido.HasRightPetitionModule, pedido.AllowsContravenerConsultation, pedido.AllowsContravenerChange, pedido.HasOcrAccess, pedido.HasReportesModule);
        (c.TipoIntegracionConductor, c.ConjuntoPlantillas, c.RequiereRepresentanteLegal, c.EmpresaNombre, c.EmpresaNit) = (integracion, conjunto, pedido.RequiereRepresentanteLegal, nombre, nit);
        (c.ActualizadoEn, c.ActualizadoPor) = (reloj.Ahora, usuario.Id);
        auditoria.Registrar(id, "configuracion_empresa", id, "ACTUALIZAR", cambios);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Al activar el cambio de contraventor, los comparendos activos sin solicitud pasan a «sin solicitar» (S-34);
        // al activar la consulta por API, los que no tienen conductor quedan pendientes de consulta.
        if (activaCambio)
        {
            await db.Comparendos.Where(x => x.TenantId == id && x.Activo && x.EstadoCambioContraventor == EstadoCambioContraventor.NoAplica)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.EstadoCambioContraventor, EstadoCambioContraventor.SinSolicitar), ct).ConfigureAwait(false);
        }

        if (activaConsulta)
        {
            await db.Comparendos.Where(x => x.TenantId == id && x.Activo && x.Contraventor == null && x.EstadoConsultaContraventor == EstadoConsultaContraventor.NoAplica)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.EstadoConsultaContraventor, EstadoConsultaContraventor.Pendiente), ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(c));
    }

    private static async Task<IResult> GuardarCorreoDeEmpresa(Guid id, CambioCorreo pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var c = await db.ConfiguracionesEmpresa.FirstOrDefaultAsync(x => x.TenantId == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La empresa");
        AplicarCorreo(c, pedido, auditoria, usuario, reloj);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(c));
    }

    private static void AplicarCorreo(ConfiguracionEmpresa c, CambioCorreo pedido, Auditoria auditoria, UsuarioActual usuario, Reloj reloj)
    {
        if (!Enum.TryParse<CanalCorreo>(pedido.CanalCorreo, true, out var canal))
            throw new ReglaException("CANAL_INVALIDO", "El canal de correo debe ser FlitSmtp o EmpresaApi.");
        var respuesta = string.IsNullOrWhiteSpace(pedido.CorreoRespuesta) ? null : CatalogosEndpoints.NormalizarCorreos([pedido.CorreoRespuesta]).Single();
        var cc = JsonSerializer.Serialize(NormalizarCopias(pedido.Cc), Json);
        var bcc = JsonSerializer.Serialize(NormalizarCopias(pedido.Bcc), Json);
        var reportes = CatalogosEndpoints.NormalizarCorreos(pedido.DestinatariosReportes ?? []);
        var alertas = CatalogosEndpoints.NormalizarCorreos(pedido.CorreosAlertaOperativa ?? []);
        var remitente = string.IsNullOrWhiteSpace(pedido.RemitenteNombre) ? null : pedido.RemitenteNombre.Trim();
        if (remitente is { Length: > 120 })
            throw new ReglaException("REMITENTE_INVALIDO", "El nombre del remitente admite hasta 120 caracteres.");

        var cambios = Auditoria.Diferencias(
            ("canal_correo", c.CanalCorreo, canal), ("remitente_nombre", c.RemitenteNombre, remitente), ("correo_respuesta", c.CorreoRespuesta, respuesta),
            ("cc", c.Cc, cc), ("bcc", c.Bcc, bcc), ("destinatarios_reportes", string.Join(", ", c.DestinatariosReportes), string.Join(", ", reportes)),
            ("correos_alerta_operativa", string.Join(", ", c.CorreosAlertaOperativa), string.Join(", ", alertas)));
        if (cambios.Length == 0)
            return;

        (c.CanalCorreo, c.RemitenteNombre, c.CorreoRespuesta, c.Cc, c.Bcc) = (canal, remitente, respuesta, cc, bcc);
        (c.DestinatariosReportes, c.CorreosAlertaOperativa, c.ActualizadoEn, c.ActualizadoPor) = (reportes, alertas, reloj.Ahora, usuario.Id);
        auditoria.Registrar(c.TenantId, "configuracion_empresa", c.TenantId, "ACTUALIZAR", cambios);
    }

    private static List<CopiaDto> NormalizarCopias(IReadOnlyList<CopiaDto>? copias)
    {
        var lista = new List<CopiaDto>();
        foreach (var copia in copias ?? [])
        {
            var correo = CatalogosEndpoints.NormalizarCorreos([copia.Correo]).SingleOrDefault();
            if (correo is null)
                continue;
            var tipos = (copia.Tipos ?? []).Select(t => Enum.TryParse<TipoComunicacion>(t, true, out var e) ? e.ToString() : throw new ReglaException("TIPO_INVALIDO", $"Tipo de comunicación desconocido: {t}.")).Distinct().ToList();
            if (lista.All(x => x.Correo != correo))
                lista.Add(new CopiaDto(correo, tipos));
        }

        return lista;
    }

    private static async Task<IResult> Historial(Guid id, ComparendosDb db, CancellationToken ct) =>
        Results.Ok(await db.AuditoriasConfiguracion.AsNoTracking().Where(a => a.TenantId == id).OrderByDescending(a => a.RegistradoEn).Take(200)
            .Select(a => new { a.Id, a.Entidad, a.EntidadId, a.Accion, a.Cambios, a.UsuarioNombre, a.RegistradoEn }).ToListAsync(ct).ConfigureAwait(false));
}
