using System.Globalization;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>
/// Cómo se califica una versión del prompt antes de publicarla. La «verdad» son los documentos OCR que una persona ya
/// revisó y registró (sus valores finales), primero los que el operador corrigió. Se cuentan solo los campos
/// obligatorios: son los que el operador tuvo que tener bien para registrar. La versión aprueba si no lee peor que la
/// publicada sobre los mismos documentos, con un margen de <see cref="ToleranciaPuntos"/> por la variación propia del modelo.
/// </summary>
public static class CalificacionOcr
{
    public const int TamanoConjunto = 10;
    public const int MinimoDocumentos = 5;
    public const decimal ToleranciaPuntos = 2m;

    public static IReadOnlyList<string> Campos => CamposOcr.Obligatorios;

    public static decimal Porcentaje(int aciertos, int campos) => campos == 0 ? 0 : Math.Round(100m * aciertos / campos, 1);

    public static bool Aprueba(int aciertosCandidata, int aciertosVigente, int campos) =>
        Porcentaje(aciertosCandidata, campos) >= Porcentaje(aciertosVigente, campos) - ToleranciaPuntos;

    /// <summary>Igual que lo usa el registro: no castiga espacios, guiones, puntos de miles ni el formato de la fecha.</summary>
    public static bool Acierta(string campo, string? esperado, string? obtenido)
    {
        var (e, o) = (Normalizar(campo, esperado), Normalizar(campo, obtenido));
        if (e is null || o is null)
            return e is null && o is null;
        if (e == o)
            return true;
        return campo is CamposOcr.Nombre or CamposOcr.Secretaria && Parecido(e, o) >= 0.7;
    }

    public static string? Normalizar(string campo, string? valor)
    {
        var s = valor?.Trim();
        if (string.IsNullOrEmpty(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;
        string? r = campo switch
        {
            CamposOcr.Numero or CamposOcr.Placa or CamposOcr.CodigoInfraccion =>
                new string([.. s.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '.')]).ToUpperInvariant(),
            CamposOcr.NumeroDocumento => new string([.. s.Where(char.IsAsciiDigit)]),
            CamposOcr.Valor => Infraestructura.Excel.LectorExcel.Valor(new(s, null))?.ToString("0", CultureInfo.InvariantCulture) ?? s,
            CamposOcr.FechaInfraccion => ValoresOcr.Fecha(s)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? s,
            CamposOcr.TipoDocumento => ValoresOcr.Documento(s)?.ToString() ?? s.ToUpperInvariant(),
            _ => Texto(s),
        };
        return string.IsNullOrEmpty(r) ? null : r;
    }

    private static string Texto(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>1 − distancia de edición / largo mayor.</summary>
    private static double Parecido(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var anterior = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var actual = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), anterior + (a[i - 1] == b[j - 1] ? 0 : 1));
                anterior = actual;
            }
        }

        return 1d - (double)d[b.Length] / Math.Max(a.Length, b.Length);
    }
}

/// <summary>
/// Evaluación de una versión del prompt contra la publicada (§7.24 endurecido por decisión de Samuel, 2026-10-16): sin una
/// evaluación aprobada no se publica. Corre en segundo plano (<see cref="EvaluadorPromptOcr"/>) porque son dos lecturas
/// del modelo por documento.
/// </summary>
public sealed class ServicioEvaluacionPrompt(ComparendosDb db, RegistroArchivos archivos, IProveedorOcr proveedor, Auditoria auditoria, Reloj reloj)
{
    private const int MaximoIntentos = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record CampoEvaluado(string Campo, string? Esperado, string? Candidata, string? Vigente, bool AciertaCandidata, bool AciertaVigente);

    public sealed record DocumentoEvaluado(Guid DocumentoId, string Archivo, string? FalloCandidata, string? FalloVigente, IReadOnlyList<CampoEvaluado> Campos);

    private sealed record Muestra(Guid Id, string Archivo, string ContentType, byte[] Contenido, IReadOnlyDictionary<string, string?> Esperado);

    /// <summary>La evaluación sirve para publicar mientras sea aprobada, de la versión tal como está y contra la publicada de hoy.</summary>
    public static bool Habilita(EvaluacionPromptOcr? e, PromptOcrVersion p, Guid? publicada) =>
        e is { Estado: EstadoEvaluacionPrompt.Terminada, Aprobada: true }
        && e.PromptVigenteId == publicada
        && e.VersionDel == (p.ActualizadoEn ?? p.CreadoEn);

