// Validación de las fotos de la captura manual EN EL DISPOSITIVO (Feature #14307, HUs #14308/#14309).
// Ninguna imagen sale a la red por esta validación. La decisión vive aquí, pura y sin librerías; los motores
// (rostro, código, textura) se inyectan: el real se carga bajo demanda en `motorLocal.ts`.
// El resultado es INFORMATIVO: no es un control de seguridad ni detecta prueba de vida ni coincidencia de rostro.

export type FotoKind = "rostro" | "anverso" | "reverso";

export type MotivoFoto = "sin_rostro" | "varios_rostros" | "sin_documento" | "lado_incorrecto" | "sin_soporte" | "sin_resultado";

/** Forma exacta del contrato (campo `validacionFotos` del submit). */
export interface ResultadoFoto {
  estado: "verificada" | "no_verificada";
  motivo: MotivoFoto | null;
}

export type ValidacionFotos = Record<FotoKind, ResultadoFoto>;

/** Intentos fallidos de un lado a partir de los cuales una foto que no pasa deja continuar. */
export const MAX_INTENTOS = 3;
/** Tiempo máximo del análisis de una foto; si se supera, el dispositivo se trata como sin soporte. */
export const TIMEOUT_ANALISIS_MS = 12_000;

/**
 * Tipos de documento SIN reverso útil (el lado no se valida). Código `PAS` = Pasaporte, el que usan los
 * selectores del proyecto. Cédulas (CC, CE), TI y PPT sí tienen reverso.
 */
const TIPOS_SIN_REVERSO: readonly string[] = ["PAS"];

export function documentoSinReverso(tipo: string | null | undefined): boolean {
  return !!tipo && TIPOS_SIN_REVERSO.includes(tipo.trim().toUpperCase());
}

/** Caja de un rostro en píxeles de la imagen. */
export interface CajaRostro {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface RostrosDetectados {
  imgW: number;
  imgH: number;
  cajas: CajaRostro[];
}

/** Motores de análisis. Cada método lanza `SinSoporteError` si el dispositivo no puede ejecutarlo. */
export interface MotorFotos {
  rostros(foto: Blob): Promise<RostrosDetectados>;
  hayCodigo(foto: Blob): Promise<boolean>;
  /** ¿La imagen tiene el detalle de un documento (y no una pared lisa)? */
  hayDocumento(foto: Blob): Promise<boolean>;
  /** Empieza a cargar solo lo que ese paso necesita (la persona aún está encuadrando). */
  precargar?(kind: FotoKind, sinReverso: boolean): void;
}

export class SinSoporteError extends Error {
  constructor(message = "Este dispositivo no puede validar la foto") {
    super(message);
    this.name = "SinSoporteError";
  }
}

export type VeredictoFoto =
  | { tipo: "ok" }
  | { tipo: "rechazada"; motivo: Extract<MotivoFoto, "sin_rostro" | "varios_rostros" | "sin_documento" | "lado_incorrecto"> }
  | { tipo: "sin_soporte"; motivo: Extract<MotivoFoto, "sin_soporte" | "sin_resultado"> };

/** Mensajes al usuario (textos cerrados por el PO). */
export function mensajeRechazo(kind: FotoKind, motivo: MotivoFoto): string {
  switch (motivo) {
    case "sin_rostro":
      return "No vemos tu rostro";
    case "varios_rostros":
      return "Debe aparecer solo una persona";
    case "lado_incorrecto":
      return kind === "reverso" ? "Muestra el lado con el código de barras" : "Muestra el lado de tu documento con tu foto";
    default:
      return "Encuadra el documento completo dentro del marco";
  }
}

// --- Geometría de la selfie -------------------------------------------------------------------------------
// El visor de rostro es un cuadrado con `object-cover` y el óvalo guía mide 58 % de ancho × 78 % de alto,
// centrado. La foto guardada es el fotograma completo: el cuadrado visible es el centro de lado min(w, h).
const OVALO = { semiAncho: 0.29, semiAlto: 0.39 } as const;
/** Holgura sobre el óvalo (el anillo punteado exterior también es parte de la guía). */
const HOLGURA_OVALO = 1.2;
/** Un rostro ajeno cuenta como «otra persona» si mide al menos esta fracción del área del principal. */
const FRACCION_OTRA_PERSONA = 0.25;
/** En un documento, el rostro del retrato es pequeño; uno que ocupa más de esto es una selfie, no un documento. */
const MAX_FRACCION_ROSTRO_EN_DOCUMENTO = 0.25;

function dentroDelOvalo(c: CajaRostro, imgW: number, imgH: number): boolean {
  const lado = Math.min(imgW, imgH);
  const dx = (c.x + c.w / 2 - imgW / 2) / (OVALO.semiAncho * lado);
  const dy = (c.y + c.h / 2 - imgH / 2) / (OVALO.semiAlto * lado);
  return Math.hypot(dx, dy) <= HOLGURA_OVALO;
}

const area = (c: CajaRostro) => c.w * c.h;

/** Selfie: exactamente un rostro dentro del óvalo y nadie más relevante en el cuadro. */
export function juzgarSelfie(r: RostrosDetectados): VeredictoFoto {
  const dentro = r.cajas.filter((c) => dentroDelOvalo(c, r.imgW, r.imgH));
  if (dentro.length === 0) return { tipo: "rechazada", motivo: "sin_rostro" };
  if (dentro.length > 1) return { tipo: "rechazada", motivo: "varios_rostros" };
  const principal = dentro[0];
  const otras = r.cajas.filter((c) => c !== principal && area(c) >= area(principal) * FRACCION_OTRA_PERSONA);
  return otras.length ? { tipo: "rechazada", motivo: "varios_rostros" } : { tipo: "ok" };
}

/** Anverso: un documento con un rostro pequeño (el retrato). */
export function juzgarAnverso(r: RostrosDetectados, hayDocumento: boolean): VeredictoFoto {
  const total = r.imgW * r.imgH;
  if (r.cajas.some((c) => area(c) / total > MAX_FRACCION_ROSTRO_EN_DOCUMENTO)) {
    return { tipo: "rechazada", motivo: "sin_documento" }; // un rostro grande es una selfie, no un documento
  }
  if (r.cajas.length > 0) return { tipo: "ok" };
  return { tipo: "rechazada", motivo: hayDocumento ? "lado_incorrecto" : "sin_documento" };
}

/** Reverso: un código de barras o QR (que ya implica un documento) o, si no, un documento del otro lado. */
export function juzgarReverso(hayCodigo: boolean, hayDocumento: boolean): VeredictoFoto {
  if (hayCodigo) return { tipo: "ok" };
  return { tipo: "rechazada", motivo: hayDocumento ? "lado_incorrecto" : "sin_documento" };
}

function conTiempo<T>(p: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const id = setTimeout(() => reject(new SinSoporteError("El análisis tardó demasiado")), ms);
    p.then(
      (v) => {
        clearTimeout(id);
        resolve(v);
      },
      (e: unknown) => {
        clearTimeout(id);
        reject(e);
      },
    );
  });
}

