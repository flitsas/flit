using System.Globalization;
using System.Security.Cryptography;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Lo que hizo una corrida del backfill en una compañía: filas revisadas, con valor fijado, que siguen en null y lotes.</summary>
internal sealed record ResultadoBackfill(int Revisados, int Fijados, int SinResultado, int Lotes);

/// <summary>Argumentos del comando: las compañías a recorrer (en orden) y el tamaño del lote.</summary>
internal sealed record OpcionesComandoBackfill(IReadOnlyList<Guid> Empresas, int TamanoLote);

/// <summary>
/// Backfill de <c>cambio_propietario</c> (HU #14582, schema.md §1, contrato-api §5 H-A3) para las filas que la migración S3 dejó con
/// propietario declarado pero sin valor (la SQL no puede descifrar). Recorre, en la compañía del contexto (filtro global, nunca entre
/// empresas), los vehículos con <c>propietario_declarado_doc_cifrado</c>, <c>cambio_propietario IS NULL</c> y al menos un propietario en
/// <c>personas_vehiculo</c> (es decir, con la celda RUNT ya cerrada), por lotes ordenados por id. Por fila descifra solo sus
/// propietarios y su declarado (SP-1), compara con <see cref="DocumentosVehiculo.FijarCambioPropietario"/> (la misma regla del cierre
/// de la celda) y guarda el lote en una transacción que sube <c>version_tablero</c> como el cierre de una celda. Idempotente: lo ya fijado no vuelve a entrar; lo que sigue en null (tipo no homologable, cifrado
/// ilegible) se vuelve a mirar en la siguiente corrida sin cambiar. Al log solo van conteos y la compañía; nunca un documento.
/// </summary>
/// <example>
/// <code>
/// var resultado = await new BackfillCambioPropietario(db, protector, log).EjecutarAsync(200, ct);
/// </code>
/// </example>
internal sealed partial class BackfillCambioPropietario(DiagnosticoDb db, IProtectorDocumentoPersona protector, ILogger<BackfillCambioPropietario> log)
{
    public async Task<ResultadoBackfill> EjecutarAsync(int tamanoLote, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanoLote, 1);
        var (revisados, fijados, sinResultado, lotes) = (0, 0, 0, 0);
        Guid? ultimo = null;
        while (true)
        {
            var desde = ultimo;
            // El reintento de Npgsql exige que la transacción vaya dentro de la estrategia de ejecución; el lote se relee entero.
            var lote = await db.Database.CreateExecutionStrategy()
                .ExecuteAsync(() => LoteEnTransaccionAsync(desde, tamanoLote, ct)).ConfigureAwait(false);
            if (lote.Leidos == 0)
                break;
            revisados += lote.Leidos;
            fijados += lote.Fijados;
            sinResultado += lote.Leidos - lote.Fijados;
            lotes++;
            ultimo = lote.Ultimo;
            if (lote.Leidos < tamanoLote)
                break;
        }

