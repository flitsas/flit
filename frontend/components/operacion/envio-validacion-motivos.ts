/**
 * HU #11666 — traducción a lenguaje de gestor de los motivos tipificados de NO envío de la
 * validación de identidad (HU #11665, `EnvioValidacionBloqueoRules`).
 *
 * El backend expone códigos estables (`proveedor_no_envia`, `rl_sin_documento`, …) más un flag
 * `informativo`. Aquí vive el ÚNICO sitio donde esos códigos se convierten en texto y en acción
 * correctiva, para que la tarjeta de la parte, el historial de identidad y cualquier consumidor
 * futuro digan exactamente lo mismo.
 *
 * Dos reglas de redacción, ambas heredadas del contrato del backend:
 *  1. El motivo NO afirma más de lo que el código dice. `sujeto_no_es_representante` significa que
 *     el sujeto resuelto no está marcado como representante legal — no que la empresa no tenga uno.
 *  2. `informativo: true` NO es un fallo: explica una ausencia legítima (la parte ya está cubierta)
 *     y no se pinta como bloqueo ni sugiere corrección.
 */

import type { EnvioValidacionMotivo } from '@/lib/api/types/procedure-runtime';

/** Destino de la acción correctiva. `null` = el gestor no puede hacer nada al respecto. */
export type MotivoAccion = 'actores' | null;

export interface MotivoPresentacion {
  /** `bloqueo` = hay que resolverlo para que la validación salga; `informacion` = solo explica. */
  naturaleza: 'bloqueo' | 'informacion';
  titulo: string;
  detalle: string;
  /** Paso al que lleva la corrección, o `null` cuando no depende del gestor. */
  accion: MotivoAccion;
  /** Rótulo del botón de corrección. Solo presente cuando `accion` no es `null`. */
  accionLabel?: string;
}

/**
 * Códigos que el backend publica hoy (espejo de `EnvioValidacionMotivos`). El tipo del contrato
 * sigue siendo `string`: un código nuevo no debe romper la pantalla, cae en el texto genérico.
 */
export const ENVIO_VALIDACION_MOTIVO_CODIGOS = [
  'proveedor_no_envia',
  'sujeto_no_es_representante',
  'rl_sin_documento',
  'rl_sin_correo',
  'cubierto_por_baul',
  'representante_utilizable',
] as const;

export type EnvioValidacionMotivoCodigo = (typeof ENVIO_VALIDACION_MOTIVO_CODIGOS)[number];

const ACCION_ACTORES_LABEL = 'Completar datos del representante legal';

/**
 * Texto de cara al gestor para un código de motivo. `parteLabel` es el rótulo del rol
 * («Comprador» / «Vendedor») para que el aviso nombre a quién se refiere sin repetir el rol
 * en el título.
 */
export function presentarMotivoNoEnvio(
  codigo: string,
  parteLabel: string,
): MotivoPresentacion {
  switch (codigo) {
    // Depende del ambiente, no del trámite: el gestor no tiene nada que corregir aquí.
    case 'proveedor_no_envia':
      return {
        naturaleza: 'bloqueo',
        titulo: 'No se envió la validación de identidad',
        detalle:
          'El proveedor de validación de identidad configurado en este ambiente no emite envíos. No hay nada que corregir en el trámite.',
        accion: null,
      };
    case 'rl_sin_documento':
      return {
        naturaleza: 'bloqueo',
        titulo: 'Falta el documento del representante legal',
        detalle: `El representante legal registrado para el ${parteLabel.toLowerCase()} no tiene tipo o número de documento, y sin ese dato no se le puede enviar la validación de identidad.`,
        accion: 'actores',
        accionLabel: ACCION_ACTORES_LABEL,
      };
    case 'rl_sin_correo':
      return {
        naturaleza: 'bloqueo',
        titulo: 'Falta el correo del representante legal',
        detalle: `El representante legal registrado para el ${parteLabel.toLowerCase()} no tiene correo, y es ahí donde llega el enlace de la validación de identidad.`,
        accion: 'actores',
        accionLabel: ACCION_ACTORES_LABEL,
      };
    // Cuidado con el copy: el código dice que el sujeto resuelto no está marcado como
    // representante legal, no que la empresa carezca de uno.
    case 'sujeto_no_es_representante':
      return {
        naturaleza: 'bloqueo',
        titulo: 'La parte no tiene un representante legal registrado',
        detalle: `La validación de identidad de una persona jurídica se le envía a su representante legal, y el ${parteLabel.toLowerCase()} no tiene uno registrado en el trámite.`,
        accion: 'actores',
        accionLabel: ACCION_ACTORES_LABEL,
      };
    case 'cubierto_por_baul':
      return {
        naturaleza: 'informacion',
        titulo: 'No hace falta enviar la validación',
        detalle: `El ${parteLabel.toLowerCase()} firma con la firma electrónica del baúl, así que su identidad ya queda acreditada.`,
        accion: null,
      };
    case 'representante_utilizable':
      return {
        naturaleza: 'informacion',
        titulo: 'No hace falta enviar la validación',
        detalle: `El representante legal del ${parteLabel.toLowerCase()} ya tiene una validación de identidad aprobada y vigente.`,
        accion: null,
      };
    default:
      // Código desconocido (backend más nuevo que esta pantalla): se avisa de la ausencia sin
      // inventarle una causa ni una corrección.
      return {
        naturaleza: 'bloqueo',
        titulo: 'No se envió la validación de identidad',
        detalle: `El trámite reporta un motivo que esta pantalla todavía no sabe explicar (${codigo}). Consúltalo con soporte antes de continuar.`,
        accion: null,
      };
  }
}

/** Motivo de una parte concreta dentro de la respuesta del estado biométrico. */
export function motivoDeParte(
  motivos: EnvioValidacionMotivo[] | null | undefined,
  parte: string,
): EnvioValidacionMotivo | null {
  if (!motivos?.length) return null;
  return (
    motivos.find((m) => m.parte?.toLowerCase() === parte.toLowerCase()) ?? null
  );
}