/**
 * Valida una foto en el dispositivo. Si el motor no está disponible, no carga, falla o tarda más de
 * `timeoutMs`, devuelve `sin_soporte` (la persona puede continuar y la foto queda «no verificada»).
 * `sinReverso` (p. ej. pasaporte): solo se comprueba que sea un documento, no el lado.
 */
export async function validarFoto(args: {
  kind: FotoKind;
  foto: Blob;
  motor: MotorFotos;
  sinReverso?: boolean;
  timeoutMs?: number;
}): Promise<VeredictoFoto> {
  const { kind, foto, motor, sinReverso = false, timeoutMs = TIMEOUT_ANALISIS_MS } = args;
  const analizar = async (): Promise<VeredictoFoto> => {
    if (kind === "rostro") return juzgarSelfie(await motor.rostros(foto));
    if (sinReverso) {
      return (await motor.hayDocumento(foto)) ? { tipo: "ok" } : { tipo: "rechazada", motivo: "sin_documento" };
    }
    if (kind === "anverso") {
      const rostros = await motor.rostros(foto);
      // Sin rostro hace falta saber si al menos es un documento (mensaje distinto).
      const documento = rostros.cajas.length ? true : await motor.hayDocumento(foto);
      return juzgarAnverso(rostros, documento);
    }
    if (await motor.hayCodigo(foto)) return { tipo: "ok" };
    return juzgarReverso(false, await motor.hayDocumento(foto));
  };
  try {
    return await conTiempo(analizar(), timeoutMs);
  } catch (e) {
    return { tipo: "sin_soporte", motivo: e instanceof SinSoporteError ? "sin_soporte" : "sin_resultado" };
  }
}

export const RESULTADO_VERIFICADA: ResultadoFoto = { estado: "verificada", motivo: null };

/** Resultado de una foto que continuó sin pasar. */
export function noVerificada(motivo: MotivoFoto): ResultadoFoto {
  return { estado: "no_verificada", motivo };
}

/** Arma el JSON del envío; una foto sin resultado queda «no verificada / sin_resultado». */
export function armarValidacion(parcial: Partial<Record<FotoKind, ResultadoFoto>>): ValidacionFotos {
  const o = (k: FotoKind) => parcial[k] ?? noVerificada("sin_resultado");
  return { rostro: o("rostro"), anverso: o("anverso"), reverso: o("reverso") };
}
