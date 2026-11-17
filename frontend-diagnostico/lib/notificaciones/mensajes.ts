import type { NotificacionDiagnostico } from "@/lib/notificaciones/api";

/**
 * Texto de un aviso de la campana (AC53, ADR-0086): la tabla no guarda texto, así que el título y el mensaje se arman
 * aquí con rótulos fijos a partir de `tipo` y `datos`. De `datos` solo se leen números y códigos conocidos, y el único
 * texto variable es el nombre del lote. Nunca placa, VIN, documento, nombre de persona ni nombre de archivo.
 * Un tipo desconocido devuelve null y no se pinta (D8: no hay aviso por dictamen ajustado).
 * Si el servidor no manda el nombre del lote ni `datos` (forma actual de `NotificacionDto`), el mensaje es el que armó el
 * servidor, también con rótulos fijos y el nombre del lote (ADR-0086); el título sigue siendo el del cliente.
 */
export interface TextoNotificacion {
  titulo: string;
  mensaje: string;
}

const numero = (datos: Record<string, unknown> | undefined, clave: string): number | null => {
  const v = datos?.[clave];
  return typeof v === "number" && Number.isFinite(v) && v >= 0 ? Math.trunc(v) : null;
};

const contar = (n: number, singular: string, plural: string) => `${n} ${n === 1 ? singular : plural}`;

export function textoNotificacion(n: NotificacionDiagnostico): TextoNotificacion | null {
  const texto = textoCliente(n);
  if (!texto) return null;
  const delServidor = n.mensaje?.trim();
  if (delServidor && !n.diagnostico_nombre?.trim() && !n.datos) return { titulo: texto.titulo, mensaje: delServidor };
  return texto;
}

function textoCliente(n: NotificacionDiagnostico): TextoNotificacion | null {
  const nombre = n.diagnostico_nombre?.trim();
  const delLote = nombre ? `del lote «${nombre}»` : "de tu lote";
  switch (n.tipo) {
    case "ZIP_EVIDENCIAS_LISTO":
      return { titulo: "ZIP de evidencias listo", mensaje: `Las evidencias ${delLote} están listas para descargar.` };
    case "ZIP_EVIDENCIAS_FALLIDO":
      return { titulo: "No se pudo generar el ZIP de evidencias", mensaje: `Vuelve a pedir las evidencias ${delLote}.` };
    case "LOTE_TERMINADO": {
      const sujeto = nombre ? `El lote «${nombre}»` : "Tu lote";
      const cola = n.datos?.estado === "INCOMPLETO" ? " con consultas incompletas" : "";
      return { titulo: "Diagnóstico terminado", mensaje: `${sujeto} terminó${cola}.` };
    }
    case "DERIVACION_TERMINADA": {
      const partes = [
        ["creadas", "trámite creado", "trámites creados"],
        ["omitidas", "omitido", "omitidos"],
        ["fallidas", "fallido", "fallidos"],
      ]
        .map(([clave, s, p]) => {
          const v = numero(n.datos, clave);
          return v === null ? null : contar(v, s, p);
        })
        .filter((p): p is string => p !== null);
      const cuerpo = partes.length === 0 ? "terminó la creación de trámites" : partes.length === 1 ? partes[0] : `${partes.slice(0, -1).join(", ")} y ${partes.at(-1)}`;
      const prefijo = nombre ? `Lote «${nombre}»` : "Tu lote";
      return { titulo: "Creación de trámites terminada", mensaje: `${prefijo}: ${cuerpo}.` };
    }
    case "SANEAMIENTO_TOMADO":
    case "SANEAMIENTO_CERRADO":
    case "SANEAMIENTO_REABIERTO":
    case "SANEAMIENTO_ANULADO":
      return textoSaneamiento(n, delLote);
    default:
      return null;
  }
}

/**
 * Avisos del saneamiento (HU #14577 AC39, spec-ui §7.2, SP-23): solo `tipo`, `datos.cantidad` (entero, mínimo 1) y, en
 * el cierre, `datos.resultado`; sin cantidad válida o con un resultado desconocido no se pinta. Singular y plural reales
 * (D-08) en vez de «vehículo(s)». Nunca placa ni otro texto de `datos`.
 */
function textoSaneamiento(n: NotificacionDiagnostico, delLote: string): TextoNotificacion | null {
  const cantidad = numero(n.datos, "cantidad");
  if (cantidad === null || cantidad < 1) return null;
  const vehiculos = `${contar(cantidad, "vehículo", "vehículos")} ${delLote}`;
  switch (n.tipo) {
    case "SANEAMIENTO_TOMADO":
      return { titulo: "Saneamiento en gestión", mensaje: `FLIT empezó el saneamiento de ${vehiculos}.` };
    case "SANEAMIENTO_CERRADO":
      if (n.datos?.resultado === "SANEADO")
        return { titulo: "Saneamiento terminado", mensaje: `${vehiculos} ${cantidad === 1 ? "quedó saneado" : "quedaron saneados"}.` };
      if (n.datos?.resultado === "NO_PROCEDE") return { titulo: "Saneamiento no procede", mensaje: `El saneamiento de ${vehiculos} no procede.` };
      return null;
    case "SANEAMIENTO_REABIERTO":
      return { titulo: "Saneamiento reabierto", mensaje: `FLIT reabrió el saneamiento de un vehículo ${delLote}.` };
    case "SANEAMIENTO_ANULADO":
      return { titulo: "Saneamiento anulado", mensaje: `FLIT anuló el saneamiento de un vehículo ${delLote}.` };
    default:
      return null;
  }
}
