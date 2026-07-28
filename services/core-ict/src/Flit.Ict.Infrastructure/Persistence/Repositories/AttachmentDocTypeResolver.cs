using System.Data;
using Flit.Ict.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Resuelve el DocTipo destino de un adjunto ICT leyendo <c>ict.external_integration_attachment_association</c>
/// (catálogo global, sin RLS). La tabla es diminuta (una fila por documento del contrato): se carga una
/// vez por ciclo de job y se cachea en memoria para no consultar por cada adjunto. Se lee con un comando
/// ADO crudo (mismo patrón que SendToCoreApiJob) porque la tabla de asociación no tiene entidad EF.
/// </summary>
public sealed class AttachmentDocTypeResolver(IctDbContext db) : IAttachmentDocTypeResolver
{
    private const string Fallback = "otro";

    private Dictionary<int, DocTipoRow>? _cache;

    public async Task<string> ResolveDocTypeAsync(int transactionType, int idAttachment, CancellationToken ct = default)
    {
        _cache ??= await LoadAsync(ct);
        if (!_cache.TryGetValue(idAttachment, out var row))
        {
            return Fallback;
        }

        var tipo = Modality(transactionType) switch
        {
            Modalidad.Matricula => row.Matricula,
            Modalidad.Traspaso => row.Traspaso,
            _ => row.Otros,
        };
        return string.IsNullOrWhiteSpace(tipo) ? Fallback : tipo;
    }

    private enum Modalidad { Matricula, Traspaso, Otros }

    private static Modalidad Modality(int transactionType) => transactionType switch
    {
        1 or 2 => Modalidad.Matricula,
        3 or 4 => Modalidad.Traspaso,
        _ => Modalidad.Otros,
    };

    private async Task<Dictionary<int, DocTipoRow>> LoadAsync(CancellationToken ct)
    {
        var result = new Dictionary<int, DocTipoRow>();
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT eiad_id, doc_tipo_matricula, doc_tipo_traspaso, doc_tipo_otros
                FROM ict.external_integration_attachment_association
                WHERE deleted_at IS NULL
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                result[id] = new DocTipoRow(
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private sealed record DocTipoRow(string Matricula, string Traspaso, string Otros);
}
