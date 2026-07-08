// Lista estática de tipos de trámite (HU #10198). No existe `GET /admin/procedure-types`
// en el contrato (#10116 es otro feature); para AC2–AC5 esta lista alimenta el selector
// y la navegación `/admin/documents/procedures/[procedureTypeId]`. Los ids están alineados
// a los seeds DEV (`tramites.procedure_types`). No añadir endpoints backend en esta HU.
export interface ProcedureTypeOption {
  id: string;
  code: string;
  name: string;
}

export const PROCEDURE_TYPES: ProcedureTypeOption[] = [
  // HU #10522 — alineados a los procedure_type que usan los trámites reales, para que la
  // configuración del gestor y el checklist del trámite operen sobre el MISMO tipo (coordinados):
  // Traspaso → TRASPASO_STANDARD, Matrícula inicial → MATRICULA_NUEVA.
  { id: "019ef140-f2d8-720f-a624-b0e526f87002", code: "TRASPASO_STANDARD", name: "Traspaso" },
  { id: "019ef140-f24e-78e4-8e6d-97faa44ed7a8", code: "MATRICULA_NUEVA", name: "Matrícula inicial" },
  { id: "55555555-5555-5555-5555-555555555555", code: "DUPLICADO_PLACA", name: "Duplicado de placa" },
  { id: "66666666-6666-6666-6666-666666666666", code: "CAMBIO_SERVICIO", name: "Cambio de servicio" },
];

/** Devuelve el tipo de trámite por id, o `undefined` si no está en la lista estática. */
export function findProcedureType(id: string): ProcedureTypeOption | undefined {
  return PROCEDURE_TYPES.find((p) => p.id === id);
}
