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
  { id: "33333333-3333-3333-3333-333333333333", code: "TRASPASO", name: "Traspaso" },
  { id: "44444444-4444-4444-4444-444444444444", code: "MATRICULA_INICIAL", name: "Matrícula inicial" },
  { id: "55555555-5555-5555-5555-555555555555", code: "DUPLICADO_PLACA", name: "Duplicado de placa" },
  { id: "66666666-6666-6666-6666-666666666666", code: "CAMBIO_SERVICIO", name: "Cambio de servicio" },
];

/** Devuelve el tipo de trámite por id, o `undefined` si no está en la lista estática. */
export function findProcedureType(id: string): ProcedureTypeOption | undefined {
  return PROCEDURE_TYPES.find((p) => p.id === id);
}
