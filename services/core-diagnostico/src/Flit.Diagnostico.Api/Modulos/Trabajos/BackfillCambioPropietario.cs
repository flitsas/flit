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
/// de la celda) y guarda el lote. Idempotente: lo ya fijado no vuelve a entrar; lo que sigue en null (tipo no homologable, cifrado
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
            var candidatos = await db.Set<VehiculoDiagnostico>()
                .Where(v => v.PropietarioDeclaradoDocCifrado != null && v.CambioPropietario == null
                    && db.Set<PersonaVehiculo>().Any(p => p.TenantId == v.TenantId && p.VehiculoId == v.Id && p.Rol == RolPersona.Propietario))
                .Where(v => desde == null || v.Id > desde.Value)
                .OrderBy(v => v.Id)
                .Take(tamanoLote)
                .ToListAsync(ct).ConfigureAwait(false);
            if (candidatos.Count == 0)
                break;

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
                revisados++;
                if (vehiculo.CambioPropietario is null)
                    sinResultado++;
                else
                    fijados++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            lotes++;
            ultimo = candidatos[^1].Id;
            if (candidatos.Count < tamanoLote)
                break;
        }

        Terminado(log, db.EmpresaActual, revisados, fijados, sinResultado, lotes);
        return new ResultadoBackfill(revisados, fijados, sinResultado, lotes);
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
                    throw new ArgumentException($"Argumento inválido en {Nombre}: «{args[i]}». Uso: {Nombre} --empresa <uuid> [--empresa <uuid>] [--lote <n>].", nameof(args));
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
