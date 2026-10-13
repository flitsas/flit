using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Pagos;

/// <summary>
/// Registro de pagos (HU #13493; ADO #12981; §6.19). El pago es una marca interna de quién asumió el comparendo: no
/// detiene ningún flujo jurídico (RN-160), saca al comparendo de los avisos y no admite DP nuevo [Excel J13]. Un pago por
/// comparendo, sin pagos parciales ni anulación (RN-162, RN-164); el valor puede diferir del comparendo (RN-163).
/// </summary>
public static class PagosEndpoints
{
    public const int MaximoFilas = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Encabezados = ["tipo_pago", "placa", "numero_comparendo", "valor", "fecha_pago"];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/comparendos/{id:guid}/pago", Registrar).RequirePermiso(Permisos.PagosRegistrar).DisableAntiforgery();
        api.MapGet("/pagos/plantilla", Plantilla).RequirePermiso(Permisos.PagosCargarMasivo);
        api.MapPost("/pagos/cargas", Cargar).RequirePermiso(Permisos.PagosCargarMasivo).DisableAntiforgery();
        api.MapGet("/pagos/cargas", Cargas).RequirePermiso(Permisos.PagosCargarMasivo);
        api.MapGet("/pagos/cargas/{id:guid}", Carga).RequirePermiso(Permisos.PagosCargarMasivo);
        api.MapGet("/pagos/cargas/{id:guid}/errores", ErroresExcel).RequirePermiso(Permisos.PagosCargarMasivo);
    }

    private static Pago Aplicar(Comparendo c, TipoPago tipo, decimal valor, DateOnly fecha, Guid? comprobante, OrigenPago origen, Guid? carga, Guid usuario, Timeline timeline, Actor actor, DateTimeOffset ahora)
    {
        var pago = new Pago
        {
            Id = Guid.CreateVersion7(), TenantId = c.TenantId, ComparendoId = c.Id, TipoPago = tipo, ValorPagado = valor, FechaPago = fecha, ComprobanteArchivoId = comprobante,
            Origen = origen, CargaMasivaId = carga, RegistradoPor = usuario, RegistradoPorNombre = actor.Nombre, RegistradoEn = ahora,
        };
        c.Pagado = true;
        c.Gestionado = true;
        timeline.Agregar(c, TipoEventoTimeline.PagoRegistrado,
            $"Pago registrado ({Nombre(tipo)}) por {Plantillas.Formato.Pesos(valor)} el {Plantillas.Formato.Fecha(fecha)}{(origen == OrigenPago.MasivoExcel ? " desde la carga masiva" : string.Empty)}.",
            actor, new { tipo = tipo.ToString(), valor, fecha }, "pago", pago.Id);
        return pago;
    }

    public static string Nombre(TipoPago t) => t switch
    {
        TipoPago.CC => "CC: cambio de contraventor",
        TipoPago.PC => "PC: pagó el contraventor",
        _ => "BC: banco / empresa",
    };

    /// <summary>Formulario: <c>tipo</c> (CC, PC o BC), <c>valor</c>, <c>fecha</c> y <c>comprobante</c> opcional (PDF, JPG o PNG).</summary>
    private static async Task<IResult> Registrar(Guid id, HttpRequest request, AccesoComparendo acceso, RegistroArchivos archivos, Timeline timeline, UsuarioActual usuario, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        if (!Enum.TryParse<TipoPago>(form["tipo"].ToString(), true, out var tipo) || !Enum.IsDefined(tipo))
            throw new ReglaException("TIPO_PAGO_INVALIDO", "Tipo de pago inválido: debe ser CC, PC o BC.");
        var valor = LectorExcel.Valor(new CeldaLeida(form["valor"].ToString(), null));
        if (valor is not > 0)
            throw new ReglaException("VALOR_INVALIDO", "El valor pagado debe ser mayor que cero.");
        if (!DateOnly.TryParse(form["fecha"].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var fecha) || fecha > reloj.Hoy)
            throw new ReglaException("FECHA_INVALIDA", "La fecha de pago no puede ser futura.");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        if (await db.Pagos.AnyAsync(p => p.ComparendoId == id, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("YA_PAGADO", "El comparendo ya tiene un pago registrado (no hay pagos parciales ni anulación).");

        var comprobante = form.Files.GetFile("comprobante") is { } f
            ? await archivos.SubirAsync(c.TenantId, "comprobante_pago", f, RegistroArchivos.Documentos, ct, usuario.Id).ConfigureAwait(false)
            : (Guid?)null;
        var pago = Aplicar(c, tipo, valor.Value, fecha, comprobante, OrigenPago.Manual, null, usuario.Id, timeline, Actor.De(usuario), reloj.Ahora);
        db.Pagos.Add(pago);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { pago.Id, diferencia = c.ValorTotal is { } v && v != valor ? valor - v : null });
    }

    private static IResult Plantilla()
    {
        var hoja = new TabularWorkbookWriter.Sheet("Pagos", Encabezados.Select(e => new TabularWorkbookWriter.Column(e, 20)).ToList(),
            [[Cell.Of("BC"), Cell.Of("ABC123"), Cell.Of("11001000000012345678"), Cell.Of(302050), Cell.Of(new DateOnly(2026, 10, 9))]]);
        return Results.File(TabularWorkbookWriter.Write([hoja]), TabularWorkbookWriter.ContentType, "plantilla-pagos.xlsx");
    }

    private sealed record FilaPago(int Numero, string? Tipo, string? Placa, string? NumeroComparendo, decimal? Valor, DateOnly? Fecha);

    /// <summary>
    /// Carga masiva (§6.19): primera hoja, encabezados exactos (sin importar mayúsculas ni tildes), hasta 1.000 filas.
    /// Las filas válidas se aplican; las inválidas vuelven con número de fila y error (y en un Excel descargable).
    /// </summary>
    private static async Task<IResult> Cargar(IFormFile archivo, UsuarioActual usuario, ComparendosDb db, RegistroArchivos archivos, Timeline timeline, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ReglaException("ARCHIVO_TIPO_INVALIDO", "Carga un archivo de Excel (.xlsx) con la plantilla de pagos.");
        await using var stream = archivo.OpenReadStream();
        using var memoria = new MemoryStream();
        await stream.CopyToAsync(memoria, ct).ConfigureAwait(false);
        var carga = new CargaMasivaPagos { Id = Guid.CreateVersion7(), TenantId = tenant, NombreArchivo = archivo.FileName, CargadoPor = usuario.Id, CargadoPorNombre = usuario.Nombre, CargadoEn = reloj.Ahora };
        carga.ArchivoId = await archivos.GuardarAsync(tenant, "carga_pagos", archivo.FileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", memoria.ToArray(), ct, usuario.Id).ConfigureAwait(false);
        db.CargasMasivasPagos.Add(carga);

        List<Dictionary<int, CeldaLeida>> filas;
        try
        {
            memoria.Position = 0;
            filas = LectorExcel.LeerPrimeraHoja(memoria);
        }
        catch (Exception ex) when (ex is InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or IOException)
        {
            return await RechazarAsync(db, carga, "El archivo no se pudo leer como Excel.", reloj, ct).ConfigureAwait(false);
        }

        var encabezado = filas.FirstOrDefault() ?? [];
        var columnas = Encabezados.ToDictionary(e => e, e => encabezado.FirstOrDefault(kv => string.Equals(TextoNormalizado.De(kv.Value.Texto).Replace(' ', '_'), e, StringComparison.OrdinalIgnoreCase)).Key);
        if (columnas.Any(kv => kv.Value == 0))
            return await RechazarAsync(db, carga, $"Faltan columnas: {string.Join(", ", columnas.Where(kv => kv.Value == 0).Select(kv => kv.Key))}.", reloj, ct).ConfigureAwait(false);

        var datos = filas.Skip(1).Select((f, i) => (Fila: f, Numero: i + 2)).Where(x => x.Fila.Values.Any(c => !string.IsNullOrWhiteSpace(c.Texto))).ToList();
        if (datos.Count > MaximoFilas)
            return await RechazarAsync(db, carga, $"El archivo tiene {datos.Count} filas: el máximo es {MaximoFilas}.", reloj, ct).ConfigureAwait(false);

        CeldaLeida? En(Dictionary<int, CeldaLeida> f, string col) => f.GetValueOrDefault(columnas[col]);
        var leidas = datos.Select(x => new FilaPago(x.Numero, En(x.Fila, "tipo_pago")?.Texto.Trim(), En(x.Fila, "placa")?.Texto, En(x.Fila, "numero_comparendo")?.Texto,
            LectorExcel.Valor(En(x.Fila, "valor")), LectorExcel.Fecha(En(x.Fila, "fecha_pago")))).ToList();
        var numeros = leidas.Select(f => NumeroComparendo.Normalizar(f.NumeroComparendo)).OfType<string>().Distinct().ToList();
        var comparendos = await db.Comparendos.Where(c => c.TenantId == tenant && numeros.Contains(c.NumeroNormalizado)).ToDictionaryAsync(c => c.NumeroNormalizado, ct).ConfigureAwait(false);
        var ids = comparendos.Values.Select(c => c.Id).ToList();
        var yaPagados = await db.Pagos.Where(p => ids.Contains(p.ComparendoId)).Select(p => p.ComparendoId).ToHashSetAsync(ct).ConfigureAwait(false);
        var vistos = new Dictionary<string, int>();
        var actor = Actor.De(usuario);

        foreach (var f in leidas)
        {
            var errores = new List<string>();
            if (!Enum.TryParse<TipoPago>(f.Tipo, true, out var tipo) || !Enum.IsDefined(tipo))
                errores.Add("Tipo de pago inválido: debe ser CC, PC o BC");
            var numero = NumeroComparendo.Normalizar(f.NumeroComparendo);
            var c = numero is null ? null : comparendos.GetValueOrDefault(numero);
            if (c is null)
                errores.Add("Comparendo no encontrado");
            else if (Placa.Normalizar(f.Placa) is not { } placa || placa != c.Placa)
                errores.Add("La placa no corresponde al comparendo");
            if (f.Valor is not > 0)
                errores.Add("Valor inválido");
            if (f.Fecha is not { } fecha || fecha > reloj.Hoy)
                errores.Add("Fecha inválida");
            if (c is not null && yaPagados.Contains(c.Id))
                errores.Add("El comparendo ya tiene un pago registrado");
            if (numero is not null && vistos.TryGetValue(numero, out var primera))
                errores.Add($"Comparendo repetido en el archivo (fila {primera})");
            else if (numero is not null)
                vistos[numero] = f.Numero;

            var fila = new CargaMasivaPagosFila
            {
                Id = Guid.CreateVersion7(), CargaId = carga.Id, NumeroFila = f.Numero, Valida = errores.Count == 0, Errores = errores,
                Datos = JsonSerializer.Serialize(new { tipoPago = f.Tipo, placa = f.Placa, numeroComparendo = f.NumeroComparendo, valor = f.Valor, fechaPago = f.Fecha }, Json),
            };
            if (fila.Valida)
            {
                var pago = Aplicar(c!, tipo, f.Valor!.Value, f.Fecha!.Value, null, OrigenPago.MasivoExcel, carga.Id, usuario.Id, timeline, actor, reloj.Ahora);
                db.Pagos.Add(pago);
                yaPagados.Add(c!.Id);
                fila.PagoId = pago.Id;
            }

            db.CargaMasivaPagosFilas.Add(fila);
        }

        carga.TotalFilas = leidas.Count;
        carga.FilasValidas = db.CargaMasivaPagosFilas.Local.Count(x => x.CargaId == carga.Id && x.Valida);
        carga.FilasInvalidas = carga.TotalFilas - carga.FilasValidas;
        carga.Estado = carga.FilasInvalidas == 0 ? EstadoCargaMasiva.Procesada : EstadoCargaMasiva.ProcesadaConErrores;
        carga.ProcesadoEn = reloj.Ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(await ResumenAsync(db, carga.Id, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> RechazarAsync(ComparendosDb db, CargaMasivaPagos carga, string motivo, Reloj reloj, CancellationToken ct)
    {
        carga.Estado = EstadoCargaMasiva.Rechazada;
        carga.ProcesadoEn = reloj.Ahora;
        db.CargaMasivaPagosFilas.Add(new CargaMasivaPagosFila { Id = Guid.CreateVersion7(), CargaId = carga.Id, NumeroFila = 0, Errores = [motivo] });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        throw new ReglaException("CARGA_RECHAZADA", motivo);
    }

    private static async Task<object> ResumenAsync(ComparendosDb db, Guid id, CancellationToken ct)
    {
        var carga = await db.CargasMasivasPagos.AsNoTracking().FirstAsync(c => c.Id == id, ct).ConfigureAwait(false);
        var filas = await db.CargaMasivaPagosFilas.AsNoTracking().Where(f => f.CargaId == id && f.NumeroFila > 0).OrderBy(f => f.NumeroFila)
            .Select(f => new { f.NumeroFila, f.Valida, f.Errores, f.Datos }).ToListAsync(ct).ConfigureAwait(false);
        // Una carga rechazada completa guarda su motivo en la fila 0.
        var motivoRechazo = carga.Estado == EstadoCargaMasiva.Rechazada
            ? (await db.CargaMasivaPagosFilas.AsNoTracking().Where(f => f.CargaId == id && f.NumeroFila == 0).Select(f => f.Errores).FirstOrDefaultAsync(ct).ConfigureAwait(false))?.FirstOrDefault()
            : null;
        return new { carga.Id, carga.NombreArchivo, estado = carga.Estado.ToString(), motivoRechazo, carga.TotalFilas, carga.FilasValidas, carga.FilasInvalidas, carga.CargadoEn, filas };
    }

    private static async Task<IResult> Cargas(UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(null, obligatoria: false);
        return Results.Ok(await db.CargasMasivasPagos.AsNoTracking().Where(c => tenant == null || c.TenantId == tenant).OrderByDescending(c => c.CargadoEn).Take(50)
            .Select(c => new
            {
                c.Id, c.NombreArchivo, estado = c.Estado.ToString(), c.TotalFilas, c.FilasValidas, c.FilasInvalidas, c.CargadoPor, c.CargadoPorNombre, c.CargadoEn,
                motivoRechazo = db.CargaMasivaPagosFilas.Where(f => f.CargaId == c.Id && f.NumeroFila == 0).Select(f => f.Errores[0]).FirstOrDefault(),
            }).ToListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Carga(Guid id, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        if (!await db.CargasMasivasPagos.AnyAsync(c => c.Id == id && (usuario.EsSuperAdmin || c.TenantId == usuario.EmpresaId), ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La carga");
        return Results.Ok(await ResumenAsync(db, id, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ErroresExcel(Guid id, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        if (!await db.CargasMasivasPagos.AnyAsync(c => c.Id == id && (usuario.EsSuperAdmin || c.TenantId == usuario.EmpresaId), ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La carga");
        var filas = await db.CargaMasivaPagosFilas.AsNoTracking().Where(f => f.CargaId == id && !f.Valida && f.NumeroFila > 0).OrderBy(f => f.NumeroFila).ToListAsync(ct).ConfigureAwait(false);
        var hoja = new TabularWorkbookWriter.Sheet("Filas con error",
            [.. Encabezados.Select(e => new TabularWorkbookWriter.Column(e, 20)), new TabularWorkbookWriter.Column("fila", 8), new TabularWorkbookWriter.Column("errores", 60)],
            filas.Select(f =>
            {
                using var d = JsonDocument.Parse(f.Datos);
                string? S(string k) => d.RootElement.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
                return (IReadOnlyList<Cell>)[Cell.Of(S("tipoPago")), Cell.Of(S("placa")), Cell.Of(S("numeroComparendo")), Cell.Of(S("valor")), Cell.Of(S("fechaPago")), Cell.Of(f.NumeroFila), Cell.Of(string.Join("; ", f.Errores))];
            }).ToList());
        return Results.File(TabularWorkbookWriter.Write([hoja]), TabularWorkbookWriter.ContentType, "pagos-con-error.xlsx");
    }
}
