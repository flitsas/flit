using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Semillas;

/// <summary>
/// Las 11 tesis de V1 como catálogo inicial [Excel J1]: código y prefijo de radicado = prefijo de V1, plazo de
/// respuesta de 15 días hábiles [Excel J8] y estados legales en que procede cada una [Excel C13 propuesta]. La tabla
/// exacta (C15) no se respondió: es el SUPUESTO S-22 de la especificación y el SuperAdmin la ajusta sin desplegar.
/// Ninguna queda habilitada por defecto en una empresa.
/// </summary>
internal static class TipologiasSemilla
{
    private const string EnTiempo = "EnTiempo";
    private const string Vencido = "Vencido";
    private const string Caducado = "Caducado";
    private const string Prescrito = "Prescrito";

    private static readonly (string Codigo, string Nombre, string Descripcion, string[] Estados, bool Masivo, string[] Alias)[] Tesis =
    [
        ("DP", "Petición estándar",
            "Derecho de petición general (art. 23 de la Constitución y Ley 1755 de 2015) para solicitar información, copias y la revisión del comparendo.",
            [EnTiempo, Vencido, Caducado, Prescrito], false, []),
        ("DA", "Solicitud de audiencia",
            "Solicita la audiencia pública para controvertir el comparendo dentro de los términos legales (art. 136 del Código Nacional de Tránsito).",
            [EnTiempo], false, []),
        ("DI", "C-038",
            "Invoca la sentencia C-038 de 2020: en las fotomultas la responsabilidad es personal y debe probarse quién conducía; el propietario no responde de forma objetiva.",
            [EnTiempo, Vencido], false, []),
        ("DC", "Caducidad masiva",
            "Solicita declarar la caducidad de la acción por haber transcurrido más de un año sin decisión de fondo (art. 161 del Código Nacional de Tránsito). Admite envío en lote ante un mismo organismo.",
            [Caducado], true, ["DP-CAD"]),
        ("DCI", "Caducidad individual",
            "Solicita declarar la caducidad de la acción de un comparendo individual (art. 161 del Código Nacional de Tránsito).",
            [Caducado], false, ["DP-CAD"]),
        ("DBN", "Mala notificación",
            "Alega la indebida notificación del comparendo (art. 8 de la Ley 1843 de 2017): la notificación no se hizo en los términos y por los medios previstos.",
            [EnTiempo, Vencido], false, ["DP-VICIO"]),
        ("DPR", "Prescripción",
            "Solicita declarar la prescripción de la acción de cobro por haber transcurrido tres años (art. 159 del Código Nacional de Tránsito).",
            [Prescrito], false, ["DP-PRES"]),
        ("DLL", "Responsabilidad de persona jurídica",
            "Expone que la empresa propietaria no es la infractora y pide identificar al conductor responsable, con la información del contrato del vehículo.",
            [EnTiempo, Vencido], false, []),
        ("DRP", "Nulidad del proceso (C-321)",
            "Pide la nulidad del proceso contravencional por vulneración del debido proceso (sentencia C-321 de 2022).",
            [EnTiempo, Vencido], false, ["DP-VICIO"]),
        ("AN", "Notificación (acto)",
            "Solicita la notificación en debida forma del acto administrativo que impuso la sanción.",
            [EnTiempo, Vencido], false, []),
        ("AA", "Acto administrativo",
            "Solicita copia y revisión del acto administrativo sancionatorio y de su ejecutoria.",
            [EnTiempo, Vencido], false, []),
    ];

    public static async Task SembrarAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        var existentes = await db.Tipologias.Select(t => t.Codigo).ToListAsync(ct).ConfigureAwait(false);
        foreach (var t in Tesis.Where(t => !existentes.Contains(t.Codigo)))
        {
            db.Tipologias.Add(new TipologiaDp
            {
                Id = Guid.CreateVersion7(),
                Codigo = t.Codigo,
                Nombre = t.Nombre,
                DescripcionJuridica = t.Descripcion,
                PrefijoRadicado = t.Codigo,
                EstadosLegalesPermitidos = [.. t.Estados],
                PermiteMasivo = t.Masivo,
                PlazoRespuestaOtDiasHabiles = 15,
                AdjuntosRequeridos = ["Cedula", "CertificadoRepresentacion", "EscrituraPoder"],
                AliasV2 = [.. t.Alias],
                LegacyV1Codigo = t.Codigo,
                CreadoEn = ahora,
            });
        }
    }
}
