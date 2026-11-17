using System.Security.Cryptography;
using System.Text;
using Flit.Diagnostico.Tests.Aislamiento;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// Plantilla de base migrada una vez para las pruebas (<c>flit_dgn_tpl_&lt;huella&gt;</c>): <see cref="ApiDePrueba.ConBaseAsync"/>
/// copia cada base efímera de ella con <c>CREATE DATABASE … TEMPLATE</c> en vez de aplicar las migraciones en cada prueba. La
/// huella es el SHA-256 (10 hex) de la lista ordenada de migraciones del ensamblado: una migración nueva genera otra plantilla.
/// Se crea una sola vez por proceso (candado en memoria) y entre procesos (<c>pg_advisory_lock</c> en la base <c>postgres</c>);
/// se migra con un nombre temporal y se renombra al final, para que nunca exista con el nombre bueno a medio migrar. Al crear
/// una nueva se borran las de otra huella y los temporales que haya dejado una corrida cancelada.
/// </summary>
/// <example>
/// <code>
/// var plantilla = await PlantillaBaseDePrueba.AsegurarAsync(servidor);          // "flit_dgn_tpl_3f9a0c12bd"
/// await PlantillaBaseDePrueba.CopiarAsync(servidor, "flit_dgn_abc", plantilla); // base efímera ya migrada
/// </code>
/// </example>
public static class PlantillaBaseDePrueba
{
    public const string Prefijo = "flit_dgn_tpl_";

    private const string CandadoEntreProcesos = "SELECT pg_advisory_lock(hashtext('flit_dgn_plantilla_pruebas'))";
    private const string LiberarCandado = "SELECT pg_advisory_unlock(hashtext('flit_dgn_plantilla_pruebas'))";
    private const int ReintentosCopia = 3;
    private const int TiempoCopiaSegundos = 120;

    private static readonly Lazy<string> HuellaDelEnsamblado = new(() =>
    {
        using var db = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", null);
        return Huella(db.Database.GetMigrations());
    });

    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly Dictionary<string, Task<string>> Listas = new(StringComparer.Ordinal);
    private static int _creaciones;

    /// <summary>Plantillas migradas por este proceso (0 si ya existía la de la huella actual).</summary>
    public static int Creaciones => Volatile.Read(ref _creaciones);

    /// <summary>Huella de las migraciones de <c>DiagnosticoDb</c> compiladas en este ensamblado.</summary>
    public static string HuellaActual => HuellaDelEnsamblado.Value;

