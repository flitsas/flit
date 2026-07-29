namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Traductor del <c>process_status</c> de V1 al estado de negocio de V2, por tipo de trámite.
///
/// <para>
/// Existe como abstracción por una razón concreta y peligrosa: <b>los catálogos de traspaso y
/// matrícula NO coinciden</b>. Se separan a partir del id 5:
/// </para>
///
/// <code>
/// id | traspaso  | matrícula
/// ---+-----------+----------
///  5 | Delivered | Assigned
///  6 | Approved  | Delivered
///  7 | Rejected  | Approved
///  8 | Archived  | Rejected
///  9 |    —      | Revoked
/// 10 |    —      | Archived
/// </code>
///
/// <para>
/// Reusar el mapa de traspaso para matrícula migraría los 12.251 aprobados de producción como
/// RECHAZADOS, en silencio y sin que nada falle. De ahí que el mapa se inyecte y no se resuelva
/// por una clase estática global.
/// </para>
/// </summary>
public interface IV1StateMap
{
    /// <summary>Nombre del estado en el catálogo de V1, para reportes legibles.</summary>
    string V1Name(int processStatus);

    /// <summary>¿El mapeo de este estado es una decisión pendiente de negocio?</summary>
    bool IsAmbiguous(int processStatus);

    /// <summary>Estado de V2 correspondiente. Nunca lanza.</summary>
    string ToV2(int processStatus);
}

/// <summary>
/// Adaptador del mapa de TRASPASO, que vive en <see cref="StateMap"/> como clase estática desde
/// antes de que existiera esta abstracción. No duplica lógica: delega.
/// </summary>
public sealed class TransferStateMap : IV1StateMap
{
    public static readonly TransferStateMap Instance = new();

    private TransferStateMap() { }

    public string V1Name(int processStatus) => StateMap.V1Name(processStatus);

    public bool IsAmbiguous(int processStatus) => StateMap.IsAmbiguous(processStatus);

    public string ToV2(int processStatus) => StateMap.ToV2(processStatus);
}
