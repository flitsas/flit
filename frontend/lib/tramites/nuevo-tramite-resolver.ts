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
 * Resuelve el tipo del catálogo (con su `description`) que corresponde a la selección actual del
 * modal mockup, para la familia+variante indicadas. Misma lógica de preferencia de codes que
 * {@link resolveNuevoTramiteCode}, pero sin mirar `bloqueadas`: una franja informativa no necesita
 * ese chequeo — si la familia está bloqueada, la tarjeta ya está deshabilitada y `tipo` nunca llega
 * a fijarse (ver `elegirEnTarjeta` en `NuevoTramiteModalContent`).
 */
function resolverTipoSeleccionado(
  seleccion: NuevoTramiteSeleccion,
  tipos: ProcedureTypeSummary[],
): ProcedureTypeSummary | null {
  if (seleccion.tipo === 'OTROS') {
    const code = seleccion.subtipoOtrosCode?.trim();
    if (!code) return null;
    return tipos.find((t) => t.code === code && t.wizardEnabled && t.family === 'OTROS') ?? null;
  }

  if (seleccion.tipo === 'MATRICULAS') {
    const familia = habilitadosDeFamilia(tipos, 'MATRICULAS');
    const preferidos = seleccion.leasing ? CODES_MATRICULA_LEASING : CODES_MATRICULA_STD;
    let code = primerCodeDisponible(familia, preferidos);
    if (!code && seleccion.leasing) {
      code = primerCodeDisponible(familia, CODES_MATRICULA_STD);
    }
    return code ? (familia.find((t) => t.code === code) ?? null) : null;
  }

  // TRASPASO
  const familia = habilitadosDeFamilia(tipos, 'TRASPASO');
  const modalidad = seleccion.modalidadTraspaso ?? 'bilateral';
  if (modalidad === 'unilateral') {
    const code = soloCodeExacto(familia, CODES_TRASPASO_UNILATERAL[0]);
    return code ? (familia.find((t) => t.code === code) ?? null) : null;
  }
  const code = primerCodeDisponible(familia, CODES_TRASPASO_BILATERAL);
  return code ? (familia.find((t) => t.code === code) ?? null) : null;
}

/**
 * Texto de la franja informativa del selector: explica en una línea qué implica la configuración
 * elegida. Null mientras no haya nada que aclarar — la franja se reserva igual (ver el modal), para
 * que el alto no salte al elegir.
 *
 * El copy sale de `description` del tipo de trámite resuelto en el catálogo (HU #12126): antes
 * Matrícula y Traspaso tenían texto fijo en código y "Otros" no mostraba nada. Ahora las tres
 * familias dependen 100% de lo configurado — si el tipo elegido no tiene `description`, no hay
 * franja (AC2), sin caer en un texto de relleno genérico.
 */
export function infoTextNuevoTramite(
  tipo: NuevoTramiteTipoUi | null,
  opciones: { leasing?: boolean; modalidadTraspaso?: ModalidadTraspasoUi; subtipoOtrosCode?: string },
  tipos: ProcedureTypeSummary[],
): string | null {
  if (!tipo) return null;
  const encontrado = resolverTipoSeleccionado(
    {
      tipo,
      leasing: opciones.leasing,
      modalidadTraspaso: opciones.modalidadTraspaso,
      subtipoOtrosCode: opciones.subtipoOtrosCode,
    },
    tipos,
  );
  const descripcion = encontrado?.description?.trim();
  return descripcion ? descripcion : null;
}
