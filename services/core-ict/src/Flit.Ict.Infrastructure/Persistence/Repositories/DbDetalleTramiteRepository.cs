using System.Data;
using System.Data.Common;
using System.Globalization;
using Flit.Ict.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Datos recibidos de un pre-trámite y log HTTP acotado a él (HU #11819).
/// </summary>
/// <remarks>
/// Los datos personales salen ENMASCARADOS: se conservan los últimos cuatro caracteres, que es lo
/// que necesita soporte para confirmar contra lo que le dicta el cliente por teléfono. El revelado
/// en claro es una acción aparte y auditada (HU #11820), no un modo de esta consulta.
/// </remarks>
public sealed class DbDetalleTramiteRepository(IctDbContext db) : IDatosTramiteQuery, ILogTramiteQuery
{
    // Las dos interfaces declaran la misma firma y solo difieren en el tipo devuelto, que C# no
    // admite como sobrecarga. Se implementan de forma explícita y cada una delega en su método con
    // nombre propio, que además se lee mejor en las pruebas.
    Task<DatosTramite?> IDatosTramiteQuery.ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct) =>
        ConsultarDatosAsync(numero, tenantId, ct);

    Task<IReadOnlyList<EventoLogTramite>?> ILogTramiteQuery.ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct) =>
        ConsultarLogAsync(numero, tenantId, ct);

    public async Task<DatosTramite?> ConsultarDatosAsync(long numero, Guid? tenantId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);

            var secciones = new List<SeccionDatos>();
            Guid masterId;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT id, plate, vin, selling_date, selling_price, delivery_address, priority,
                           manager_user, manager_mail, manager_id_transaction, traffic_secretary_code,
                           runt_transit_office_name, closed_document, process_without_attached_documents,
                           starts_procedure_in_paused, observation_when_paused,
                           send_automatic_traffic_secretary, related_company_name, related_company_document
                    FROM ict.external_integration_master
                    WHERE transaction_number = @numero
                      AND deleted_at IS NULL
                      AND (@tenant::uuid IS NULL OR tenant_id = @tenant::uuid)
                    LIMIT 1
                    """;
                AddParam(cmd, "numero", numero);
                AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    return null;
                }

                masterId = reader.GetGuid(0);
                secciones.Add(new SeccionDatos("Transacción",
                [
                    new DatoTramite("Placa", Texto(reader, 1)),
                    new DatoTramite("VIN", Texto(reader, 2)),
                    new DatoTramite("Fecha de venta", Texto(reader, 3)),
                    new DatoTramite("Precio de venta", Moneda(reader, 4)),
                    new DatoTramite("Dirección de entrega", Texto(reader, 5)),
                    new DatoTramite("Prioritario", SiNo(reader, 6)),
                    new DatoTramite("Organismo de tránsito", Texto(reader, 11) ?? Texto(reader, 10)),
                    new DatoTramite("Referencia del cliente", Texto(reader, 9)),
                    new DatoTramite("Compañía relacionada", Texto(reader, 17)),
                ]));

                secciones.Add(new SeccionDatos("Cómo pidió procesarlo el cliente",
                [
                    new DatoTramite("Documento cerrado", SiNo(reader, 12)),
                    new DatoTramite("Procesar sin adjuntos", SiNo(reader, 13)),
                    new DatoTramite("Empieza pausado", SiNo(reader, 14)),
                    new DatoTramite("Observación de la pausa", Texto(reader, 15)),
                    new DatoTramite("Envío automático al organismo", SiNo(reader, 16)),
                    new DatoTramite("Usuario que radicó", Texto(reader, 7)),
                    // El correo del gestor es de contacto profesional, no del ciudadano, pero sigue
                    // siendo un dato personal: se marca para que la pantalla lo trate como tal.
                    new DatoTramite("Correo del gestor", Enmascarar(Texto(reader, 8)), EsSensible: true),
                ]));
            }

            secciones.AddRange(await LeerActoresAsync(connection, masterId, ct));
            secciones.Add(await LeerAdjuntosAsync(connection, masterId, ct));

            return new DatosTramite(numero, secciones);
        }
        finally
        {
            await ResetTenantGucAsync(connection);
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<List<SeccionDatos>> LeerActoresAsync(
        DbConnection connection, Guid masterId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT actor_type, document_type, document_number, name, first_last_name, second_last_name,
                   phone, email, city, state, address,
                   legal_representative_name, legal_representative_document_type,
                   legal_representative_document_number, legal_representative_email
            FROM ict.external_integration_actors
            WHERE master_id = @master AND deleted_at IS NULL
            ORDER BY actor_type
            """;
        AddParam(cmd, "master", masterId);

        var secciones = new List<SeccionDatos>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var nombre = string.Join(" ", new[] { Texto(reader, 3), Texto(reader, 4), Texto(reader, 5) }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

            var datos = new List<DatoTramite>
            {
                new("Nombre", Enmascarar(nombre), EsSensible: true),
                new("Documento", Documento(Texto(reader, 1), Texto(reader, 2)), EsSensible: true),
                new("Teléfono", Enmascarar(Texto(reader, 6)), EsSensible: true),
                new("Correo", Enmascarar(Texto(reader, 7)), EsSensible: true),
                new("Ciudad", Texto(reader, 8)),
                new("Departamento", Texto(reader, 9)),
                new("Dirección", Enmascarar(Texto(reader, 10)), EsSensible: true),
            };

            var repLegal = Texto(reader, 11);
            if (!string.IsNullOrWhiteSpace(repLegal))
            {
                // El representante legal solo se pinta cuando existe: en una persona natural esas
                // cuatro filas vacías harían pensar que faltan datos.
                datos.Add(new DatoTramite("Representante legal", Enmascarar(repLegal), EsSensible: true));
                datos.Add(new DatoTramite("Documento del representante",
                    Documento(Texto(reader, 12), Texto(reader, 13)), EsSensible: true));
                datos.Add(new DatoTramite("Correo del representante",
                    Enmascarar(Texto(reader, 14)), EsSensible: true));
            }

            secciones.Add(new SeccionDatos(EtiquetasDetalle.Actor(Texto(reader, 0)), datos));
        }

        return secciones;
    }

    private static async Task<SeccionDatos> LeerAdjuntosAsync(
        DbConnection connection, Guid masterId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT filename, mime_type, size_bytes, created_at
            FROM ict.external_integration_transaction_attachments
            WHERE master_id = @master AND deleted_at IS NULL
            ORDER BY created_at
            """;
        AddParam(cmd, "master", masterId);

        var datos = new List<DatoTramite>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tamano = await reader.IsDBNullAsync(2, ct) ? (long?)null : reader.GetInt64(2);
            datos.Add(new DatoTramite(
                Texto(reader, 0) ?? "(sin nombre)",
                tamano is null ? Texto(reader, 1) : $"{Texto(reader, 1)} · {Tamano(tamano.Value)}"));
        }

        return new SeccionDatos("Adjuntos", datos);
    }

    public async Task<IReadOnlyList<EventoLogTramite>?> ConsultarLogAsync(
        long numero, Guid? tenantId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);

            string referencia;
            string placa;
            Guid tenantDelTramite;
            DateTime recibido;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT manager_id_transaction, plate, tenant_id, created_at
                    FROM ict.external_integration_master
                    WHERE transaction_number = @numero
                      AND deleted_at IS NULL
                      AND (@tenant::uuid IS NULL OR tenant_id = @tenant::uuid)
                    LIMIT 1
                    """;
                AddParam(cmd, "numero", numero);
                AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    return null;
                }

                referencia = await reader.IsDBNullAsync(0, ct) ? string.Empty : reader.GetString(0);
                placa = reader.GetString(1);
                tenantDelTramite = reader.GetGuid(2);
                recibido = reader.GetDateTime(3);
            }

            await using var logCmd = connection.CreateCommand();
            // HEURÍSTICA, y conviene saberlo al leer la pantalla: no existe ninguna clave que una un
            // registro de log con un trámite. La relación es de muchos a muchos (una petición de
            // registro trae hasta veinte pre-trámites), así que se busca por las tres huellas que el
            // trámite deja en el log: su referencia o su número en la RUTA, y su placa en el CUERPO.
            // La ventana temporal acota el barrido: sin ella esto recorrería la tabla entera.
            logCmd.CommandText = """
                SELECT id, created_at, log_type, direction, method, path, status_code, duration_ms,
                       -- Cuántos pre-trámites viajaban en la misma petición: se cuentan las placas que
                       -- devolvió la respuesta del registro. Es aproximado por definición, y es
                       -- justamente la cifra que explica por qué el log crudo resulta ilegible.
                       GREATEST(1, (length(COALESCE(response::text, '')) -
                                    length(replace(COALESCE(response::text, ''), '"Plate"', ''))) / 7) AS lote
                FROM ict.integration_log
                WHERE (tenant_id = @tenant_tramite OR tenant_id IS NULL)
                  AND created_at BETWEEN @desde AND @hasta
                  AND (
                      (@ref <> '' AND path ILIKE '%' || @ref || '%')
                      -- El número se busca como SEGMENTO de la ruta, no como subcadena. Con ILIKE
                      -- '%1%' el trámite 1 casaría con todas las rutas por el «/api/v1/», y el 82
                      -- traería además 182, 820 y 1829. Es exactamente el defecto del filtro del Log
                      -- ICT actual, y repetirlo aquí haría inservible la pestaña.
                      OR path ~ ('(^|/)' || @numero || '($|/|\?)')
                      OR response::text ILIKE '%' || @placa || '%'
                      OR request::text ILIKE '%' || @placa || '%'
                  )
                ORDER BY created_at
                LIMIT 200
                """;
            AddParam(logCmd, "tenant_tramite", tenantDelTramite);
            AddParam(logCmd, "ref", referencia);
            AddParam(logCmd, "numero", numero.ToString(CultureInfo.InvariantCulture));
            AddParam(logCmd, "placa", placa);
            AddParam(logCmd, "desde", recibido.AddMinutes(-5));
            AddParam(logCmd, "hasta", recibido.AddDays(30));

            var eventos = new List<EventoLogTramite>();
            await using var logReader = await logCmd.ExecuteReaderAsync(ct);
            while (await logReader.ReadAsync(ct))
            {
                eventos.Add(new EventoLogTramite(
                    Id: logReader.GetGuid(0),
                    Ocurrido: logReader.GetDateTime(1),
                    Tipo: EtiquetasDetalle.TipoLog(logReader.GetString(2)),
                    Direccion: EtiquetasDetalle.Direccion(logReader.GetString(3)),
                    Metodo: logReader.GetString(4),
                    Ruta: logReader.GetString(5),
                    Codigo: logReader.GetInt32(6),
                    DuracionMs: logReader.GetInt32(7),
                    TramitesEnLaPeticion: (int)logReader.GetInt64(8)));
            }

            return eventos;
        }
        finally
        {
            await ResetTenantGucAsync(connection);
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    // ── Formato ────────────────────────────────────────────────────────────────

    private static string? Texto(DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : reader.GetString(i) is { Length: > 0 } s ? s : null;

    /// <summary>
    /// Importe en pesos con separador de miles, escrito a mano.
    /// </summary>
    /// <remarks>
    /// El servicio corre en modo de globalización INVARIANTE
    /// (<c>InvariantGlobalization</c> en <c>Directory.Build.props</c>): ahí
    /// <c>new CultureInfo("es-CO")</c> lanza <c>CultureNotFoundException</c> y el formato "C0"
    /// tampoco da el separador colombiano. Se compone sobre la cultura invariante y se cambia la
    /// coma por punto.
    /// </remarks>
    private static string? Moneda(DbDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return null;
        }

        var entero = decimal.Truncate(reader.GetDecimal(i))
            .ToString("N0", CultureInfo.InvariantCulture)
            .Replace(",", ".", StringComparison.Ordinal);

        return $"$ {entero}";
    }

    private static string SiNo(DbDataReader reader, int i) =>
        !reader.IsDBNull(i) && reader.GetBoolean(i) ? "Sí" : "No";

    private static string Tamano(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / (1024d * 1024d):0.#} MB",
    };

    private static string? Documento(string? tipo, string? numero)
    {
        var enmascarado = Enmascarar(numero);
        if (enmascarado is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(tipo) ? enmascarado : $"{tipo} {enmascarado}";
    }

    /// <summary>
    /// Deja visibles los últimos cuatro caracteres. Es el mínimo con el que soporte puede confirmar
    /// contra lo que le dicta el cliente sin que la pantalla exponga el dato entero a quien pase por
    /// detrás. Ver también HU #11820: el revelado en claro es una acción aparte y auditada.
    /// </summary>
    private static string? Enmascarar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var limpio = valor.Trim();
        return limpio.Length <= 4
            ? new string('*', limpio.Length)
            : string.Concat(new string('*', limpio.Length - 4), limpio[^4..]);
    }

    private static async Task SetTenantGucAsync(DbConnection connection, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        AddParam(cmd, "tenant", tenantId?.ToString() ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ResetTenantGucAsync(DbConnection connection)
    {
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                return;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false)";
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // limpiar el GUC es best-effort; nunca debe romper la consulta
        catch (Exception)
        {
            // best-effort
        }
#pragma warning restore CA1031
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
