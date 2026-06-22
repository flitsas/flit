using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// (De)serialización del snapshot documental (HU #10197) en la forma canónica camelCase
/// <c>{ procedureTypeId, transitOfficeId, clienteId, documentos: [...] }</c>. Usa
/// <c>System.Text.Json</c> con generación de código (source-gen) para ser compatible con
/// trimming/AOT; la misma forma se aplica al escribir el snapshot, leerlo (GET) e
/// inspeccionarlo (guard de uso).
/// </summary>
public static class SnapshotJson
{
    /// <summary>Serializa el payload a la forma canónica persistida en el jsonb.</summary>
    public static string Serialize(ProcedureDocumentSnapshotPayload payload) =>
        JsonSerializer.Serialize(payload, SnapshotJsonContext.Default.ProcedureDocumentSnapshotPayload);

    /// <summary>Deserializa el jsonb a su payload; null si la cadena no es válida.</summary>
    public static ProcedureDocumentSnapshotPayload? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.ProcedureDocumentSnapshotPayload);
}

/// <summary>
/// Contexto de serialización source-generated para el snapshot documental. Evita la
/// reflexión en tiempo de ejecución (compatible con AOT/trimming).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProcedureDocumentSnapshotPayload))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;
