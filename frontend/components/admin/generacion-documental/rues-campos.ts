/**
 * Traducción de las claves de campo mercantil del RUES a etiquetas en español.
 *
 * El contrato (`StandaloneRuesPreviewField` en `contracts/openapi/core-api.v1.yaml`) solo entrega
 * `key` y `value`: la etiqueta legible es responsabilidad del frontend. Las claves son las que
 * emite `VerifikRuesConsultationProvider`; no están inventadas.
 */

/**
 * `rues_representacion_legal` NO trae el nombre del representante legal: el servicio devuelve la
 * FACULTAD de representación (`LegalRepresentatives.Faculty`). Etiquetarlo «Representante legal»
 * haría leer un texto de facultades como si fuera el nombre de una persona.
 */
const ETIQUETAS: Readonly<Record<string, string>> = {
  rues_nit: "NIT",
  rues_razon_social: "Razón social",
  rues_sigla: "Sigla",
  rues_estado: "Estado de la matrícula",
  rues_matricula_mercantil: "Matrícula mercantil",
  rues_fecha_matricula: "Fecha de matrícula",
  rues_ultimo_ano_renovado: "Último año renovado",
  rues_fecha_renovacion: "Fecha de renovación",
  rues_fecha_actualizacion: "Fecha de actualización",
  rues_razon_cancelacion: "Razón de cancelación",
  rues_camara_comercio: "Cámara de comercio",
  rues_camara_ciudad: "Ciudad de la cámara",
  rues_camara_departamento: "Departamento de la cámara",
  rues_direccion: "Dirección",
  rues_municipio: "Municipio",
  rues_email: "Correo electrónico",
  rues_categoria: "Categoría",
  rues_tipo_organizacion: "Tipo de organización",
  rues_tipo_compania: "Tipo de compañía",
  rues_actividad_economica: "Actividad económica",
  rues_representacion_legal: "Facultad de representación legal",
  rues_id_rm: "Id. del registro mercantil",
};

/**
 * Campos que el proveedor entrega para uso de máquina, no para leerse en pantalla.
 * `rues_actividades_json` es un JSON serializado; la actividad legible ya sale en
 * `rues_actividad_economica`.
 */
const OCULTOS: ReadonlySet<string> = new Set(["rues_actividades_json"]);

/** ¿Este campo se muestra en la revisión previa? */
export function esCampoRuesVisible(key: string): boolean {
  return !OCULTOS.has(key);
}

/**
 * Etiqueta legible de una clave del RUES.
 *
 * Una clave desconocida no se descarta ni se pinta cruda: se deriva quitando el prefijo `rues_` y
 * cambiando los guiones bajos por espacios. Así, cuando el proveedor añada un campo, aparece
 * legible en vez de desaparecer sin que nadie se entere.
 */
export function etiquetaDeCampoRues(key: string): string {
  const conocida = ETIQUETAS[key];
  if (conocida) {
    return conocida;
  }

  const derivada = key.replace(/^rues_/, "").replace(/_/g, " ").trim();
  if (derivada.length === 0) {
    return key;
  }

  return derivada.charAt(0).toUpperCase() + derivada.slice(1);
}
