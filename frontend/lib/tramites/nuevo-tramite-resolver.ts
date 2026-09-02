import type { ProcedureFamily, ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

/** Familias que el mockup presenta como las tres tarjetas fijas. */
export type NuevoTramiteTipoUi = 'MATRICULAS' | 'TRASPASO' | 'OTROS';

export type ModalidadTraspasoUi = 'bilateral' | 'unilateral';

/** Selección del modal mockup antes de resolver al catálogo. */
export interface NuevoTramiteSeleccion {
  tipo: NuevoTramiteTipoUi;
  leasing?: boolean;
  modalidadTraspaso?: ModalidadTraspasoUi;
  /** `code` del tipo OTROS elegido en el select. */
  subtipoOtrosCode?: string;
}

export interface FamiliasBloqueadasResolver {
  matriculas?: boolean;
  traspaso?: boolean;
  otros?: boolean;
}

export type NuevoTramiteResolveOk = { ok: true; procedureTypeCode: string };
export type NuevoTramiteResolveErr = {
  ok: false;
  reason: 'blocked' | 'not-found' | 'incomplete';
  message: string;
};
export type NuevoTramiteResolveResult = NuevoTramiteResolveOk | NuevoTramiteResolveErr;

const BLOQUEO: Record<NuevoTramiteTipoUi, keyof FamiliasBloqueadasResolver> = {
  MATRICULAS: 'matriculas',
  TRASPASO: 'traspaso',
  OTROS: 'otros',
};

/** Códigos preferidos por variante; se toma el primero que exista habilitado en el catálogo. */
const CODES_MATRICULA_STD = ['MATRICULA_NUEVA', 'MATRICULA_INICIAL'] as const;
const CODES_MATRICULA_LEASING = ['MATRICULA_LEASING'] as const;
const CODES_TRASPASO_BILATERAL = ['TRASPASO_STANDARD', 'TRASPASO_BILATERAL', 'TRASPASO'] as const;
const CODES_TRASPASO_UNILATERAL = ['TRASPASO_UNILATERAL'] as const;

function habilitadosDeFamilia(
  tipos: ProcedureTypeSummary[],
  family: ProcedureFamily,
): ProcedureTypeSummary[] {
  return tipos.filter((t) => t.wizardEnabled && t.family === family);
}

/**
 * Primer código preferido que esté habilitado; si ninguno lo está, CUALQUIER otro de la familia.
 *
 * <p>La caída al primero de la familia solo vale donde las variantes son intercambiables para
 * empezar el trámite: da igual entrar por `MATRICULA_NUEVA` o por `MATRICULA_INICIAL`, y un traspaso
 * bilateral es un traspaso bilateral se llame como se llame el código. Donde la variante ES el
 * trámite —el unilateral— no se puede caer a otra: ver {@link soloCodeExacto}.</p>
 */
function primerCodeDisponible(
  tiposFamilia: ProcedureTypeSummary[],
  preferidos: readonly string[],
): string | null {
  for (const code of preferidos) {
    if (tiposFamilia.some((t) => t.code === code)) return code;
  }
  return tiposFamilia[0]?.code ?? null;
}

/**
 * El código pedido, o nada. Sin caída al resto de la familia.
 *
 * <p>Existe por un defecto silencioso: el traspaso unilateral se resolvía con
 * {@link primerCodeDisponible}, cuya caída devuelve el primer tipo habilitado de la familia. Con
 * `TRASPASO_UNILATERAL` apagado y `TRASPASO_STANDARD` encendido —que es como estuvo el catálogo desde
 * que existe el tipo— elegir «Traspaso Unilateral» en el modal abría un traspaso BILATERAL sin decir
 * nada: otro FUR, otros firmantes (en el unilateral el locatario no firma, art. 5.3.2.2), otro
 * checklist. El mensaje de «no está habilitado» nunca llegaba a verse.</p>
 *
 * <p>Un trámite equivocado y silencioso es peor que una opción bloqueada con su motivo.</p>
 */
function soloCodeExacto(
  tiposFamilia: ProcedureTypeSummary[],
  code: string,
): string | null {
  return tiposFamilia.some((t) => t.code === code) ? code : null;
}

/**
 * Traduce la selección del modal mockup a un `procedureTypeCode` del catálogo operable.
 *
 * Puro: no llama red. Recibe tipos ya cargados (`listPublishedProcedureTypes`) y bloqueos de
 * compañía (`getConsultationConfig`). Si el code no está habilitado, no inventa tipos.
 */
export function resolveNuevoTramiteCode(
  seleccion: NuevoTramiteSeleccion,
  tipos: ProcedureTypeSummary[],
  bloqueadas?: FamiliasBloqueadasResolver,
): NuevoTramiteResolveResult {
  if (bloqueadas?.[BLOQUEO[seleccion.tipo]] === true) {
    return {
      ok: false,
      reason: 'blocked',
      message: 'Tu compañía no tiene habilitada la creación de este tipo de trámite.',
    };
  }

  if (seleccion.tipo === 'OTROS') {
    const code = seleccion.subtipoOtrosCode?.trim();
    if (!code) {
      return {
        ok: false,
        reason: 'incomplete',
        message: 'Selecciona el trámite a realizar.',
      };
    }
    const encontrado = tipos.find((t) => t.code === code && t.wizardEnabled && t.family === 'OTROS');
    if (!encontrado) {
      return {
        ok: false,
        reason: 'not-found',
        message: 'El trámite seleccionado no está disponible o aún no está habilitado.',
      };
    }
    return { ok: true, procedureTypeCode: encontrado.code };
  }

  if (seleccion.tipo === 'MATRICULAS') {
    const familia = habilitadosDeFamilia(tipos, 'MATRICULAS');
    const preferidos = seleccion.leasing ? CODES_MATRICULA_LEASING : CODES_MATRICULA_STD;
    let code = primerCodeDisponible(familia, preferidos);
    // Leasing sin tipo dedicado: cae a matrícula estándar (wizard declara leasing en paso 1).
    if (!code && seleccion.leasing) {
      code = primerCodeDisponible(familia, CODES_MATRICULA_STD);
    }
    if (!code) {
      return {
        ok: false,
        reason: 'not-found',
        message: 'No hay tipos de matrícula habilitados para crear.',
      };
    }
    return { ok: true, procedureTypeCode: code };
  }

  // TRASPASO
  const familia = habilitadosDeFamilia(tipos, 'TRASPASO');
  const modalidad = seleccion.modalidadTraspaso ?? 'bilateral';
  if (modalidad === 'unilateral') {
    // Exacto y sin caída: el unilateral no es una variante de nombre del bilateral (ver ADR-0051).
    const code = soloCodeExacto(familia, CODES_TRASPASO_UNILATERAL[0]);
    if (!code) {
      return {
        ok: false,
        reason: 'not-found',
        message: 'El traspaso unilateral no está habilitado en el catálogo.',
      };
    }
    return { ok: true, procedureTypeCode: code };
  }

  const code = primerCodeDisponible(familia, CODES_TRASPASO_BILATERAL);
  if (!code) {
    return {
      ok: false,
      reason: 'not-found',
      message: 'No hay tipos de traspaso habilitados para crear.',
    };
  }
  return { ok: true, procedureTypeCode: code };
}

export const TIPOS_UI_MOCKUP: {
  id: NuevoTramiteTipoUi;
  title: string;
  /** Ya no se pinta en la tarjeta (el diseño la deja con icono + título + desplegable). Se conserva
   *  como texto accesible del icono y para los mensajes de familia bloqueada / sin tipos. */
  subtitle: string;
  /** Ilustración de la tarjeta. Trae su círculo azul dentro, igual que los iconos de estado. */
  icon: string;
  /** Rótulo del desplegable de la tarjeta cuando aún no se ha elegido nada. */
  placeholder: string;
}[] = [
  {
    id: 'MATRICULAS',
    title: 'Matrícula Inicial',
    subtitle: 'Vehículo nuevo sin placa asignada',
    icon: '/assets/tipos-tramite/matriculas.svg',
    placeholder: 'Selecciona tipo',
  },
  {
    id: 'TRASPASO',
    title: 'Traspaso',
    subtitle: 'Cambio de propietario del vehículo',
    icon: '/assets/tipos-tramite/traspaso.svg',
    placeholder: 'Selecciona modalidad',
  },
  {
    id: 'OTROS',
    title: 'Otros Trámites',
    subtitle: 'Modificaciones y novedades',
    icon: '/assets/tipos-tramite/otros.svg',
    placeholder: 'Selecciona trámite',
  },
];

/**
 * Texto de la franja informativa del selector: explica en una línea qué implica la configuración
 * elegida. Null mientras no haya nada que aclarar — la franja se reserva igual (ver el modal), para
 * que el alto no salte al elegir.
 *
 * "Otros" no tiene texto: son quince tipos con explicaciones propias, y una frase genérica no diría
 * nada que el nombre del trámite ya elegido no diga mejor.
 */
export function infoTextNuevoTramite(
  tipo: NuevoTramiteTipoUi | null,
  opciones: { leasing?: boolean; modalidadTraspaso?: ModalidadTraspasoUi },
): string | null {
  if (tipo === 'MATRICULAS') {
    return opciones.leasing
      ? 'Matrícula tipo Leasing: el vehículo queda registrado a nombre de la entidad financiera (arrendador), mientras lo usas como locatario según el contrato.'
      : 'Matrícula tradicional: el vehículo nuevo será matriculado a nombre del comprador ante el organismo de tránsito elegido.';
  }
  if (tipo === 'TRASPASO') {
    return opciones.modalidadTraspaso === 'unilateral'
      ? 'Traspaso unilateral: traspaso realizado únicamente por el propietario actual, sin requerir la presencia del comprador en la sede de tránsito.'
      : 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.';
  }
  return null;
}
