using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Configuracion;

/// <summary>Para qué se busca el firmante.</summary>
public enum ParaFirmar
{
    DerechoPeticion,
    CambioContraventor,
}

/// <summary>
/// Quién firma (§6.9) [Excel J7, P6, P10]: el representante legal activo del NIT del comparendo con el permiso que
/// corresponde; si no hay, el de la empresa. FLIT nunca firma ni envía a su nombre. Sin NIT (comparendos por placa) se
/// usa el de la empresa (S-55). La vigencia de los documentos no bloquea.
/// </summary>
public sealed class ResolutorRepresentante(ComparendosDb db)
{
    public async Task<RepresentanteLegal?> ResolverAsync(Guid tenantId, Guid? identificacionId, ParaFirmar para, CancellationToken ct)
    {
        var q = db.RepresentantesLegales.AsNoTracking().Where(r => r.TenantId == tenantId && r.Activo);
        q = para == ParaFirmar.DerechoPeticion ? q.Where(r => r.PermisoEnvioDp) : q.Where(r => r.PermisoCambioContraventor);
        if (identificacionId is { } nit)
        {
            var delNit = await q.FirstOrDefaultAsync(r => r.Alcance == AlcanceRepresentante.Identificacion && r.IdentificacionMonitoreadaId == nit, ct).ConfigureAwait(false);
            if (delNit is not null)
                return delNit;
        }

        return await q.FirstOrDefaultAsync(r => r.Alcance == AlcanceRepresentante.Empresa, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El propietario del vehículo (quien pide el cambio de contraventor o firma el DP como peticionario) y quién firma a
    /// su nombre (hallazgo B9-1). El propietario es el infractor que trae la fuente o, si no lo trae, el NIT consultado; es
    /// de la empresa si coincide con su NIT o con uno de sus NITs vigilados (la fuente puede traer el NIT con el dígito
    /// pegado: «8110117798»). Firma el RL de ese NIT, o el de la empresa solo cuando el propietario es la empresa misma:
    /// los documentos dicen «en calidad de representante legal de {propietario}».
    /// </summary>
    public async Task<(bool EsDeLaEmpresa, RepresentanteLegal? Representante)> DelPropietarioAsync(Comparendo c, string? empresaNit, ParaFirmar para, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var nits = await db.IdentificacionesMonitoreadas.AsNoTracking()
            .Where(n => n.TenantId == c.TenantId && n.Activo && n.TipoDocumento == TipoDocumento.NIT)
            .Select(n => new { n.Id, n.NumeroBase }).ToListAsync(ct).ConfigureAwait(false);
        bool esLaEmpresa;
        Guid? nitId;
        if (string.IsNullOrWhiteSpace(c.NumeroDocumentoInfractor))
        {
            var porNit = nits.FirstOrDefault(n => n.Id == c.IdentificacionMonitoreadaId);
            if (porNit is null)
                return (false, null);
            (esLaEmpresa, nitId) = (Coincide(empresaNit, porNit.NumeroBase), porNit.Id);
        }
        else
        {
            if (c.TipoDocumentoInfractor is { } tipo && tipo != TipoDocumento.NIT)
                return (false, null);
            var documento = new string(c.NumeroDocumentoInfractor.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
            esLaEmpresa = Coincide(empresaNit, documento);
            nitId = nits.FirstOrDefault(n => Coincide(n.NumeroBase, documento))?.Id;
            if (!esLaEmpresa && nitId is null)
                return (false, null);
        }

        return (true, await ResolverDelPropietarioAsync(c.TenantId, nitId, esLaEmpresa, para, ct).ConfigureAwait(false));
    }

    private static bool Coincide(string? nitBase, string documento) =>
        !string.IsNullOrEmpty(nitBase) && documento.Length > 0
        && (documento == nitBase || documento == string.Concat(nitBase, Nit.DigitoVerificacion(nitBase).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private async Task<RepresentanteLegal?> ResolverDelPropietarioAsync(Guid tenantId, Guid? identificacionId, bool esLaEmpresa, ParaFirmar para, CancellationToken ct)
    {
        var q = db.RepresentantesLegales.AsNoTracking().Where(r => r.TenantId == tenantId && r.Activo);
        q = para == ParaFirmar.DerechoPeticion ? q.Where(r => r.PermisoEnvioDp) : q.Where(r => r.PermisoCambioContraventor);
        if (identificacionId is { } nit
            && await q.FirstOrDefaultAsync(r => r.Alcance == AlcanceRepresentante.Identificacion && r.IdentificacionMonitoreadaId == nit, ct).ConfigureAwait(false) is { } delNit)
            return delNit;
        return esLaEmpresa ? await q.FirstOrDefaultAsync(r => r.Alcance == AlcanceRepresentante.Empresa, ct).ConfigureAwait(false) : null;
    }
}

/// <summary>
/// Permisos por organismo de tránsito («Admin de OTs bloqueados», §3.8): sin fila, todo se permite (un OT solo se
/// bloquea de forma explícita, S-14). Una fila por OT y representante gana sobre la fila del OT para cualquier
/// representante.
/// </summary>
public sealed class PermisosOt(ComparendosDb db)
{
    public async Task<(bool Consulta, bool Cambio)> DeAsync(Guid tenantId, Guid organismoId, Guid? representanteId, CancellationToken ct)
    {
        var filas = await db.PermisosOtEmpresa.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.OrganismoTransitoId == organismoId && (p.RepresentanteLegalId == null || p.RepresentanteLegalId == representanteId))
            .ToListAsync(ct).ConfigureAwait(false);
        var fila = filas.FirstOrDefault(p => representanteId is not null && p.RepresentanteLegalId == representanteId) ?? filas.FirstOrDefault(p => p.RepresentanteLegalId is null);
        return fila is null ? (true, true) : (fila.PermiteConsultaContraventor, fila.PermiteCambioContraventor);
    }
}

/// <summary>Representantes legales y permisos por OT de la empresa (HU #13475, §7.13-7.14).</summary>
public static class RepresentantesEndpoints
{
    public sealed record RepresentanteDto(Guid Id, string Alcance, Guid? IdentificacionMonitoreadaId, string? Nit, string TipoDocumento, string NumeroDocumento, string Nombres,
        string? Cargo, string? Correo, string? Telefono, string? DireccionNotificacion, Guid? FirmaArchivoId, Guid? CedulaArchivoId, Guid? CertificadoArchivoId,
        Guid? EscrituraArchivoId, DateOnly? FechaExpedicionCertificado, bool CertificadoVencido, bool PermisoEnvioDp, bool PermisoCambioContraventor, bool Activo);

    public sealed record DatosRepresentante(string Alcance, Guid? IdentificacionMonitoreadaId, string TipoDocumento, string NumeroDocumento, string Nombres, string? Cargo,
        string? Correo, string? Telefono, string? DireccionNotificacion, DateOnly? FechaExpedicionCertificado, bool PermisoEnvioDp, bool PermisoCambioContraventor, bool? Activo);

    public sealed record PermisoOtDto(Guid OrganismoTransitoId, string Organismo, string? Municipio, Guid? RepresentanteLegalId, string? Representante,
        bool PermiteConsultaContraventor, bool PermiteCambioContraventor, bool CambioBloqueadoGlobal, int ComparendosActivos);

    public sealed record CambioPermisoOt(Guid OrganismoTransitoId, Guid? RepresentanteLegalId, bool PermiteConsultaContraventor, bool PermiteCambioContraventor);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/representantes", Listar).RequirePermiso(Permisos.RepresentantesVer);
        api.MapPost("/representantes", Crear).RequirePermiso(Permisos.RepresentantesGestionar);
        api.MapPut("/representantes/{id:guid}", Editar).RequirePermiso(Permisos.RepresentantesGestionar);
        api.MapPost("/representantes/{id:guid}/documentos/{tipo}", SubirDocumento).RequirePermiso(Permisos.RepresentantesGestionar).DisableAntiforgery();
        api.MapGet("/organismos/permisos", ListarPermisos).RequirePermiso(Permisos.OtsGestionar);
        api.MapPut("/organismos/permisos", GuardarPermiso).RequirePermiso(Permisos.OtsGestionar);
    }

    private static async Task<IResult> Listar(Guid? empresa, UsuarioActual usuario, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa);
        var lista = await (from r in db.RepresentantesLegales.AsNoTracking()
                           where r.TenantId == tenant
                           join n in db.IdentificacionesMonitoreadas.AsNoTracking() on r.IdentificacionMonitoreadaId equals n.Id into nits
                           from n in nits.DefaultIfEmpty()
                           orderby r.Activo descending, r.Alcance, r.Nombres
                           select new { r, nit = n == null ? null : n.NumeroBase }).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(lista.Select(x => Dto(x.r, x.nit, reloj.Hoy)));
    }

    /// <summary>El certificado se marca «vencido» a los 30 días de expedido, solo como aviso: no bloquea [Excel J7].</summary>
    private static RepresentanteDto Dto(RepresentanteLegal r, string? nit, DateOnly hoy) => new(
        r.Id, r.Alcance.ToString(), r.IdentificacionMonitoreadaId, nit, r.TipoDocumento.ToString(), r.NumeroDocumento, r.Nombres, r.Cargo, r.Correo, r.Telefono,
        r.DireccionNotificacion, r.FirmaArchivoId, r.CedulaArchivoId, r.CertificadoArchivoId, r.EscrituraArchivoId, r.FechaExpedicionCertificado,
        r.FechaExpedicionCertificado is { } f && f.AddDays(30) < hoy, r.PermisoEnvioDp, r.PermisoCambioContraventor, r.Activo);

    private static async Task<(AlcanceRepresentante Alcance, TipoDocumento Tipo, Guid? Nit)> ValidarAsync(DatosRepresentante d, Guid tenant, ComparendosDb db, CancellationToken ct)
    {
        if (!Enum.TryParse<AlcanceRepresentante>(d.Alcance, true, out var alcance))
            throw new ReglaException("ALCANCE_INVALIDO", "El alcance es Empresa o Identificacion (de un NIT monitoreado).");
        if (!Enum.TryParse<TipoDocumento>(d.TipoDocumento, true, out var tipo) || !Enum.IsDefined(tipo))
            throw new ReglaException("TIPO_DOCUMENTO_INVALIDO", "Tipo de documento inválido.");
        if (string.IsNullOrWhiteSpace(d.NumeroDocumento) || d.NumeroDocumento.Trim().Length > 20 || string.IsNullOrWhiteSpace(d.Nombres))
            throw new ReglaException("REPRESENTANTE_INCOMPLETO", "El documento (hasta 20 caracteres) y el nombre son obligatorios.");
        if (!string.IsNullOrWhiteSpace(d.Correo))
            _ = CatalogosEndpoints.NormalizarCorreos([d.Correo]);

        Guid? nit = null;
        if (alcance == AlcanceRepresentante.Identificacion)
        {
            nit = d.IdentificacionMonitoreadaId ?? throw new ReglaException("NIT_REQUERIDO", "Elige el NIT que representa.");
            if (!await db.IdentificacionesMonitoreadas.AnyAsync(n => n.Id == nit && n.TenantId == tenant, ct).ConfigureAwait(false))
                throw ReglaException.NoEncontrado("El NIT");
        }

        return (alcance, tipo, nit);
    }

    private static async Task<IResult> Crear(DatosRepresentante d, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var (alcance, tipo, nit) = await ValidarAsync(d, tenant, db, ct).ConfigureAwait(false);
        var r = new RepresentanteLegal { Id = Guid.CreateVersion7(), TenantId = tenant, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        Aplicar(r, d, alcance, tipo, nit);
        db.RepresentantesLegales.Add(r);
        auditoria.Registrar(tenant, "representante_legal", r.Id, "CREAR", new CambioCampo("nombres", null, r.Nombres), new CambioCampo("alcance", null, alcance));
        await GuardarUnicoAsync(db, ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/representantes/{r.Id}", Dto(r, null, reloj.Hoy));
    }

    private static async Task<IResult> Editar(Guid id, DatosRepresentante d, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var r = await db.RepresentantesLegales.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El representante");
        var (alcance, tipo, nit) = await ValidarAsync(d, tenant, db, ct).ConfigureAwait(false);
        var cambios = Auditoria.Diferencias(("alcance", r.Alcance, alcance), ("identificacion", r.IdentificacionMonitoreadaId, nit), ("documento", r.NumeroDocumento, d.NumeroDocumento.Trim()),
            ("nombres", r.Nombres, d.Nombres.Trim()), ("cargo", r.Cargo, d.Cargo?.Trim()), ("correo", r.Correo, d.Correo?.Trim().ToLowerInvariant()),
            ("permiso_envio_dp", r.PermisoEnvioDp, d.PermisoEnvioDp), ("permiso_cambio_contraventor", r.PermisoCambioContraventor, d.PermisoCambioContraventor),
            ("fecha_expedicion_certificado", r.FechaExpedicionCertificado, d.FechaExpedicionCertificado), ("activo", r.Activo, d.Activo ?? r.Activo));
        Aplicar(r, d, alcance, tipo, nit);
        (r.ActualizadoEn, r.ActualizadoPor) = (reloj.Ahora, usuario.Id);
        if (cambios.Length > 0)
            auditoria.Registrar(tenant, "representante_legal", r.Id, "ACTUALIZAR", cambios);
        await GuardarUnicoAsync(db, ct).ConfigureAwait(false);
        return Results.Ok(Dto(r, null, reloj.Hoy));
    }

    private static void Aplicar(RepresentanteLegal r, DatosRepresentante d, AlcanceRepresentante alcance, TipoDocumento tipo, Guid? nit)
    {
        (r.Alcance, r.IdentificacionMonitoreadaId, r.TipoDocumento, r.NumeroDocumento, r.Nombres) = (alcance, nit, tipo, d.NumeroDocumento.Trim(), d.Nombres.Trim());
        (r.Cargo, r.Correo, r.Telefono, r.DireccionNotificacion) = (d.Cargo?.Trim(), d.Correo?.Trim().ToLowerInvariant(), d.Telefono?.Trim(), d.DireccionNotificacion?.Trim());
        (r.FechaExpedicionCertificado, r.PermisoEnvioDp, r.PermisoCambioContraventor, r.Activo) = (d.FechaExpedicionCertificado, d.PermisoEnvioDp, d.PermisoCambioContraventor, d.Activo ?? r.Activo);
    }

    /// <summary>Un solo representante activo de la empresa y uno por NIT (índices únicos parciales).</summary>
    private static async Task GuardarUnicoAsync(ComparendosDb db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_representante_legal", StringComparison.Ordinal) == true)
        {
            throw ReglaException.Conflicto("REPRESENTANTE_DUPLICADO", "Ya hay un representante activo para ese alcance. Desactiva el actual antes de activar otro.");
        }
    }

    /// <summary>Firma (PNG o JPG, idealmente con fondo transparente), cédula, certificado o escritura (PDF, JPG o PNG).</summary>
    private static async Task<IResult> SubirDocumento(Guid id, string tipo, IFormFile archivo, UsuarioActual usuario, ComparendosDb db, RegistroArchivos archivos, Auditoria auditoria, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var r = await db.RepresentantesLegales.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El representante");
        var archivoId = tipo switch
        {
            "firma" => await archivos.SubirAsync(tenant, "firma_representante", archivo, RegistroArchivos.Imagenes, ct, usuario.Id, 2L * 1024 * 1024).ConfigureAwait(false),
            "cedula" or "certificado" or "escritura" => await archivos.SubirAsync(tenant, $"{tipo}_representante", archivo, RegistroArchivos.Documentos, ct, usuario.Id).ConfigureAwait(false),
            _ => throw new ReglaException("DOCUMENTO_INVALIDO", "El documento debe ser firma, cedula, certificado o escritura."),
        };
        switch (tipo)
        {
            case "firma": r.FirmaArchivoId = archivoId; break;
            case "cedula": r.CedulaArchivoId = archivoId; break;
            case "certificado": r.CertificadoArchivoId = archivoId; break;
            default: r.EscrituraArchivoId = archivoId; break;
        }

        auditoria.Registrar(tenant, "representante_legal", r.Id, "ACTUALIZAR", new CambioCampo(tipo, null, archivo.FileName));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { archivoId });
    }

    /// <summary>Los OT donde la empresa tiene comparendos, más los que ya tienen permisos configurados.</summary>
    private static async Task<IResult> ListarPermisos(Guid? empresa, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa)!.Value;
        var conComparendos = await db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && c.Activo && c.OrganismoTransitoId != null)
            .GroupBy(c => c.OrganismoTransitoId!.Value).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct).ConfigureAwait(false);
        var filas = await db.PermisosOtEmpresa.AsNoTracking().Where(p => p.TenantId == tenant).ToListAsync(ct).ConfigureAwait(false);
        var ids = conComparendos.Keys.Concat(filas.Select(f => f.OrganismoTransitoId)).Distinct().ToList();
        var ots = await db.OrganismosTransito.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct).ConfigureAwait(false);
        var representantes = await db.RepresentantesLegales.AsNoTracking().Where(r => r.TenantId == tenant).ToDictionaryAsync(r => r.Id, r => r.Nombres, ct).ConfigureAwait(false);