        Terminado(log, db.EmpresaActual, revisados, fijados, sinResultado, lotes);
        return new ResultadoBackfill(revisados, fijados, sinResultado, lotes);
    }

    /// <summary>
    /// Un lote en su transacción: lee las candidatas después de <paramref name="desde"/>, fija el valor y, por cada diagnóstico con
    /// filas fijadas, sube <c>version_tablero</c> de la cabecera (<see cref="EscritorCeldas.SubirVersionTableroAsync(DiagnosticoDb, Guid, CancellationToken)"/>,
    /// que la bloquea hasta el commit) y deja esa versión en sus filas (<see cref="EscritorCeldas.MarcarFila"/>), como el cierre de una
    /// celda (CR-3 del review): el delta <c>?desde=</c> de la grilla las trae. Las filas que siguen en null no se marcan.
    /// </summary>
    private async Task<(int Leidos, int Fijados, Guid? Ultimo)> LoteEnTransaccionAsync(Guid? desde, int tamanoLote, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var candidatos = await db.Set<VehiculoDiagnostico>()
            .Where(v => v.PropietarioDeclaradoDocCifrado != null && v.CambioPropietario == null
                && db.Set<PersonaVehiculo>().Any(p => p.TenantId == v.TenantId && p.VehiculoId == v.Id && p.Rol == RolPersona.Propietario))
            .Where(v => desde == null || v.Id > desde.Value)
            .OrderBy(v => v.Id)
            .Take(tamanoLote)
            .ToListAsync(ct).ConfigureAwait(false);
        if (candidatos.Count == 0)
            return (0, 0, null);

        var ids = candidatos.Select(v => v.Id).ToList();
        var propietarios = (await db.Set<PersonaVehiculo>().AsNoTracking()
                .Where(p => ids.Contains(p.VehiculoId) && p.Rol == RolPersona.Propietario)
                .OrderBy(p => p.Orden)
                .Select(p => new { p.VehiculoId, p.DocTipo, p.DocCifrado })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(p => p.VehiculoId);
        foreach (var vehiculo in candidatos)
        {
            List<(string Tipo, string Numero)>? delRunt;
            try
            {
                delRunt = [.. propietarios[vehiculo.Id].Select(p => (p.DocTipo, protector.Desproteger(p.DocCifrado)))];
            }
            catch (CryptographicException)
            {
                delRunt = null;
            }

            if (delRunt is not null)
                DocumentosVehiculo.FijarCambioPropietario(vehiculo, delRunt, protector, MetricasDiagnostico.OrigenBackfill);
        }

        var conValor = candidatos.Where(v => v.CambioPropietario is not null).ToList();
        foreach (var delLote in conValor.GroupBy(v => v.DiagnosticoId))
        {
            var version = await EscritorCeldas.SubirVersionTableroAsync(db, delLote.Key, ct).ConfigureAwait(false);
            foreach (var vehiculo in delLote)
                EscritorCeldas.MarcarFila(vehiculo, version);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return (candidatos.Count, conValor.Count, candidatos[^1].Id);
    }

    [LoggerMessage(EventId = 14584, Level = LogLevel.Information,
        Message = "Backfill de cambio_propietario en la compañía {Compania}: {Revisados} revisados, {Fijados} fijados, {SinResultado} siguen en null, {Lotes} lotes")]
    private static partial void Terminado(ILogger logger, Guid? compania, int revisados, int fijados, int sinResultado, int lotes);
}

/// <summary>
/// Subcomando del host para lanzar <see cref="BackfillCambioPropietario"/> por ambiente (lo corre infra-agent; H-A3):
/// <c>dotnet Flit.Diagnostico.Api.dll backfill-cambio-propietario --empresa &lt;uuid&gt; [--empresa &lt;uuid&gt; …] [--lote 200]</c>,
/// con la misma configuración del servicio (cadena de conexión y anillo de Data Protection para descifrar). Construye el host sin
/// arrancarlo (no hay trabajadores ni HTTP), no migra, y por cada compañía abre un scope con <see cref="EmpresaDelTrabajo.Fijar"/>: el
/// servicio no lee entre empresas, así que la lista de compañías la da quien lo lanza. Devuelve 0 si todas terminaron.
/// </summary>
/// <example>
/// <code>
/// if (ComandoBackfillCambioPropietario.EsComando(args))
///     return await ComandoBackfillCambioPropietario.EjecutarAsync(app.Services, ComandoBackfillCambioPropietario.Leer(args), ct);
/// </code>
/// </example>
internal static class ComandoBackfillCambioPropietario
{
    public const string Nombre = "backfill-cambio-propietario";
    public const int LotePorDefecto = 200;

    /// <summary>Uso del subcomando (va a stderr con un argumento inválido).</summary>
    public const string Uso = "Uso: " + Nombre + " --empresa <uuid> [--empresa <uuid> ...] [--lote <n>]";

    /// <summary>
    /// Punto de entrada de <c>Main</c> (CR-4 del review): lee los argumentos y, si son inválidos, escribe el motivo y <see cref="Uso"/>
    /// en <paramref name="error"/> y devuelve 2, sin traza y sin construir el host; si son válidos construye el host con
    /// <paramref name="construir"/> y corre <see cref="EjecutarAsync"/>.
    /// </summary>
    public static async Task<int> CorrerAsync(string[] args, Func<WebApplication> construir, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(construir);
        ArgumentNullException.ThrowIfNull(error);
        OpcionesComandoBackfill opciones;
        try
        {
            opciones = Leer(args);
        }
        catch (ArgumentException ex)
        {
            await error.WriteLineAsync(Motivo(ex)).ConfigureAwait(false);
            await error.WriteLineAsync(Uso).ConfigureAwait(false);
            return 2;
        }

        await using var host = construir();
        return await EjecutarAsync(host.Services, opciones, ct).ConfigureAwait(false);
    }

    /// <summary>El mensaje de la excepción sin el sufijo «(Parameter 'args')» que añade ArgumentException.</summary>
    private static string Motivo(ArgumentException ex) =>
        ex.ParamName is { } nombre ? ex.Message.Replace($" (Parameter '{nombre}')", string.Empty, StringComparison.Ordinal) : ex.Message;

    public static bool EsComando(string[] args) => args is { Length: > 0 } && string.Equals(args[0], Nombre, StringComparison.Ordinal);

    /// <summary>Lee <c>--empresa</c> (una o más, uuid) y <c>--lote</c> (entero ≥ 1); cualquier otra cosa es ArgumentException.</summary>
    public static OpcionesComandoBackfill Leer(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var empresas = new List<Guid>();
        var lote = LotePorDefecto;
        for (var i = EsComando(args) ? 1 : 0; i < args.Length; i++)
        {
            var valor = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--empresa" when Guid.TryParse(valor, out var empresa):
                    empresas.Add(empresa);
                    i++;
                    break;
                case "--lote" when int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var tamano) && tamano >= 1:
                    lote = tamano;
                    i++;
                    break;
                default:
                    throw new ArgumentException($"Argumento inválido en {Nombre}: «{args[i]}».", nameof(args));
            }
        }

        if (empresas.Count == 0)
            throw new ArgumentException($"{Nombre} necesita al menos un --empresa <uuid>.", nameof(args));
        return new OpcionesComandoBackfill(empresas, lote);
    }

    public static async Task<int> EjecutarAsync(IServiceProvider servicios, OpcionesComandoBackfill opciones, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);
        foreach (var empresa in opciones.Empresas)
        {
            await using var scope = servicios.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            await ActivatorUtilities.CreateInstance<BackfillCambioPropietario>(scope.ServiceProvider)
                .EjecutarAsync(opciones.TamanoLote, ct).ConfigureAwait(false);
        }

        return 0;
    }
}