    /// <summary>SHA-256 truncado a 10 hex de la lista ordenada (ordinal) de migraciones.</summary>
    public static string Huella(IEnumerable<string> migraciones)
    {
        ArgumentNullException.ThrowIfNull(migraciones);
        var lista = string.Join('\n', migraciones.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lista)))[..10];
    }

    public static string Nombre(string huella) => Prefijo + huella;

    /// <summary>
    /// Devuelve el nombre de la plantilla de la huella actual en el servidor de <paramref name="servidor"/>, creándola si no
    /// existe. Una falla no queda en caché: la siguiente llamada lo vuelve a intentar.
    /// </summary>
    public static async Task<string> AsegurarAsync(string servidor)
    {
        var clave = ClaveServidor(servidor);
        await Candado.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Listas.TryGetValue(clave, out var lista) || lista.IsFaulted || lista.IsCanceled)
                Listas[clave] = lista = CrearSiFaltaAsync(servidor);
            return await lista.ConfigureAwait(false);
        }
        catch
        {
            Listas.Remove(clave);
            throw;
        }
        finally
        {
            Candado.Release();
        }
    }

    /// <summary>
    /// Crea <paramref name="baseNueva"/> como copia de <paramref name="plantilla"/>. Si la plantilla tiene una sesión abierta
    /// (Postgres no la copia) reintenta con una espera corta; si otra corrida con otra huella la borró, la vuelve a crear.
    /// </summary>
    public static async Task CopiarAsync(string servidor, string baseNueva, string plantilla)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await using var admin = await AbrirAdminAsync(servidor).ConfigureAwait(false);
                // Con muchas copias en paralelo y la máquina cargada, una copia pasa de los 30 s por defecto; un vencimiento
                // aquí acabaría en Omitir (local) sin que la base esté caída.
                await using var crear = new NpgsqlCommand($"CREATE DATABASE \"{baseNueva}\" TEMPLATE \"{plantilla}\"", admin) { CommandTimeout = TiempoCopiaSegundos };
                await crear.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException && intento < ReintentosCopia)
            {
                await BorrarSiQuedoAsync(servidor, baseNueva).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && intento < ReintentosCopia)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * intento)).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName && intento < ReintentosCopia)
            {
                await OlvidarAsync(servidor).ConfigureAwait(false);
                plantilla = await AsegurarAsync(servidor).ConfigureAwait(false);
            }
        }
    }

    private static async Task BorrarSiQuedoAsync(string servidor, string baseNueva)
    {
        await using var admin = await AbrirAdminAsync(servidor).ConfigureAwait(false);
        await EjecutarAsync(admin, $"DROP DATABASE IF EXISTS \"{baseNueva}\" WITH (FORCE)").ConfigureAwait(false);
    }

    private static async Task OlvidarAsync(string servidor)
    {
        await Candado.WaitAsync().ConfigureAwait(false);
        try
        {
            Listas.Remove(ClaveServidor(servidor));
        }
        finally
        {
            Candado.Release();
        }
    }

    private static async Task<string> CrearSiFaltaAsync(string servidor)
    {
        var nombre = Nombre(HuellaActual);
        await using var admin = await AbrirAdminAsync(servidor).ConfigureAwait(false);
        await EjecutarAsync(admin, CandadoEntreProcesos).ConfigureAwait(false);
        try
        {
            if (await ExisteAsync(admin, nombre).ConfigureAwait(false))
                return nombre;

            // Quedan solo bases de otra huella o temporales de una corrida cancelada: con el candado tomado nadie las usa para crear.
            foreach (var vieja in await PlantillasAsync(admin).ConfigureAwait(false))
                await EjecutarAsync(admin, $"DROP DATABASE IF EXISTS \"{vieja}\" WITH (FORCE)").ConfigureAwait(false);

            var temporal = $"{nombre}_tmp_{Guid.NewGuid():N}"[..(nombre.Length + 13)];
            await EjecutarAsync(admin, $"CREATE DATABASE \"{temporal}\"").ConfigureAwait(false);
            var cadenaTemporal = new NpgsqlConnectionStringBuilder(servidor) { Database = temporal }.ConnectionString;
            await using (var db = DosEmpresas.Contexto(cadenaTemporal, null))
                await db.Database.MigrateAsync().ConfigureAwait(false);

            // Sin sesiones en la temporal: el RENAME y las copias posteriores las rechazan.
            await using (var conexion = new NpgsqlConnection(cadenaTemporal))
                NpgsqlConnection.ClearPool(conexion);
            await EjecutarAsync(admin, $"ALTER DATABASE \"{temporal}\" RENAME TO \"{nombre}\"").ConfigureAwait(false);
            Interlocked.Increment(ref _creaciones);
            return nombre;
        }
        finally
        {
            await EjecutarAsync(admin, LiberarCandado).ConfigureAwait(false);
        }
    }

    private static async Task<NpgsqlConnection> AbrirAdminAsync(string servidor)
    {
        var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(servidor) { Database = "postgres" }.ConnectionString);
        try
        {
            await admin.OpenAsync().ConfigureAwait(false);
            return admin;
        }
        catch
        {
            await admin.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> ExisteAsync(NpgsqlConnection admin, string nombre)
    {
        await using var consulta = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @nombre)", admin);
        consulta.Parameters.AddWithValue("nombre", nombre);
        return (bool)(await consulta.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private static async Task<List<string>> PlantillasAsync(NpgsqlConnection admin)
    {
        await using var consulta = new NpgsqlCommand("SELECT datname FROM pg_database WHERE starts_with(datname, @prefijo)", admin);
        consulta.Parameters.AddWithValue("prefijo", Prefijo);
        var plantillas = new List<string>();
        await using var lector = await consulta.ExecuteReaderAsync().ConfigureAwait(false);
        while (await lector.ReadAsync().ConfigureAwait(false))
            plantillas.Add(lector.GetString(0));
        return plantillas;
    }

    private static async Task EjecutarAsync(NpgsqlConnection admin, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, admin);
        await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string ClaveServidor(string servidor)
    {
        var cadena = new NpgsqlConnectionStringBuilder(servidor);
        return $"{cadena.Host}:{cadena.Port}:{cadena.Username}";
    }
}
