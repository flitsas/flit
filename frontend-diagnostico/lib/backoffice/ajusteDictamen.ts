import { ApiError } from "@/lib/api";

/** Alertamiento del ajuste FLIT (B7): código del catálogo o AJUSTE_FLIT, texto ≤ 500, efecto y fuente. */
export interface AlertamientoAjuste {
  codigo: string;
  texto: string;
  efecto: "BLOQUEA" | "ADVIERTE";
  fuente: string;
}

/** Fila de la lista editable: el alertamiento más su clave estable y la marca local de lo que hizo soporte. */
export interface FilaAlertamiento extends AlertamientoAjuste {
  /** «Alertamiento N»: nombre accesible y clave de la fila; el número no se reutiliza al eliminar. */
  clave: string;
  ajuste?: "AGREGADO" | "EDITADO";
}

/** Código del alertamiento libre (B7): el único al que aplica el filtro de dígitos de P3. */
export const CODIGO_AJUSTE_FLIT = "AJUSTE_FLIT";
export const TEXTO_ALERTAMIENTO_MAX = 500;
/** P3: secuencias de 6 o más dígitos (posibles documentos) en alertamientos visibles a la compañía. */
const SECUENCIA_DIGITOS = /\d{6,}/;

/** PUT del ajuste FLIT (ADR-0086, D9). El cuerpo viejo `{veredicto, confirmado}` se retiró (B-PR6). */
export interface CuerpoAjusteDictamen {
  version_esperada: number;
  veredicto: "VIABLE" | "BLOQUEADO";
  alertamientos: AlertamientoAjuste[];
  motivo: string;
  confirmar_con_tramite?: boolean;
}

export const MOTIVO_MIN = 10;
export const MOTIVO_MAX = 500;

export function cuerpoAjusteDictamen(p: {
  versionEsperada: number;
  veredicto: CuerpoAjusteDictamen["veredicto"];
  alertamientos: ReadonlyArray<AlertamientoAjuste>;
  motivo: string;
  confirmarConTramite?: boolean;
}): CuerpoAjusteDictamen {
  const cuerpo: CuerpoAjusteDictamen = {
    version_esperada: p.versionEsperada,
    veredicto: p.veredicto,
    alertamientos: p.alertamientos.map(({ codigo, texto, efecto, fuente }) => ({ codigo, texto: texto.trim(), efecto, fuente })),
    motivo: p.motivo.trim(),
  };
  if (p.confirmarConTramite) cuerpo.confirmar_con_tramite = true;
  return cuerpo;
}

/** D9: motivo obligatorio de 10 a 500 caracteres. Devuelve el error o null. */
export function validarMotivoAjuste(motivo: string): string | null {
  const m = motivo.trim();
  if (m.length === 0) return "El motivo es obligatorio.";
  if (m.length < MOTIVO_MIN) return `Escribe al menos ${MOTIVO_MIN} caracteres.`;
  if (m.length > MOTIVO_MAX) return `El motivo admite máximo ${MOTIVO_MAX} caracteres.`;
  return null;
}

export interface ErrorCampoAjuste {
  /** Ruta del campo como la manda el API en el 422 (`alertamientos[i].texto`) o `veredicto` para la coherencia. */
  campo: string;
  mensaje: string;
}

/** Valida un alertamiento suelto (al guardar su fila). */
export function validarAlertamiento(a: AlertamientoAjuste): string | null {
  const texto = a.texto.trim();
  if (!texto) return "Escribe el texto del alertamiento.";
  if (texto.length > TEXTO_ALERTAMIENTO_MAX) return `El texto admite máximo ${TEXTO_ALERTAMIENTO_MAX} caracteres.`;
  if (a.codigo === CODIGO_AJUSTE_FLIT && SECUENCIA_DIGITOS.test(texto)) return "No escribas secuencias de 6 o más dígitos (pueden ser documentos).";
  return null;
}

/** Valida la lista antes de aplicar: cada fila y la coherencia con el veredicto (la misma regla del 422 DICTAMEN_INCOHERENTE). */
export function validarAlertamientos(veredicto: CuerpoAjusteDictamen["veredicto"], lista: ReadonlyArray<AlertamientoAjuste>): ErrorCampoAjuste | null {
  for (const [i, a] of lista.entries()) {
    const error = validarAlertamiento(a);
    if (error) return { campo: `alertamientos[${i}].texto`, mensaje: error };
  }
  const bloquea = lista.some((a) => a.efecto === "BLOQUEA");
  if (veredicto === "VIABLE" && bloquea) return { campo: "veredicto", mensaje: MENSAJES.DICTAMEN_INCOHERENTE };
  if (veredicto === "BLOQUEADO" && !bloquea) return { campo: "veredicto", mensaje: MENSAJES.DICTAMEN_INCOHERENTE };
  return null;
}

/** «Aplicar» solo se habilita con veredicto elegido, motivo válido (D9) y alertamientos válidos. */
export function puedeAplicarAjuste(p: { veredicto: CuerpoAjusteDictamen["veredicto"] | null; motivo: string; alertamientos: ReadonlyArray<AlertamientoAjuste> }): boolean {
  return p.veredicto !== null && validarMotivoAjuste(p.motivo) === null && validarAlertamientos(p.veredicto, p.alertamientos) === null;
}

export interface ErrorAjuste {
  mensaje: string;
  /** Campo del cuerpo al que pertenece el error (422 con `campo`, p. ej. el filtro de dígitos P3), o null. */
  campo: string | null;
  ofrecerRecargar: boolean;
  pedirConfirmarTramite: boolean;
}

const MENSAJES: Readonly<Record<string, string>> = {
  DICTAMEN_DESACTUALIZADO: "El dictamen cambió mientras editabas. Recarga para ver la versión vigente",
  DICTAMEN_EN_CONFLICTO: "Otro ajuste se aplicó al mismo tiempo. Recarga para ver la versión vigente",
  DICTAMEN_INCOHERENTE: "Un dictamen Viable no puede tener alertamientos que bloqueen, y uno Bloqueado necesita al menos uno",
  VEHICULO_CON_TRAMITE: "El vehículo ya tiene un trámite creado; el ajuste no lo modifica. Confirma para continuar",
};

/** AC46: traduce el error del PUT a un mensaje fijo; lo digitado se conserva en el panel. */
export function errorAjusteDictamen(error: unknown): ErrorAjuste {
  const base: ErrorAjuste = { mensaje: "No se pudo aplicar el ajuste. Intenta de nuevo en un momento.", campo: null, ofrecerRecargar: false, pedirConfirmarTramite: false };
  if (!(error instanceof ApiError)) return base;
  const code = error.code ?? "";
  if (MENSAJES[code]) {
    return {
      ...base,
      mensaje: MENSAJES[code],
      ofrecerRecargar: code === "DICTAMEN_DESACTUALIZADO" || code === "DICTAMEN_EN_CONFLICTO",
      pedirConfirmarTramite: code === "VEHICULO_CON_TRAMITE",
    };
  }
  if (error.status === 422 && error.campo) return { ...base, mensaje: error.message, campo: error.campo };
  return base;
}
