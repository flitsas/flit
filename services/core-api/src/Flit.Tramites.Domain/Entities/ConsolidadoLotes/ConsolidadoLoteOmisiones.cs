namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13371 (Épica #13216) — vocabulario de los motivos por los que un trámite queda <c>omitido</c> en un
/// lote de descarga masiva de consolidados (<c>tramites.consolidado_export_batch_items.omission_code</c>).
/// <para>Es contrato con el CHECK del DDL de la HU #13368, que lista estos mismos diez códigos de forma
/// literal: añadir o renombrar uno exige migración. El texto legible para el usuario (CSV de omitidos)
/// no vive aquí: lo resuelve el catálogo de textos del procesamiento por ítem (#13375).</para>
/// <para>Uso de ejemplo: <c>item.OmissionCode = ConsolidadoLoteOmisiones.FurRequerido;</c>.</para>
/// </summary>
public static class ConsolidadoLoteOmisiones
{
    /// <summary>Estado final sin consolidado y sin FUR: el lote no crea un FUR sobre un trámite definitivo (Q12).</summary>
    public const string FurRequerido = "fur_requerido";

    /// <summary>Migrado de la V1 en estado final sin consolidado: modo foto, no se genera (Q10).</summary>
    public const string MigradoSoloLectura = "migrado_solo_lectura";

    /// <summary>El generador no encontró documentos que fusionar.</summary>
    public const string SinAdjuntos = "sin_adjuntos";

    /// <summary>Un documento del expediente no se pudo leer del almacenamiento.</summary>
    public const string AdjuntoNoDisponible = "adjunto_no_disponible";

    /// <summary>Un documento del expediente tiene un formato que no se puede fusionar.</summary>
    public const string MimetypeNoSoportado = "mimetype_no_soportado";

    /// <summary>Falta el organismo de tránsito para producir un documento de la cascada.</summary>
    public const string OrganismoRequerido = "organismo_requerido";

    /// <summary>La modalidad del trámite no admite la generación pedida.</summary>
    public const string ModalidadNoSoportada = "modalidad_no_soportada";

    /// <summary>El organismo está en solo lectura ante Quipux para generar el maestro (#13308).</summary>
    public const string QuipuxSoloLectura = "quipux_solo_lectura";

    /// <summary>El solicitante ya no puede ver el trámite, o el trámite ya no existe en la compañía del lote.</summary>
    public const string AccesoRevocado = "acceso_revocado";

    /// <summary>Error técnico persistente tras agotar los reintentos.</summary>
    public const string ErrorTecnico = "error_tecnico";

    /// <summary>Los diez códigos, en el orden del CHECK del DDL.</summary>
    public static IReadOnlyList<string> Todos { get; } =
    [
        FurRequerido,
        MigradoSoloLectura,
        SinAdjuntos,
        AdjuntoNoDisponible,
        MimetypeNoSoportado,
        OrganismoRequerido,
        ModalidadNoSoportada,
        QuipuxSoloLectura,
        AccesoRevocado,
        ErrorTecnico,
    ];

    /// <summary>¿<paramref name="codigo"/> es un motivo de omisión del vocabulario? (comparación exacta).</summary>
    public static bool EsValido(string? codigo) => codigo is not null && Todos.Contains(codigo, StringComparer.Ordinal);
}
