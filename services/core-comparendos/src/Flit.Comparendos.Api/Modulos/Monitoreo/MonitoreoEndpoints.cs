using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Monitoreo;

/// <summary>
/// NITs y placas que vigila la gestora (HU #13458; ADO #12956, #12977). Se agregan, activan y desactivan; nunca se
/// borran. Una clave desactivada deja de consultarse y sus comparendos no se inactivan por eso (RN-035).
/// </summary>
public static class MonitoreoEndpoints
{
    public sealed record NitDto(Guid Id, string TipoDocumento, string NumeroBase, short? DigitoVerificacion, string Numero, string? NombreTitular, bool Activo, DateTimeOffset CreadoEn, int ComparendosActivos = 0,
        bool ConsultaContratoApi = false, bool ConsultaAutomatica = false);

    public sealed record PlacaDto(Guid Id, string Placa, bool Activo, string OrigenRegistro, DateTimeOffset CreadoEn, int ComparendosActivos = 0);

    public sealed record NuevoNit(string? TipoDocumento, string Numero, string? NombreTitular);

    public sealed record CambioNit(bool? Activo, string? NombreTitular);

    /// <summary>Placas nuevas; <c>Origen</c> = OCR cuando se agregan desde la revisión de un documento OCR (§3.4).</summary>
    public sealed record NuevasPlacas(IReadOnlyList<string> Placas, string? Origen = null);

    public sealed record CambioPlaca(bool Activo);