    public async Task<EvaluacionPromptOcr> IniciarAsync(Guid versionId, Guid usuario, CancellationToken ct)
    {
        var p = await db.PromptsOcr.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        if (p.Publicada)
            throw ReglaException.Conflicto("YA_PUBLICADA", "Esta versión ya es la publicada.");
        if (!proveedor.Configurado)
            throw ReglaException.Conflicto("OCR_NO_CONFIGURADO", "El OCR no tiene proveedor configurado en este ambiente.");
        if (await db.EvaluacionesPromptOcr.AnyAsync(e => e.PromptVersionId == versionId && e.Estado == EstadoEvaluacionPrompt.EnCurso, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("EVALUACION_EN_CURSO", "Ya hay una evaluación en curso de esta versión.");

        var vigente = await db.PromptsOcr.Where(x => x.Publicada).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var ev = new EvaluacionPromptOcr
        {
            Id = Guid.CreateVersion7(), PromptVersionId = p.Id, PromptVigenteId = vigente, VersionDel = p.ActualizadoEn ?? p.CreadoEn,
            CreadoEn = reloj.Ahora, CreadoPor = usuario,
        };
        db.EvaluacionesPromptOcr.Add(ev);
        auditoria.Registrar(null, "prompt_ocr_version", p.Id, "EVALUAR", new CambioCampo("evaluacion", null, ev.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ev;
    }

    /// <summary>Corre una evaluación ya reclamada por el evaluador.</summary>
    public async Task EvaluarAsync(Guid id, CancellationToken ct)
    {
        var ev = await db.EvaluacionesPromptOcr.FirstAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (ev.Estado != EstadoEvaluacionPrompt.EnCurso)
            return;
        var candidata = await db.PromptsOcr.AsNoTracking().FirstAsync(p => p.Id == ev.PromptVersionId, ct).ConfigureAwait(false);
        var vigente = ev.PromptVigenteId is { } v ? await db.PromptsOcr.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v, ct).ConfigureAwait(false) : null;

        var muestras = await ConjuntoAsync(ct).ConfigureAwait(false);
        if (muestras.Count < CalificacionOcr.MinimoDocumentos)
        {
            await TerminarAsync(ev, EstadoEvaluacionPrompt.Fallida, $"Hacen falta al menos {CalificacionOcr.MinimoDocumentos} documentos registrados por OCR para comparar; hay {muestras.Count}.", ct).ConfigureAwait(false);
            return;
        }

        // Pocas a la vez para no chocar con el límite del proveedor; las dos versiones leen el mismo documento en paralelo.
        using var cupo = new SemaphoreSlim(3);
        var resultados = await Task.WhenAll(muestras.Select(async m =>
        {
            await cupo.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var c = ExtraerAsync(m, candidata, ct);
                ResultadoOcr? vi = vigente is null ? null : await ExtraerAsync(m, vigente, ct).ConfigureAwait(false);
                return (Muestra: m, Candidata: await c.ConfigureAwait(false), Vigente: vi);
            }
            finally
            {
                cupo.Release();
            }
        })).ConfigureAwait(false);

        var tecnicos = resultados.Count(r => ResultadoOcr.EsTecnico(r.Candidata?.TipoFallo) || ResultadoOcr.EsTecnico(r.Vigente?.TipoFallo));
        if (tecnicos > 0)
        {
            await TerminarAsync(ev, EstadoEvaluacionPrompt.Fallida, $"El proveedor de OCR no respondió en {tecnicos} de {resultados.Length} documentos. Vuelve a evaluar.", ct).ConfigureAwait(false);
            return;
        }

        var documentos = resultados.Select(r => new DocumentoEvaluado(r.Muestra.Id, r.Muestra.Archivo, Fallo(r.Candidata), Fallo(r.Vigente),
            [.. CalificacionOcr.Campos.Select(campo =>
            {
                var esperado = r.Muestra.Esperado.GetValueOrDefault(campo);
                var c = Valor(r.Candidata, campo);
                var vi = Valor(r.Vigente, campo);
                return new CampoEvaluado(campo, esperado, c, vi, CalificacionOcr.Acierta(campo, esperado, c), CalificacionOcr.Acierta(campo, esperado, vi));
            })])).ToList();

        ev.Documentos = documentos.Count;
        ev.Campos = documentos.Sum(d => d.Campos.Count);
        ev.AciertosCandidata = documentos.Sum(d => d.Campos.Count(c => c.AciertaCandidata));
        ev.AciertosVigente = vigente is null ? 0 : documentos.Sum(d => d.Campos.Count(c => c.AciertaVigente));
        ev.Aprobada = vigente is null || CalificacionOcr.Aprueba(ev.AciertosCandidata, ev.AciertosVigente, ev.Campos);
        ev.Detalle = JsonSerializer.Serialize(documentos, Json);
        var pc = CalificacionOcr.Porcentaje(ev.AciertosCandidata, ev.Campos).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        var pv = CalificacionOcr.Porcentaje(ev.AciertosVigente, ev.Campos).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        var mensaje = vigente is null
            ? $"Acertó {pc} % de los campos. No hay versión publicada para comparar."
            : ev.Aprobada
                ? $"Acertó {pc} % de los campos y la publicada (versión {vigente.NumeroVersion}) {pv} %: se puede publicar."
                : $"Acertó {pc} % de los campos y la publicada (versión {vigente.NumeroVersion}) {pv} %: lee peor, no se puede publicar.";
        await TerminarAsync(ev, EstadoEvaluacionPrompt.Terminada, mensaje, ct).ConfigureAwait(false);
    }

    private async Task TerminarAsync(EvaluacionPromptOcr ev, EstadoEvaluacionPrompt estado, string mensaje, CancellationToken ct)
    {
        (ev.Estado, ev.Mensaje, ev.TerminadaEn, ev.ReclamadaHasta) = (estado, mensaje, reloj.Ahora, null);
        if (estado == EstadoEvaluacionPrompt.Fallida)
            ev.Aprobada = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Los últimos documentos registrados tras la revisión de una persona; primero los que el operador corrigió.</summary>
    private async Task<List<Muestra>> ConjuntoAsync(CancellationToken ct)
    {
        var docs = await db.DocumentosOcr.AsNoTracking()
            .Where(d => d.Estado == EstadoDocumentoOcr.Registrado && d.RevisadoEn != null)
            .OrderByDescending(d => db.DocumentoOcrEdiciones.Any(e => e.DocumentoOcrId == d.Id))
            .ThenByDescending(d => d.RevisadoEn).ThenByDescending(d => d.Id)
            .Take(CalificacionOcr.TamanoConjunto)
            .Select(d => new { d.Id, d.ArchivoId, d.NombreArchivo })
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = docs.Select(d => d.Id).ToList();
        var nombres = CalificacionOcr.Campos.ToList();
        var campos = (await db.DocumentoOcrCampos.AsNoTracking()
                .Where(c => ids.Contains(c.DocumentoOcrId) && nombres.Contains(c.NombreCampo))
                .Select(c => new { c.DocumentoOcrId, c.NombreCampo, c.ValorFinal })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.DocumentoOcrId);

        var muestras = new List<Muestra>();
        foreach (var d in docs)
        {
            if (await archivos.LeerAsync(d.ArchivoId, ct).ConfigureAwait(false) is not { } a)
                continue;
            muestras.Add(new Muestra(d.Id, d.NombreArchivo, a.Archivo.ContentType, a.Contenido,
                campos[d.Id].ToDictionary(c => c.NombreCampo, c => c.ValorFinal)));
        }

        return muestras;
    }

    /// <summary>Con los mismos reintentos que la cola (un 429 respeta el Retry-After).</summary>
    private async Task<ResultadoOcr> ExtraerAsync(Muestra m, PromptOcrVersion p, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var r = await proveedor.ExtraerAsync(m.Contenido, m.ContentType, p.Texto, p.Modelo, ct).ConfigureAwait(false);
            if (!ResultadoOcr.EsTecnico(r.TipoFallo) || intento >= MaximoIntentos)
                return r;
            await Task.Delay(r.ReintentarEn is { } e && e > TimeSpan.Zero ? e : TimeSpan.FromSeconds(5 * intento), ct).ConfigureAwait(false);
        }
    }

    private static string? Valor(ResultadoOcr? r, string campo) => r?.Campos.GetValueOrDefault(campo)?.Valor;

    private static string? Fallo(ResultadoOcr? r) => r is { Exitoso: false } ? r.Mensaje ?? r.TipoFallo.ToString() : null;
}

/// <summary>
/// Cola de evaluaciones: reclama una EN_CURSO con <c>FOR UPDATE SKIP LOCKED</c> y una concesión de 30 minutos; si la réplica
/// muere a mitad, otra la retoma cuando vence.
/// </summary>
public sealed partial class EvaluadorPromptOcr(IServiceScopeFactory scopes, TimeProvider time, ILogger<EvaluadorPromptOcr> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await EvaluarSiguienteAsync(stoppingToken).ConfigureAwait(false))
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), time, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> EvaluarSiguienteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var ahora = time.GetUtcNow();
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE comparendos.evaluacion_prompt_ocr SET reclamada_hasta = {ahora.AddMinutes(30)}
            WHERE id = (SELECT id FROM comparendos.evaluacion_prompt_ocr
                        WHERE estado = 'EnCurso' AND (reclamada_hasta IS NULL OR reclamada_hasta <= {ahora})
                        ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return false;

        try
        {
            await scope.ServiceProvider.GetRequiredService<ServicioEvaluacionPrompt>().EvaluarAsync(ids[0], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallo(logger, ex);
            db.ChangeTracker.Clear();
            await db.EvaluacionesPromptOcr.Where(e => e.Id == ids[0]).ExecuteUpdateAsync(u => u
                .SetProperty(e => e.Estado, EstadoEvaluacionPrompt.Fallida)
                .SetProperty(e => e.Aprobada, false)
                .SetProperty(e => e.Mensaje, "Error interno al evaluar. Vuelve a intentarlo.")
                .SetProperty(e => e.TerminadaEn, ahora)
                .SetProperty(e => e.ReclamadaHasta, (DateTimeOffset?)null), ct).ConfigureAwait(false);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló la evaluación de un prompt de OCR.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
