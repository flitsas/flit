using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14033 — la empresa «Persona Natural» ya NO la siembra una migración (decisión del PO: se crea a mano por ambiente con
/// <c>deploy/postgres/crear-empresa-persona-natural.sql</c>). Las pruebas de la fachada que la necesitan la siembran ellas
/// mismas, una sola vez por proceso de pruebas, y, si fueron ellas quienes la crearon, la retiran al terminar el proceso
/// junto con lo que las pruebas dejaron colgado (best-effort: nunca falla la corrida). Si la fila ya existía (base de
/// desarrollo con la empresa creada), ni se toca ni se borra.
/// </summary>
internal static class PersonaNaturalTenantSeed
{
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static bool _listo;
    private static bool _laCreoLaPrueba;
    private static string? _cadenaConexion;

    /// <summary>Siembra la empresa (si falta) al construir el host de la prueba y devuelve el mismo host.</summary>
    public static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> ConEmpresaPersonaNatural(
        this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        AsegurarAsync(host.Services, CancellationToken.None).GetAwaiter().GetResult();
        return host;
    }

    /// <summary>Garantiza que exista la empresa; es idempotente y barata tras la primera llamada.</summary>
    public static async Task AsegurarAsync(IServiceProvider services, CancellationToken ct)
    {
        if (_listo)
            return;

        await Candado.WaitAsync(ct);
        try
        {
            if (_listo)
                return;

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            // Mismos valores que deploy/postgres/crear-empresa-persona-natural.sql (code lo asigna la secuencia de la Épica #13517).
            var insertadas = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
                VALUES ({PlatformTenants.PersonaNatural}, 'Persona Natural', 'PN-0000000001', 'RENTING', true, now())
                ON CONFLICT DO NOTHING
                """, ct);
            _laCreoLaPrueba = insertadas == 1;
            _cadenaConexion = db.Database.GetConnectionString();
            if (_laCreoLaPrueba)
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Retirar();
            _listo = true;
        }
        finally
        {
            Candado.Release();
        }
    }

    /// <summary>Borra lo que cuelga de la empresa (por las llaves foráneas hacia identity.tenants) y la empresa misma.</summary>
    private static void Retirar()
    {
        if (!_laCreoLaPrueba || string.IsNullOrEmpty(_cadenaConexion))
            return;
        try
        {
            using var conexion = new NpgsqlConnection(_cadenaConexion);
            conexion.Open();
            using var comando = conexion.CreateCommand();
            comando.CommandText =
                """
                DO $$
                DECLARE r record; i int;
                BEGIN
                    FOR i IN 1..6 LOOP
                        FOR r IN
                            SELECT c.conrelid::regclass AS tabla, a.attname AS columna
                            FROM pg_constraint c
                            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
                            WHERE c.contype = 'f' AND c.confrelid = 'identity.tenants'::regclass AND array_length(c.conkey, 1) = 1
                        LOOP
                            BEGIN
                                EXECUTE format('DELETE FROM %s WHERE %I = $1', r.tabla, r.columna) USING @id;
                            EXCEPTION WHEN OTHERS THEN NULL;
                            END;
                        END LOOP;
                    END LOOP;
                    BEGIN
                        DELETE FROM identity.tenants WHERE id = @id;
                    EXCEPTION WHEN OTHERS THEN NULL;
                    END;
                END $$;
                """;
            comando.CommandText = comando.CommandText.Replace("@id", $"'{PlatformTenants.PersonaNatural}'::uuid", StringComparison.Ordinal);
            comando.ExecuteNonQuery();
        }
        catch
        {
            // Limpieza de mejor esfuerzo: la base de pruebas es desechable.
        }
    }
}
