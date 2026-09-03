import type { OtClientProcedure } from "@/lib/api/types-ot";

/** Etiqueta legible del estado del SOAT en el RUNT. */
export function soatEstadoLabel(value: string | null | undefined): string {
  if (!value) return "—";
  if (value === "vigente") return "Vigente";
  if (value === "vencido") return "Vencido";
  if (value === "unknown") return "Desconocido";
  return value;
}

/**
 * Pendientes que el OT debe tener a la vista antes de decidir (Bug #11585).
 *
 * Vive fuera del acordeón porque su sitio es el encabezado del modal —la banda de aviso del
 * prototipo—: son la razón por la que un trámite todavía no se puede resolver, y enterrarlos dentro
 * de un acordeón plegado los volvería invisibles justo cuando más se necesitan (HU #12061).
 *
 * La lista vacía es información: significa que no hay nada que frene la decisión, y entonces no se
 * pinta banda alguna.
 */
export function pendientesDelTramite(
  procedure: OtClientProcedure,
  totalDocumentos: number,
): string[] {
  const items: string[] = [];

  if (procedure.plateFlowStatus === "preasignado") {
    items.push("Pendiente asignar placa por el OT.");
  }
  if (procedure.plateFlowStatus === "asignado") {
    items.push("Pendiente proceso del gestor (Asignado → Terminado) antes de decidir.");
  }
  if (procedure.soatEstado && procedure.soatEstado !== "vigente") {
    items.push(`SOAT RUNT no vigente (${soatEstadoLabel(procedure.soatEstado)}).`);
  }
  if (totalDocumentos === 0) {
    items.push("El expediente aún no tiene documentos.");
  }

  return items;
}