        var lista = new List<PermisoOtDto>();
        foreach (var otId in ids)
        {
            var ot = ots[otId];
            var propias = filas.Where(f => f.OrganismoTransitoId == otId).ToList();
            if (propias.Count == 0)
                lista.Add(new PermisoOtDto(otId, ot.Nombre, ot.Municipio, null, null, true, true, !ot.PermiteCambioContraventor, conComparendos.GetValueOrDefault(otId)));
            lista.AddRange(propias.Select(f => new PermisoOtDto(otId, ot.Nombre, ot.Municipio, f.RepresentanteLegalId, f.RepresentanteLegalId is { } r ? representantes.GetValueOrDefault(r) : null,
                f.PermiteConsultaContraventor, f.PermiteCambioContraventor, !ot.PermiteCambioContraventor, conComparendos.GetValueOrDefault(otId))));
        }

        return Results.Ok(lista.OrderByDescending(x => x.ComparendosActivos).ThenBy(x => x.Organismo));
    }

    private static async Task<IResult> GuardarPermiso(CambioPermisoOt pedido, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        if (!await db.OrganismosTransito.AnyAsync(o => o.Id == pedido.OrganismoTransitoId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El organismo de tránsito");
        if (pedido.RepresentanteLegalId is { } rl && !await db.RepresentantesLegales.AnyAsync(r => r.Id == rl && r.TenantId == tenant, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El representante");

        var fila = await db.PermisosOtEmpresa.FirstOrDefaultAsync(p => p.TenantId == tenant && p.OrganismoTransitoId == pedido.OrganismoTransitoId && p.RepresentanteLegalId == pedido.RepresentanteLegalId, ct).ConfigureAwait(false);
        var antes = fila is null ? (true, true) : (fila.PermiteConsultaContraventor, fila.PermiteCambioContraventor);
        if (fila is null)
        {
            fila = new PermisoOtEmpresa { Id = Guid.CreateVersion7(), TenantId = tenant, OrganismoTransitoId = pedido.OrganismoTransitoId, RepresentanteLegalId = pedido.RepresentanteLegalId, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
            db.PermisosOtEmpresa.Add(fila);
        }

        (fila.PermiteConsultaContraventor, fila.PermiteCambioContraventor, fila.ActualizadoEn, fila.ActualizadoPor) =
            (pedido.PermiteConsultaContraventor, pedido.PermiteCambioContraventor, reloj.Ahora, usuario.Id);
        auditoria.Registrar(tenant, "permiso_ot_empresa", fila.Id, "ACTUALIZAR",
            Auditoria.Diferencias(("permite_consulta_contraventor", antes.Item1, pedido.PermiteConsultaContraventor), ("permite_cambio_contraventor", antes.Item2, pedido.PermiteCambioContraventor)));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { fila.Id });
    }
}