    /// <summary>Repetidas: las que venían más de una vez en el mismo envío (se agregan una sola vez, hallazgo B3-1).</summary>
    public sealed record ResultadoPlacas(IReadOnlyList<PlacaDto> Agregadas, IReadOnlyList<string> YaExistian, IReadOnlyList<string> Invalidas, IReadOnlyList<string> Repetidas);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/monitoreo");
        g.MapGet("/nits", ListarNits).RequirePermiso(Permisos.MonitoreoVer);
        g.MapPost("/nits", CrearNit).RequirePermiso(Permisos.MonitoreoGestionar);
        g.MapPatch("/nits/{id:guid}", CambiarNit).RequirePermiso(Permisos.MonitoreoGestionar);
        g.MapGet("/placas", ListarPlacas).RequirePermiso(Permisos.MonitoreoVer);
        g.MapPost("/placas", CrearPlacas).RequirePermiso(Permisos.MonitoreoGestionar);
        g.MapGet("/placas/plantilla", PlantillaPlacas).RequirePermiso(Permisos.MonitoreoGestionar);
        g.MapPost("/placas/archivo", CargarPlacas).RequirePermiso(Permisos.MonitoreoGestionar).DisableAntiforgery();
        g.MapPatch("/placas/{id:guid}", CambiarPlaca).RequirePermiso(Permisos.MonitoreoGestionar);
        g.MapGet("/{tipo}/{id:guid}/historial", Historial).RequirePermiso(Permisos.MonitoreoVer);
    }

    /// <summary>Historial de un NIT o una placa (§7.12): quién lo registró, activó o desactivó y cuándo.</summary>
    private static async Task<IResult> Historial(string tipo, Guid id, Guid? empresa, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa);
        var entidad = tipo switch
        {
            "nits" => "identificacion_monitoreada",
            "placas" => "placa_monitoreada",
            _ => throw ReglaException.NoEncontrado("El historial"),
        };
        var clave = id.ToString();
        var items = await db.AuditoriasConfiguracion.AsNoTracking().Where(a => a.TenantId == tenant && a.Entidad == entidad && a.EntidadId == clave)
            .OrderByDescending(a => a.RegistradoEn).Select(a => new { a.Id, a.TenantId, a.Entidad, a.EntidadId, a.Accion, a.Cambios, a.UsuarioNombre, a.RegistradoEn })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { total = items.Count, pagina = 1, tamano = items.Count, items });
    }

    internal static NitDto ANit(IdentificacionMonitoreada n) => new(
        n.Id, n.TipoDocumento.ToString(), n.NumeroBase, n.DigitoVerificacion,
        n.DigitoVerificacion is { } dv ? $"{n.NumeroBase}-{dv}" : n.NumeroBase, n.NombreTitular, n.Activo, n.CreadoEn, ConsultaContratoApi: n.ConsultaContratoApi);

    private static PlacaDto APlaca(PlacaMonitoreada p) => new(p.Id, p.Placa, p.Activo, p.OrigenRegistro, p.CreadoEn);

    private static async Task<IResult> ListarNits(Guid? empresa, string? buscar, bool? activo, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa);
        var q = db.IdentificacionesMonitoreadas.AsNoTracking().Where(n => n.TenantId == tenant);
        if (activo is { } a)
            q = q.Where(n => n.Activo == a);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = buscar.Trim();
            q = q.Where(n => n.NumeroBase.Contains(texto) || (n.NombreTitular != null && EF.Functions.ILike(n.NombreTitular, $"%{texto}%")));
        }

        var lista = await q.OrderByDescending(n => n.Activo).ThenBy(n => n.NumeroBase).ToListAsync(ct).ConfigureAwait(false);
        var activos = await db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && c.Activo && c.IdentificacionMonitoreadaId != null)
            .GroupBy(c => c.IdentificacionMonitoreadaId!.Value).Select(g => new { g.Key, Cantidad = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);
        // HU #13844: lo que de verdad pasa con el conductor de sus comparendos (la empresa lo ve; solo el SuperAdmin lo cambia).
        var config = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant, ct).ConfigureAwait(false);
        var automatica = config is not null && Contraventor.ConsultaPorApi.EmpresaLaUsa(config);
        return Results.Ok(lista.Select(n => ANit(n) with { ComparendosActivos = activos.GetValueOrDefault(n.Id), ConsultaAutomatica = automatica && n.ConsultaContratoApi }));
    }

    private static async Task<IResult> CrearNit(NuevoNit pedido, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var tipo = Enum.TryParse<TipoDocumento>(pedido.TipoDocumento ?? "NIT", ignoreCase: true, out var t) && Enum.IsDefined(t)
            ? t
            : throw new ReglaException("TIPO_DOCUMENTO_INVALIDO", "El tipo de documento no está en el catálogo (CC, TI, CE, NIT, PA, CD, RC, CV).");

        string numero;
        short? dv = null;
        if (tipo == TipoDocumento.NIT)
        {
            var separado = Nit.Separar(pedido.Numero) ?? throw new ReglaException("NIT_INVALIDO", "Escribe el NIT solo con números, con o sin dígito de verificación (900123456-7).");
            var esperado = Nit.DigitoVerificacion(separado.Base);
            if (separado.Dv is { } digitado && digitado != esperado)
                throw new ReglaException("DV_INVALIDO", $"El dígito de verificación de {separado.Base} es {esperado}, no {digitado}.");
            numero = separado.Base;
            dv = (short)esperado;
        }
        else
        {
            numero = new string((pedido.Numero ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
            if (numero.Length is 0 or > 15)
                throw new ReglaException("DOCUMENTO_INVALIDO", "Escribe un número de documento válido.");
        }

        if (await db.IdentificacionesMonitoreadas.AnyAsync(n => n.TenantId == tenant && n.TipoDocumento == tipo && n.NumeroBase == numero, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("YA_MONITOREADO", $"{tipo} {numero} ya está registrado. Si está inactivo, actívalo.");

        var nit = new IdentificacionMonitoreada
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            TipoDocumento = tipo,
            NumeroBase = numero,
            DigitoVerificacion = dv,
            NombreTitular = string.IsNullOrWhiteSpace(pedido.NombreTitular) ? null : pedido.NombreTitular.Trim(),
            CreadoEn = reloj.Ahora,
            CreadoPor = usuario.Id,
        };
        db.IdentificacionesMonitoreadas.Add(nit);
        auditoria.Registrar(tenant, "identificacion_monitoreada", nit.Id, "CREAR", new CambioCampo("numero", null, $"{tipo} {numero}"),
            new CambioCampo("titular", null, nit.NombreTitular)); // el historial muestra con qué titular se creó (B3-3)
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/monitoreo/nits/{nit.Id}", ANit(nit));
    }

    private static async Task<IResult> CambiarNit(Guid id, CambioNit pedido, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var nit = await db.IdentificacionesMonitoreadas.FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenant, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El NIT");
        var nombre = pedido.NombreTitular is null ? nit.NombreTitular : (string.IsNullOrWhiteSpace(pedido.NombreTitular) ? null : pedido.NombreTitular.Trim());
        var activo = pedido.Activo ?? nit.Activo;
        var cambios = Auditoria.Diferencias(("activo", nit.Activo, activo), ("nombre_titular", nit.NombreTitular, nombre));
        if (cambios.Length == 0)
            return Results.Ok(ANit(nit));

        nit.Activo = activo;
        nit.NombreTitular = nombre;
        nit.ActualizadoEn = reloj.Ahora;
        nit.ActualizadoPor = usuario.Id;
        auditoria.Registrar(tenant, "identificacion_monitoreada", nit.Id, pedido.Activo is null ? "ACTUALIZAR" : activo ? "ACTIVAR" : "DESACTIVAR", cambios);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ANit(nit));
    }

    private static async Task<IResult> ListarPlacas(Guid? empresa, string? buscar, bool? activo, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa);
        var q = db.PlacasMonitoreadas.AsNoTracking().Where(p => p.TenantId == tenant);
        if (activo is { } a)
            q = q.Where(p => p.Activo == a);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = new string(buscar.Where(char.IsAsciiLetterOrDigit).ToArray());
            q = q.Where(p => EF.Functions.ILike(p.Placa, $"%{texto}%"));
        }

        var lista = await q.OrderByDescending(p => p.Activo).ThenBy(p => p.Placa).ToListAsync(ct).ConfigureAwait(false);
        var activos = await db.Comparendos.AsNoTracking().Where(c => c.TenantId == tenant && c.Activo && c.PlacaMonitoreadaId != null)
            .GroupBy(c => c.PlacaMonitoreadaId!.Value).Select(g => new { g.Key, Cantidad = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct).ConfigureAwait(false);
        return Results.Ok(lista.Select(p => APlaca(p) with { ComparendosActivos = activos.GetValueOrDefault(p.Id) }));
    }

    /// <summary>Filas de la carga de placas por Excel (S-64, hallazgo B3-2).</summary>
    public const int MaximoFilasExcel = 1000;

    private static IResult PlantillaPlacas()
    {
        var hoja = new TabularWorkbookWriter.Sheet("Placas", [new TabularWorkbookWriter.Column("placa", 14)], [[Cell.Of("ABC123")], [Cell.Of("XYZ98F")]]);
        return Results.File(TabularWorkbookWriter.Write([hoja]), TabularWorkbookWriter.ContentType, "plantilla-placas.xlsx");
    }

    /// <summary>
    /// Carga de placas por Excel (S-64, hallazgo B3-2): primera hoja, una columna «placa», hasta 1.000 filas. Responde lo
    /// mismo que el pegado: agregadas, las que ya estaban, repetidas e inválidas.
    /// </summary>
    private static async Task<IResult> CargarPlacas(IFormFile archivo, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ReglaException("ARCHIVO_TIPO_INVALIDO", "Carga un archivo de Excel (.xlsx) con una columna «placa».");
        List<Dictionary<int, CeldaLeida>> filas;
        try
        {
            await using var stream = archivo.OpenReadStream();
            using var memoria = new MemoryStream();
            await stream.CopyToAsync(memoria, ct).ConfigureAwait(false);
            memoria.Position = 0;
            filas = LectorExcel.LeerPrimeraHoja(memoria);
        }
        catch (Exception ex) when (ex is InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or IOException)
        {
            throw new ReglaException("ARCHIVO_ILEGIBLE", "El archivo no se pudo leer como Excel.");
        }

        var columna = (filas.FirstOrDefault() ?? []).FirstOrDefault(kv => string.Equals(TextoNormalizado.De(kv.Value.Texto), "placa", StringComparison.OrdinalIgnoreCase)).Key;
        if (columna == 0)
            throw new ReglaException("FALTA_COLUMNA_PLACA", "El archivo no tiene la columna «placa» en la primera fila.");
        var placas = filas.Skip(1).Select(f => f.GetValueOrDefault(columna)?.Texto).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        if (placas.Count == 0)
            throw new ReglaException("PLACAS_REQUERIDAS", "El archivo no trae placas.");
        if (placas.Count > MaximoFilasExcel)
            throw new ReglaException("DEMASIADAS_PLACAS", $"El archivo tiene {placas.Count} placas: el máximo es {MaximoFilasExcel:N0}.");
        return Results.Ok(await AgregarAsync(placas, "ADMIN", usuario, db, auditoria, reloj, ct).ConfigureAwait(false));
    }

    /// <summary>Una o varias placas a la vez (pegadas de una hoja de cálculo): agrega las nuevas e informa el resto.</summary>
    private static async Task<IResult> CrearPlacas(NuevasPlacas pedido, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        if (pedido.Placas is not { Count: > 0 and <= 5000 })
            throw new ReglaException("PLACAS_REQUERIDAS", "Envía entre 1 y 5.000 placas.");
        var origen = (pedido.Origen ?? "ADMIN").Trim().ToUpperInvariant();
        if (origen is not ("ADMIN" or "OCR"))
            throw new ReglaException("ORIGEN_INVALIDO", "El origen de la placa es ADMIN u OCR.");
        return Results.Ok(await AgregarAsync(pedido.Placas, origen, usuario, db, auditoria, reloj, ct).ConfigureAwait(false));
    }

    private static async Task<ResultadoPlacas> AgregarAsync(IReadOnlyList<string> textos, string origen, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();

        var invalidas = new List<string>();
        var repetidas = new List<string>();
        var normalizadas = new List<string>();
        foreach (var texto in textos)
        {
            if (Placa.Normalizar(texto) is { } p)
            {
                if (!normalizadas.Contains(p))
                    normalizadas.Add(p);
                else if (!repetidas.Contains(p))
                    repetidas.Add(p);
            }
            else if (!string.IsNullOrWhiteSpace(texto))
            {
                invalidas.Add(texto.Trim());
            }
        }

        var existentes = await db.PlacasMonitoreadas.Where(p => p.TenantId == tenant && normalizadas.Contains(p.Placa)).Select(p => p.Placa).ToListAsync(ct).ConfigureAwait(false);
        var nuevas = normalizadas.Except(existentes).Select(p => new PlacaMonitoreada
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Placa = p,
            OrigenRegistro = origen,
            CreadoEn = reloj.Ahora,
            CreadoPor = usuario.Id,
        }).ToList();
        db.PlacasMonitoreadas.AddRange(nuevas);
        foreach (var p in nuevas)
            auditoria.Registrar(tenant, "placa_monitoreada", p.Id, "CREAR", new CambioCampo("placa", null, p.Placa));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ResultadoPlacas(nuevas.Select(APlaca).ToList(), existentes, invalidas, repetidas);
    }

    private static async Task<IResult> CambiarPlaca(Guid id, CambioPlaca pedido, UsuarioActual usuario, ComparendosDb db, Auditoria auditoria, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var placa = await db.PlacasMonitoreadas.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenant, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La placa");
        if (placa.Activo == pedido.Activo)
            return Results.Ok(APlaca(placa));

        auditoria.Registrar(tenant, "placa_monitoreada", placa.Id, pedido.Activo ? "ACTIVAR" : "DESACTIVAR", new CambioCampo("activo", placa.Activo, pedido.Activo));
        placa.Activo = pedido.Activo;
        placa.ActualizadoEn = reloj.Ahora;
        placa.ActualizadoPor = usuario.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(APlaca(placa));
    }
}
