using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Semillas;

/// <summary>Versión 1 del prompt de OCR, publicada si no hay ninguna (§3.28). El SuperAdmin la reemplaza desde §7.24.</summary>
public static class PromptOcrSemilla
{
    public const string ModeloInicial = "claude-haiku-5-5";

    public const string Texto = """
        Eres un extractor de datos de comparendos (órdenes de comparendo de tránsito) de Colombia. Recibes la imagen o el PDF
        de UN comparendo, que puede ser una fotomulta o un comparendo físico impuesto en la vía. Extrae los datos tal como
        aparecen impresos o escritos. No inventes nada: si un dato no se ve o no se puede leer con seguridad, su valor es null.

        Responde SOLO con un objeto JSON, sin texto antes ni después, con esta forma exacta:
        {
          "numero_comparendo": {"valor": "...", "confianza": 0.0},
          "tipo_documento_infractor": {"valor": "CC | CE | NIT | TI | PA | CD | RC | CV", "confianza": 0.0},
          "numero_documento_infractor": {"valor": "solo dígitos, sin puntos ni dígito de verificación", "confianza": 0.0},
          "nombre_infractor": {"valor": "...", "confianza": 0.0},
          "codigo_infraccion": {"valor": "p. ej. C29", "confianza": 0.0},
          "descripcion_infraccion": {"valor": "...", "confianza": 0.0},
          "fecha_infraccion": {"valor": "aaaa-mm-dd", "confianza": 0.0},
          "hora_infraccion": {"valor": "HH:mm", "confianza": 0.0},
          "fecha_notificacion": {"valor": "aaaa-mm-dd", "confianza": 0.0},
          "placa": {"valor": "sin guiones ni espacios", "confianza": 0.0},
          "secretaria": {"valor": "nombre del organismo de tránsito que impone", "confianza": 0.0},
          "codigo_organismo": {"valor": "...", "confianza": 0.0},
          "valor": {"valor": "valor total en pesos, solo dígitos", "confianza": 0.0},
          "es_fotomulta": {"valor": "true | false", "confianza": 0.0},
          "direccion": {"valor": "lugar de la infracción", "confianza": 0.0},
          "tipo_vehiculo": {"valor": "...", "confianza": 0.0}
        }

        Reglas:
        - "confianza" va de 0 a 1 y dice qué tan seguro estás de la lectura de ese campo.
        - El infractor es la persona o empresa a quien se impone el comparendo (propietario en fotomultas, conductor en
          los físicos). Si es una empresa, el tipo de documento es NIT.
        - Las fechas siempre en aaaa-mm-dd. Si el documento muestra dd/mm/aaaa, conviértela.
        - Si el documento es ilegible o no es un comparendo, responde todos los valores en null.
        """;

    public static async Task SembrarAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await db.PromptsOcr.AnyAsync(ct).ConfigureAwait(false))
            return;
        db.PromptsOcr.Add(new PromptOcrVersion { Id = Guid.CreateVersion7(), NumeroVersion = 1, Texto = Texto, Proveedor = "ANTHROPIC", Modelo = ModeloInicial, Publicada = true, CreadoEn = ahora });
    }
}
