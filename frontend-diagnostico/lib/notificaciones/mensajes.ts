import type { NotificacionDiagnostico } from "@/lib/notificaciones/api";

/**
 * Texto de un aviso de la campana (AC53, ADR-0086): la tabla no guarda texto, así que el título y el mensaje se arman
 * aquí con rótulos fijos a partir de `tipo` y `datos`. De `datos` solo se leen números y códigos conocidos, y el único
 * texto variable es el nombre del lote. Nunca placa, VIN, documento, nombre de persona ni nombre de archivo.
 * Un tipo desconocido devuelve null y no se pinta (D8: no hay aviso por dictamen ajustado).
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
    default:
      return null;
  }
}
