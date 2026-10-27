using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Consentimientos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>Una regla del negocio incumplida, ya con el estado HTTP y el código estable que se le responde al ciudadano.</summary>
public sealed class ReglaDeNegocioException(int estado, string codigo, string titulo, string detalle, IReadOnlyDictionary<string, object?>? extensiones = null) : Exception(detalle)
{
    public int Estado { get; } = estado;

    public string Codigo { get; } = codigo;

    public string Titulo { get; } = titulo;

    public IReadOnlyDictionary<string, object?>? Extensiones { get; } = extensiones;

    public static ReglaDeNegocioException Invalido(ErroresDeCampo errores) => new(
        StatusCodes.Status422UnprocessableEntity, "datos_invalidos", "Datos inválidos", "Revise los datos marcados.",
        new Dictionary<string, object?> { ["errors"] = errores.Todos() });
}

/// <summary>Limpia lo que otra pieza guardó de una solicitud (p. ej. los archivos) cuando se cancela. Se suma por inyección.</summary>
public interface ILimpiadorDeSolicitud
{
    Task LimpiarAsync(Guid solicitudId, CancellationToken ct);
}

/// <summary>
/// El borrador del trámite (HU #14047, ADR-0075): crea la solicitud con la definición del tipo congelada y guarda por pasos lo
/// que el ciudadano escribe. Toda regla sale del <c>definicion_snapshot</c> de la solicitud, nunca de código por tipo. Los
/// datos personales entran y salen solo por <see cref="PersonaService"/>.
/// </summary>
public sealed partial class SolicitudesService(
    PersonaNaturalDb db,
    PersonaService personas,
    SesionService sesiones,
    PiiHasher hasher,
    DefinicionesService definiciones,
    CotizacionesService catalogo,
    IFachadaRequisitos requisitos,
    IEnumerable<ILimpiadorDeSolicitud> limpiadores,
    TimeProvider reloj,
    ConsentimientoVersiones versionesConsentimiento)
{
    public const string CodigoTipoNoDisponible = "tipo_no_disponible";
    public const string CodigoNoPermiteCambio = "estado_no_permite_cambio";
    public const string CodigoConflictoVersion = "conflicto_version";
    public const string TipoComprobantePago = "comprobante_pago";

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^\d{4,15}$")]
    private static partial Regex CedulaValida();

    /// <summary>Estados en los que el ciudadano todavía puede editar o cancelar (antes de entrar a la cola).</summary>
    public static bool EsEditable(string estado) => estado is EstadoSolicitud.Borrador or EstadoSolicitud.PendienteValidacion;

    // ── Abrir el trámite ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea la solicitud en <c>borrador</c> con el código y la versión de la definición y su snapshot, la huella de la cédula
    /// y la constancia del consentimiento, y emite la sesión que abre SOLO ese trámite.
    /// </summary>
    public async Task<SesionCreadaDto> CrearAsync(CrearSolicitudRequest? body, byte[]? ipHash, CancellationToken ct)
    {
        var errores = new ErroresDeCampo();
        if (body is null || string.IsNullOrWhiteSpace(body.TipoCodigo) || body.TipoCodigo.Length > 80)
            errores.Agregar("tipoCodigo", "Elija un tipo de trámite.");
        if (!Guid.TryParse(body?.OrganismoId, out var organismoId))
            errores.Agregar("organismoId", "Elija un organismo de tránsito.");
        var cedula = PiiHasher.NormalizarDocumento(body?.Cedula ?? string.Empty);
        if (!CedulaValida().IsMatch(cedula))
            errores.Agregar("cedula", "Escriba su número de documento.");
        if (errores.Hay)
            throw ReglaDeNegocioException.Invalido(errores);
        if (!body!.Consentimiento)
            throw new ReglaDeNegocioException(StatusCodes.Status400BadRequest, ConsultasEndpoints.CodigoConsentimiento, "Falta el consentimiento", "Acepte el tratamiento de sus datos personales para continuar.");

        // La versión del texto la valida el servidor contra las vigentes: una desconocida es 422, no se guarda tal cual.
        var versionConsentimiento = versionesConsentimiento.Normalizar(body.ConsentimientoVersion)
            ?? throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, ConsentimientoVersiones.CodigoVersionDesconocida, "Versión del consentimiento desconocida", "El texto de consentimiento que aceptó ya no está vigente. Recargue la página y vuelva a aceptarlo.");

        DefinicionTipoDto def;
        try
        {
            def = await definiciones.ObtenerAsync(body.TipoCodigo!.Trim(), organismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.Codigo == DefinicionesService.CodigoTipoNoDisponible)
        {
            throw TipoNoDisponible();
        }

        var (placa, vin) = ReglasBorrador.ValidarVehiculo(def, body.Placa, body.Vin, errores);
        var organismo = (await catalogo.OrganismosAsync(ct).ConfigureAwait(false)).FirstOrDefault(o => string.Equals(o.Id, organismoId.ToString(), StringComparison.OrdinalIgnoreCase));
        if (organismo is null)
            throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, "organismo_no_habilitado", "Organismo no habilitado", "El organismo de tránsito no está habilitado.");
        if (errores.Hay)
            throw ReglaDeNegocioException.Invalido(errores);

        var ahora = reloj.GetUtcNow();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformTenants.PersonaNatural,
            CodigoPublico = await SiguienteCodigoAsync(ahora, ct).ConfigureAwait(false),
            TipoTramiteCodigo = def.Code,
            DefinicionVersion = def.Version,
            DefinicionSnapshot = DefinicionesService.Serializar(def),
            OrganismoId = organismoId,
            OrganismoNombre = organismo.Nombre.Length > 200 ? organismo.Nombre[..200] : organismo.Nombre,
            Placa = placa,
            Vin = vin,
            Estado = EstadoSolicitud.Borrador,
            CedulaSolicitanteHash = hasher.HashCedula(cedula),
            CorrelationId = Guid.CreateVersion7(),
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };
        db.Add(solicitud);
        db.Add(new Consentimiento
        {
            Id = Guid.CreateVersion7(),
            SolicitudId = solicitud.Id,
            DocumentoHash = solicitud.CedulaSolicitanteHash,
            TextoVersion = versionConsentimiento,
            AceptadoEn = ahora,
            IpHash = ipHash,
        });
        AgregarHistorial(solicitud, null, EstadoSolicitud.Borrador, "solicitud_creada", ahora);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var sesion = await sesiones.IniciarAsync(solicitud.Id, ipHash, ct).ConfigureAwait(false);
        return new SesionCreadaDto(sesion.Token, sesion.ExpiraEn, solicitud.Id, solicitud.CodigoPublico, solicitud.Estado, solicitud.TipoTramiteCodigo, solicitud.DefinicionVersion);
    }

    /// <summary>Radicado visible <c>PN-AAAA-NNNNNN</c>: el consecutivo sale de una secuencia, no se repite entre réplicas.</summary>
    private async Task<string> SiguienteCodigoAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var n = await db.Database.SqlQuery<long>($"SELECT nextval('personanatural.public_code_seq') AS \"Value\"").SingleAsync(ct).ConfigureAwait(false);
        return $"PN-{ahora.Year}-{n:D6}";
    }

    // ── Leer (rehidratar) ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El borrador completo para rehidratar el wizard. Si la definición del tipo cambió mientras estaba guardado, conserva lo
    /// que el ciudadano ya escribió, descarta solo los campos que la definición ya no tiene y deja los nuevos como pendientes.
    /// </summary>
    public async Task<SolicitudDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var solicitud = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false) ?? throw NoEncontrada();
        var cambio = await ReconciliarAsync(solicitud, ct).ConfigureAwait(false);
        return await ConstruirAsync(solicitud, conPersonas: true, cambio, ct).ConfigureAwait(false);
    }

    private async Task<DefinicionCambioDto?> ReconciliarAsync(SolicitudTramite s, CancellationToken ct)
    {
        if (!EsEditable(s.Estado) || DefinicionesService.Leer(s.DefinicionSnapshot) is not { } anterior)
            return null;

        DefinicionTipoDto actual;
        try
        {
            actual = await definiciones.ObtenerAsync(s.TipoTramiteCodigo, s.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException)
        {
            return null; // sin fachada o sin tipo: se sigue con lo congelado; radicar vuelve a revisar la disponibilidad
        }

        if (actual.Huella == anterior.Huella && actual.Version == s.DefinicionVersion)
            return null;

        var clavesActuales = actual.Pasos.SelectMany(p => p.Campos).Select(c => c.FieldKey).ToHashSet(StringComparer.Ordinal);
        var clavesAnteriores = anterior.Pasos.SelectMany(p => p.Campos).Select(c => c.FieldKey).ToHashSet(StringComparer.Ordinal);
        var campos = LeerCampos(s.FieldValues);
        var descartados = campos.Keys.Where(k => !clavesActuales.Contains(k)).Order(StringComparer.Ordinal).ToList();
        foreach (var k in descartados)
            campos.Remove(k);
        var versionAnterior = s.DefinicionVersion;

        s.FieldValues = JsonSerializer.Serialize(campos, JsonOpciones);
        s.DefinicionSnapshot = DefinicionesService.Serializar(actual);
        s.DefinicionVersion = actual.Version;
        Tocar(s);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(s).ReloadAsync(ct).ConfigureAwait(false);
            return null; // otra pestaña lo reconcilió primero
        }

        return new DefinicionCambioDto(versionAnterior, actual.Version, descartados, [.. clavesActuales.Where(k => !clavesAnteriores.Contains(k)).Order(StringComparer.Ordinal)]);
    }

    // ── Guardar por pasos ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Actores por rol: cada rol enviado REEMPLAZA al anterior (las posiciones que ya no vienen se retiran). Cambiar el
    /// documento de una persona borra su validación de identidad: la validación es de la persona que la hizo.
    /// </summary>
    public Task<SolicitudDto> GuardarActoresAsync(Guid id, ActoresRequest? body, CancellationToken ct) =>
        EnTransaccionAsync(async ct2 =>
        {
            var (s, def) = await CargarParaEditarAsync(id, body?.Version, ct2).ConfigureAwait(false);
            var errores = new ErroresDeCampo();
            var validados = ReglasBorrador.ValidarActores(def, body?.Actores, errores);
            if (errores.Hay)
                throw ReglaDeNegocioException.Invalido(errores);

            // Una misma persona no puede ser vendedora y compradora: se compara por huella con las partes que no se reemplazan.
            var roles = validados.Select(a => a.Rol).Distinct(StringComparer.Ordinal).ToList();
            var otras = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == id && !roles.Contains(p.Parte)).Select(p => p.DocumentoHash).ToListAsync(ct2).ConfigureAwait(false);
            foreach (var a in validados.Where(a => otras.Any(h => PiiHasher.Iguales(h, hasher.HashCedula(a.Documento)))))
                errores.Agregar($"actores[{a.Indice}].numeroDocumento", "Esa persona ya está en otro rol del trámite.");
            if (errores.Hay)
                throw ReglaDeNegocioException.Invalido(errores);

            foreach (var rol in roles)
            {
                var posiciones = validados.Where(a => a.Rol == rol).Select(a => a.Ordinal).ToList();
                await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id && p.Parte == rol && !posiciones.Contains(p.Ordinal)).ExecuteDeleteAsync(ct2).ConfigureAwait(false);
            }

            foreach (var a in validados)
                await personas.GuardarAsync(id, new DatosPersona(a.Rol, a.Ordinal, a.TipoDocumento, a.Documento, a.Nombre, a.Correo, a.Celular, a.Direccion), ct2).ConfigureAwait(false);

            await GuardarAsync(s, ct2).ConfigureAwait(false);
            return await ConstruirAsync(s, conPersonas: true, null, ct2).ConfigureAwait(false);
        }, ct);

    /// <summary>Valores de los campos del tipo por <c>field_key</c>: se mezclan con los ya guardados y <c>null</c> borra un campo opcional.</summary>
    public async Task<SolicitudDto> GuardarCamposAsync(Guid id, CamposRequest? body, CancellationToken ct)
    {
        var (s, def) = await CargarParaEditarAsync(id, body?.Version, ct).ConfigureAwait(false);
        var errores = new ErroresDeCampo();
        var nuevos = ReglasBorrador.ValidarCampos(def, body?.Valores, errores);
        if (errores.Hay)
            throw ReglaDeNegocioException.Invalido(errores);

        var campos = LeerCampos(s.FieldValues);
        foreach (var (clave, valor) in nuevos)
        {
            if (valor is null)
                campos.Remove(clave);
            else
                campos[clave] = valor;
        }

        s.FieldValues = JsonSerializer.Serialize(campos, JsonOpciones);
        await GuardarAsync(s, ct).ConfigureAwait(false);
        return await ConstruirAsync(s, conPersonas: false, null, ct).ConfigureAwait(false);
    }

    public async Task<SolicitudDto> GuardarComercialAsync(Guid id, ComercialRequest? body, CancellationToken ct)
    {
        var (s, def) = await CargarParaEditarAsync(id, body?.Version, ct).ConfigureAwait(false);
        var errores = new ErroresDeCampo();
        var (comercial, prenda) = ReglasBorrador.ValidarComercial(def, body, errores);
        if (errores.Hay)
            throw ReglaDeNegocioException.Invalido(errores);

        var datos = DatosSolicitud.Leer(s.Datos);
        s.Datos = (datos with { Comercial = comercial ?? datos.Comercial, Prenda = prenda ?? datos.Prenda }).Serializar();
        await GuardarAsync(s, ct).ConfigureAwait(false);
        return await ConstruirAsync(s, conPersonas: false, null, ct).ConfigureAwait(false);
    }

    public async Task<SolicitudDto> GuardarRequisitosAsync(Guid id, RequisitosRequest? body, CancellationToken ct)
    {
        var (s, def) = await CargarParaEditarAsync(id, body?.Version, ct).ConfigureAwait(false);
        var errores = new ErroresDeCampo();
        var requisitosDatos = ReglasBorrador.ValidarRequisitos(def, body, errores);
        if (errores.Hay)
            throw ReglaDeNegocioException.Invalido(errores);

        s.Datos = (DatosSolicitud.Leer(s.Datos) with { Requisitos = requisitosDatos }).Serializar();
        await GuardarAsync(s, ct).ConfigureAwait(false);
        return await ConstruirAsync(s, conPersonas: false, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Diagnóstico: los requisitos previos del tipo, evaluados por FLIT 2 con la placa o VIN de la solicitud. Del propietario
    /// solo se usa el documento que llega en la petición, y no se guarda: del resultado solo queda etiqueta y estado.
    /// </summary>
    public async Task<DiagnosticoDto> DiagnosticarAsync(Guid id, DiagnosticoRequest? body, CancellationToken ct)
    {
        var (s, _) = await CargarParaEditarAsync(id, null, ct).ConfigureAwait(false);
        var tipoDoc = body?.Propietario?.TipoDocumento?.Trim().ToUpperInvariant();
        var doc = body?.Propietario?.NumeroDocumento is { } n ? PiiHasher.NormalizarDocumento(n) : null;
        var resultado = await requisitos.EvaluarAsync(s.TipoTramiteCodigo, s.OrganismoId.ToString(), s.Placa, s.Vin, tipoDoc, doc, ct).ConfigureAwait(false);

        s.Datos = (DatosSolicitud.Leer(s.Datos) with { Diagnostico = new DiagnosticoDatos(reloj.GetUtcNow(), resultado.PuedeContinuar, resultado.Requisitos) }).Serializar();
        await GuardarAsync(s, ct).ConfigureAwait(false);
        return resultado;
    }

    // ── Cancelar ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancela antes de entrar a la cola: la solicitud queda <c>cancelada</c>, sus sesiones se revocan y los datos personales
    /// que el ciudadano escribió se borran (no hay razón para conservarlos), con la traza en la auditoría.
    /// </summary>
    public Task<CancelacionDto> CancelarAsync(Guid id, CancellationToken ct) =>
        EnTransaccionAsync(async ct2 =>
        {
            var s = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(x => x.Id == id, ct2).ConfigureAwait(false) ?? throw NoEncontrada();
            if (!EsEditable(s.Estado))
                throw NoPermiteCambio(s.Estado);

            var ahora = reloj.GetUtcNow();
            AgregarHistorial(s, s.Estado, EstadoSolicitud.Cancelada, "cancelada_por_el_ciudadano", ahora);
            s.Estado = EstadoSolicitud.Cancelada;
            Tocar(s);
            await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id).ExecuteDeleteAsync(ct2).ConfigureAwait(false);
            foreach (var limpiador in limpiadores)
                await limpiador.LimpiarAsync(id, ct2).ConfigureAwait(false);
            db.Add(new AccesoPiiLog { Actor = "sesion", Accion = "cancelada_por_el_ciudadano", SolicitudId = id, OcurrioEn = ahora });
            await GuardarAsync(s, ct2, tocar: false).ConfigureAwait(false);
            await sesiones.RevocarDeSolicitudAsync(id, ct2).ConfigureAwait(false);
            return new CancelacionDto(id, s.Estado);
        }, ct);

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Carga la solicitud para editarla: que exista, que no haya entrado a la cola, que la versión sea la que el cliente leyó y
    /// que el tipo siga disponible. La definición con la que se valida es la congelada, no la viva.
    /// </summary>
    internal async Task<(SolicitudTramite Solicitud, DefinicionTipoDto Definicion)> CargarParaEditarAsync(Guid id, long? version, CancellationToken ct)
    {
        var s = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw NoEncontrada();
        if (!EsEditable(s.Estado))
            throw NoPermiteCambio(s.Estado);
        if (version is { } v && v != s.RowVersion)
            throw ConflictoVersion(s.RowVersion);

        try
        {
            await definiciones.ObtenerAsync(s.TipoTramiteCodigo, s.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.Codigo == DefinicionesService.CodigoTipoNoDisponible)
        {
            throw TipoNoDisponible();
        }
        catch (FachadaException)
        {
            // Sin fachada no se bloquea el borrador: guardar lo que escribe el ciudadano no depende de FLIT 2.
        }

        var def = DefinicionesService.Leer(s.DefinicionSnapshot)
            ?? throw new ReglaDeNegocioException(StatusCodes.Status409Conflict, "definicion_obsoleta", "Definición no disponible", "La definición de este trámite ya no se puede leer.");
        return (s, def);
    }

    internal async Task GuardarAsync(SolicitudTramite s, CancellationToken ct, bool tocar = true)
    {
        if (tocar)
            Tocar(s);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            var vigente = await db.Set<SolicitudTramite>().AsNoTracking().Where(x => x.Id == s.Id).Select(x => x.RowVersion).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            throw ConflictoVersion(vigente);
        }
    }

    private void Tocar(SolicitudTramite s)
    {
        s.RowVersion++;
        s.UpdatedAt = reloj.GetUtcNow();
    }

    private void AgregarHistorial(SolicitudTramite s, string? desde, string hasta, string motivo, DateTimeOffset ahora) =>
        db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = desde, Hasta = hasta, Origen = "bff", Motivo = motivo, OcurrioEn = ahora });

    /// <summary>La transacción propia exige correr dentro de la estrategia de ejecución (reintentos de Npgsql).</summary>
    private Task<T> EnTransaccionAsync<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async ct2 =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct2).ConfigureAwait(false);
            var resultado = await trabajo(ct2).ConfigureAwait(false);
            await tx.CommitAsync(ct2).ConfigureAwait(false);
            return resultado;
        }, ct);

    private static Dictionary<string, string> LeerCampos(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpciones) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>El borrador en su forma de respuesta. Con <paramref name="conPersonas"/> descifra las personas y deja la lectura en la auditoría.</summary>
    internal async Task<SolicitudDto> ConstruirAsync(SolicitudTramite s, bool conPersonas, DefinicionCambioDto? cambio, CancellationToken ct)
    {
        var def = DefinicionesService.Leer(s.DefinicionSnapshot) ?? DefinicionesService.Vacia(s.TipoTramiteCodigo, s.DefinicionVersion);
        var filas = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == s.Id)
            .OrderBy(p => p.Parte).ThenBy(p => p.Ordinal).Select(p => new { p.Parte, p.Ordinal, p.ValidacionEstado }).ToListAsync(ct).ConfigureAwait(false);
        var adjuntos = await db.Set<Adjunto>().AsNoTracking().Where(a => a.SolicitudId == s.Id).OrderBy(a => a.CreatedAt)
            .Select(a => new AdjuntoDto(a.Id, a.Tipo, a.NombreArchivo, a.MimeType, a.TamanoBytes, a.Sha256, a.CreatedAt)).ToListAsync(ct).ConfigureAwait(false);
        var tiposAdjuntos = adjuntos.Select(a => a.Tipo).ToList();

        var detalle = conPersonas && filas.Count > 0
            ? (await personas.LeerAsync(s.Id, "sesion", ct).ConfigureAwait(false)).ToDictionary(p => (p.Parte, p.Ordinal))
            : [];
        var actores = filas.Select(f =>
        {
            detalle.TryGetValue((f.Parte, f.Ordinal), out var d);
            return new ActorEstadoDto(f.Parte, f.Ordinal, f.ValidacionEstado, d?.TipoDocumento, d?.Documento, d?.Nombre, d?.Correo, d?.Celular, d?.Direccion);
        }).ToList();

        var campos = LeerCampos(s.FieldValues);
        var datos = DatosSolicitud.Leer(s.Datos);
        var (pasos, pendientes) = ReglasBorrador.Completitud(
            def, campos, datos, [.. filas.Select(f => (f.Parte, f.Ordinal))],
            tiposAdjuntos.ToHashSet(StringComparer.Ordinal), tiposAdjuntos.Contains(TipoComprobantePago));

        return new SolicitudDto(
            s.Id, s.CodigoPublico, s.Estado, s.TipoTramiteCodigo, s.DefinicionVersion, def.Huella, s.RowVersion, s.OrganismoId, s.OrganismoNombre,
            s.Placa, s.Vin, campos, datos, actores, adjuntos, pasos, pendientes, pendientes.Count == 0, cambio);
    }

    internal static ReglaDeNegocioException NoEncontrada() =>
        new(StatusCodes.Status404NotFound, SesionEndpoints.CodigoNoEncontrada, "No encontrado", SesionEndpoints.MensajeNoEncontrada);

    internal static ReglaDeNegocioException NoPermiteCambio(string estado) =>
        new(StatusCodes.Status409Conflict, CodigoNoPermiteCambio, "No se puede cambiar", "Este trámite ya fue enviado o cancelado y no admite cambios.",
            new Dictionary<string, object?> { ["estado"] = estado });

    internal static ReglaDeNegocioException ConflictoVersion(long vigente) =>
        new(StatusCodes.Status409Conflict, CodigoConflictoVersion, "Cambió mientras lo editaba", "El trámite se modificó desde otra pestaña. Vuelva a cargarlo.",
            new Dictionary<string, object?> { ["version"] = vigente });

    internal static ReglaDeNegocioException TipoNoDisponible() =>
        new(StatusCodes.Status409Conflict, CodigoTipoNoDisponible, "Trámite no disponible", "Este tipo de trámite ya no está disponible en el portal.");
}
