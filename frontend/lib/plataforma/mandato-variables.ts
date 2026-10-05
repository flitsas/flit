/**
 * HU #13175 — variables `{{...}}` que el Super Admin puede insertar en la plantilla de un formato.
 *
 * El backend (registro único de variables, HU #13170) es quien valida y devuelve las desconocidas; esta
 * lista solo alimenta los botones de inserción. Quedan fuera a propósito la fecha de la firma y las
 * variables de vigencia o «fijo» (decisión de alcance de la Feature #13118): aunque el registro del
 * backend las acepte, la pantalla no las ofrece.
 */
export interface MandatoVariableInsertable {
  name: string;
  description: string;
}

export const MANDATO_VARIABLES_INSERTABLES: readonly MandatoVariableInsertable[] = [
  { name: "placa", description: "Placa del vehículo." },
  { name: "tramite", description: "Nombre del trámite (objeto del contrato)." },
  { name: "organismo", description: "Organismo de tránsito." },
  { name: "ciudad", description: "Ciudad del organismo." },
  { name: "fecha", description: "Fecha del trámite, en letras." },
  { name: "mandante_nombre", description: "Nombre del mandante (o lista de otorgantes)." },
  { name: "mandante_documento", description: "Documento del mandante." },
  { name: "mandatario_nombre", description: "Nombre del mandatario persona." },
  { name: "mandatario_documento", description: "Documento del mandatario persona." },
  { name: "mandatario_institucional", description: "Razón social del mandatario institucional." },
  { name: "mandatario_nit", description: "NIT del mandatario institucional." },
];

/** Mensajes en español de los códigos de error del PUT / vista previa de formatos. */
export const MANDATO_FORMATO_ERRORES: Readonly<Record<string, string>> = {
  nombre_vacio: "El nombre no puede quedar vacío.",
  nombre_demasiado_largo: "El nombre es demasiado largo.",
  nombre_repetido: "Ya existe otro formato con ese nombre.",
  assignment_mode_invalido: "El tipo de mandato no es válido.",
  formato_sin_plantilla: "La automática no tiene plantilla propia: usa la de cada organismo.",
  plantilla_vacia: "La plantilla no puede estar vacía.",
  plantilla_demasiado_larga: "La plantilla supera el largo máximo permitido.",
  plantilla_sintaxis_invalida: "La plantilla tiene una variable sin cerrar o mal escrita.",
  plantilla_variable_invalida: "La plantilla usa variables que no existen.",
  template_code_invalido: "El formato no admite vista previa.",
  row_version_conflict: "Otro usuario editó este formato mientras lo modificabas.",
};
