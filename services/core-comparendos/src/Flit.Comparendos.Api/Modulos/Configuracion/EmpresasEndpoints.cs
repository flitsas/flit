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
public static partial class EmpresasEndpoints
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

    /// <summary>La empresa elegida de la lista de la plataforma (<c>GET /admin/empresas/plataforma</c>).</summary>
    public sealed record NuevaEmpresa(Guid Id);

    public sealed record CambioCapacidades(
        bool HasRightPetitionModule,
        bool AllowsContravenerConsultation,
        bool AllowsContravenerChange,
        bool HasOcrAccess,
        bool HasReportesModule,
        string TipoIntegracionConductor,
        string ConjuntoPlantillas,
        bool RequiereRepresentanteLegal);

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
        admin.MapGet("/plataforma", DePlataforma);
        admin.MapPost("", Registrar);
        admin.MapGet("/{id:guid}/configuracion", DeEmpresa);
        admin.MapPut("/{id:guid}/capacidades", GuardarCapacidades);
        admin.MapPut("/{id:guid}/correo", GuardarCorreoDeEmpresa);
        admin.MapGet("/{id:guid}/auditoria", Historial);
        admin.MapPut("/{id:guid}/nits/{nitId:guid}/consulta-contrato", GuardarConsultaContratoNit);
    }

    public static ConfiguracionDto Dto(ConfiguracionEmpresa c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.TenantId, c.EmpresaNombre, c.HasRightPetitionModule, c.AllowsContravenerConsultation, c.AllowsContravenerChange, c.HasOcrAccess,
            c.HasReportesModule, c.NotificationChannel.ToString(), c.CanalCorreo.ToString(), c.RemitenteNombre, c.CorreoRespuesta, Copias(c.Cc), Copias(c.Bcc),
            c.TipoIntegracionConductor.ToString(), c.ConjuntoPlantillas.ToString(), c.RequiereRepresentanteLegal, c.DestinatariosReportes, c.CorreosAlertaOperativa,
            ConDv(c.EmpresaNit));
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
    /// Empresas de la plataforma con Comparendos encendido, para elegir al registrar (hallazgos B2-1 y B2-4). Trae el nombre
    /// y el NIT de la plataforma, con el dígito de verificación calculado, y si ya está registrada aquí.
    /// </summary>
    private static async Task<IResult> DePlataforma(IEmpresasPlataforma plataforma, ComparendosDb db, CancellationToken ct)
    {
        var empresas = await ListaPlataformaAsync(plataforma, ct).ConfigureAwait(false);
        var registradas = (await db.ConfiguracionesEmpresa.AsNoTracking().Select(c => c.TenantId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return Results.Ok(empresas.Select(e => new { e.Id, e.Nombre, nit = ConDv(e.NitBase), registrada = registradas.Contains(e.Id) }));
    }

    /// <summary>
    /// Alta de una empresa en Comparendos antes de que alguno de sus usuarios entre. Se elige de la lista de la plataforma:
    /// solo una empresa con el producto encendido, y su nombre y NIT salen de allá, no se escriben.
    /// </summary>
    private static async Task<IResult> Registrar(NuevaEmpresa pedido, IEmpresasPlataforma plataforma, Capacidades capacidades, Auditoria auditoria, ComparendosDb db, CancellationToken ct)
    {
        var empresa = (await ListaPlataformaAsync(plataforma, ct).ConfigureAwait(false)).FirstOrDefault(e => e.Id == pedido.Id)
            ?? throw new ReglaException("EMPRESA_SIN_PRODUCTO", "Esa empresa no tiene Comparendos encendido en la plataforma. Enciéndelo en Administración de compañías → Productos.");
        var existe = await db.ConfiguracionesEmpresa.AnyAsync(c => c.TenantId == empresa.Id, ct).ConfigureAwait(false);
        var config = await capacidades.DeAsync(empresa.Id, ct, empresa.Nombre).ConfigureAwait(false);
        if (!existe)
        {
            config.EmpresaNit = empresa.NitBase;
            auditoria.Registrar(empresa.Id, "configuracion_empresa", empresa.Id, "CREAR",
                new CambioCampo("empresa_nombre", null, empresa.Nombre), new CambioCampo("empresa_nit", null, empresa.NitBase));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(config));
    }

    /// <summary>
    /// Al abrir una empresa, su nombre y su NIT se ponen al día con la plataforma (la dueña de esos datos). Si la
    /// plataforma no responde, se muestra lo último guardado.
    /// </summary>
    private static async Task<IResult> DeEmpresa(Guid id, IEmpresasPlataforma plataforma, Auditoria auditoria, ComparendosDb db, ILoggerFactory logs, CancellationToken ct)
    {
        var config = await db.ConfiguracionesEmpresa.FirstOrDefaultAsync(c => c.TenantId == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La empresa");
        IReadOnlyList<EmpresaPlataforma>? empresas = null;
        try
        {
            empresas = await plataforma.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (global::Grpc.Core.RpcException ex)
        {
            LogPlataformaNoResponde(logs.CreateLogger(typeof(EmpresasEndpoints)), id, ex.StatusCode);
        }

        if (empresas?.FirstOrDefault(e => e.Id == id) is { } empresa)
        {
            var cambios = Auditoria.Diferencias(("empresa_nombre", config.EmpresaNombre, empresa.Nombre), ("empresa_nit", config.EmpresaNit, empresa.NitBase));
            if (cambios.Length > 0)
            {
                (config.EmpresaNombre, config.EmpresaNit) = (empresa.Nombre, empresa.NitBase);
                auditoria.Registrar(id, "configuracion_empresa", id, "SINCRONIZAR", cambios);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        return Results.Ok(Dto(config));
    }

    private static async Task<IReadOnlyList<EmpresaPlataforma>> ListaPlataformaAsync(IEmpresasPlataforma plataforma, CancellationToken ct)
    {
        const string NoDisponible = "No se pudo leer la lista de empresas de la plataforma. Intenta de nuevo en unos minutos.";
        try
        {
            return await plataforma.ListarAsync(ct).ConfigureAwait(false)
                ?? throw new ReglaException("PLATAFORMA_NO_DISPONIBLE", NoDisponible, StatusCodes.Status503ServiceUnavailable);
        }
        catch (global::Grpc.Core.RpcException)
        {
            throw new ReglaException("PLATAFORMA_NO_DISPONIBLE", NoDisponible, StatusCodes.Status503ServiceUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer la empresa {Empresa} en la plataforma ({Codigo}); se muestra lo último guardado.")]
    private static partial void LogPlataformaNoResponde(ILogger logger, Guid empresa, global::Grpc.Core.StatusCode codigo);

    private static string? ConDv(string? nitBase) => nitBase is { } nit ? $"{nit}-{Nit.DigitoVerificacion(nit)}" : null;

    private static async Task<IResult> GuardarCapacidades(Guid id, CambioCapacidades pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var c = await db.ConfiguracionesEmpresa.FirstOrDefaultAsync(x => x.TenantId == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La empresa");
        if (!Enum.TryParse<TipoIntegracionConductor>(pedido.TipoIntegracionConductor, true, out var integracion))
            throw new ReglaException("INTEGRACION_INVALIDA", "La integración del contraventor debe ser Manual o ApiRenting.");
        if (!Enum.TryParse<ConjuntoPlantillas>(pedido.ConjuntoPlantillas, true, out var conjunto))
            throw new ReglaException("CONJUNTO_INVALIDO", "El conjunto de plantillas debe ser Flit o Renting.");

        var cambios = Auditoria.Diferencias(
            ("has_right_petition_module", c.HasRightPetitionModule, pedido.HasRightPetitionModule),
            ("allows_contravener_consultation", c.AllowsContravenerConsultation, pedido.AllowsContravenerConsultation),
            ("allows_contravener_change", c.AllowsContravenerChange, pedido.AllowsContravenerChange),
            ("has_ocr_access", c.HasOcrAccess, pedido.HasOcrAccess),
            ("has_reportes_module", c.HasReportesModule, pedido.HasReportesModule),
            ("tipo_integracion_conductor", c.TipoIntegracionConductor, integracion),
            ("conjunto_plantillas", c.ConjuntoPlantillas, conjunto),
            ("requiere_representante_legal", c.RequiereRepresentanteLegal, pedido.RequiereRepresentanteLegal));
        if (cambios.Length == 0)
            return Results.Ok(Dto(c));

        var activaCambio = !c.AllowsContravenerChange && pedido.AllowsContravenerChange;
        var cambiaConsulta = c.AllowsContravenerConsultation != pedido.AllowsContravenerConsultation || c.TipoIntegracionConductor != integracion;
        (c.HasRightPetitionModule, c.AllowsContravenerConsultation, c.AllowsContravenerChange, c.HasOcrAccess, c.HasReportesModule) =
            (pedido.HasRightPetitionModule, pedido.AllowsContravenerConsultation, pedido.AllowsContravenerChange, pedido.HasOcrAccess, pedido.HasReportesModule);
        (c.TipoIntegracionConductor, c.ConjuntoPlantillas, c.RequiereRepresentanteLegal) = (integracion, conjunto, pedido.RequiereRepresentanteLegal);
        (c.ActualizadoEn, c.ActualizadoPor) = (reloj.Ahora, usuario.Id);
        auditoria.Registrar(id, "configuracion_empresa", id, "ACTUALIZAR", cambios);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Al activar el cambio de contraventor, los comparendos activos sin solicitud pasan a «sin solicitar» (S-34);
        // al encender o apagar la consulta por API, los que no tienen conductor se recalculan con las casillas de cada NIT
        // (HU #13712): pendientes de consulta si su NIT la usa, asignación manual si no.
        if (activaCambio)
        {
            await db.Comparendos.Where(x => x.TenantId == id && x.Activo && x.EstadoCambioContraventor == EstadoCambioContraventor.NoAplica)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.EstadoCambioContraventor, EstadoCambioContraventor.SinSolicitar), ct).ConfigureAwait(false);
        }

        if (cambiaConsulta)
            await Contraventor.ConsultaPorApi.RecalcularAsync(db, id, ct).ConfigureAwait(false);

        return Results.Ok(Dto(c));
    }

    /// <summary>Casilla de un NIT para consultar el conductor en la API de contratos (HU #13712). Solo el SuperAdmin.</summary>
    public sealed record CambioConsultaContrato(bool Activo);

    private static async Task<IResult> GuardarConsultaContratoNit(Guid id, Guid nitId, CambioConsultaContrato pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario,
        Reloj reloj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var nit = await db.IdentificacionesMonitoreadas.FirstOrDefaultAsync(n => n.Id == nitId && n.TenantId == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El NIT");
        if (nit.ConsultaContratoApi != pedido.Activo)
        {
            auditoria.Registrar(id, "identificacion_monitoreada", nit.Id, "ACTUALIZAR", new CambioCampo("consulta_contrato_api", nit.ConsultaContratoApi, pedido.Activo));
            (nit.ConsultaContratoApi, nit.ActualizadoEn, nit.ActualizadoPor) = (pedido.Activo, reloj.Ahora, usuario.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await Contraventor.ConsultaPorApi.RecalcularAsync(db, id, ct).ConfigureAwait(false);
        }

        return Results.Ok(Monitoreo.MonitoreoEndpoints.ANit(nit));
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
