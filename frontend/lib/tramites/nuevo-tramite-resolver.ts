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
    const code = primerCodeDisponible(familia, CODES_TRASPASO_UNILATERAL);
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

/** Chips de transformaciones del mockup (UI); no cambian el code principal. */
export const TRANSFORMACIONES_MOCKUP = [
  { id: 'PRENDA_INSCRIPCION', label: 'Inscribir Prenda' },
  { id: 'CAMBIO_COLOR', label: 'Cambio de Color' },
  { id: 'BLINDAJE', label: 'Blindaje' },
  { id: 'CAMBIO_CARROCERIA', label: 'Cambio de Carrocería' },
  { id: 'CONVERSION_COMBUSTIBLE', label: 'Conversiones de Combustible' },
] as const;

export const TIPOS_UI_MOCKUP: {
  id: NuevoTramiteTipoUi;
  title: string;
  subtitle: string;
}[] = [
  {
    id: 'MATRICULAS',
    title: 'Matrícula Inicial',
    subtitle: 'Vehículo nuevo sin placa asignada',
  },
  {
    id: 'TRASPASO',
    title: 'Traspaso',
    subtitle: 'Cambio de propietario del vehículo',
  },
  {
    id: 'OTROS',
    title: 'Otros Trámites',
    subtitle: 'Modificaciones y novedades',
  },
];
