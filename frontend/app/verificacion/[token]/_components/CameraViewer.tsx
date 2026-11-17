"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useEffect, useRef, useState } from "react";
import { CameraOff } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import {
  MAX_INTENTOS,
  RESULTADO_VERIFICADA,
  mensajeRechazo,
  noVerificada,
  validarFoto,
  type FotoKind,
  type MotivoFoto,
  type MotorFotos,
  type ResultadoFoto,
} from "@/lib/captura-manual/validacionFotos";
import { useLiveCamera, type CameraFacing } from "@/lib/captura-manual/useLiveCamera";
import { CROP_PADDING, DOC_FRAME, GUIDANCE_HINT, frameRectInVideo } from "@/lib/captura-manual/documentDetection";
import { useDocumentAutoCapture } from "@/lib/captura-manual/useDocumentAutoCapture";

/** Validación de la foto en el dispositivo (Feature #14307). Sin esta propiedad el visor no valida. */
export interface ValidacionVisor {
  kind: FotoKind;
  motor: MotorFotos;
  /** Documento sin reverso (p. ej. pasaporte): se valida que sea un documento, no el lado. */
  sinReverso: boolean;
  /** Intentos fallidos ya acumulados en este lado (vive en el Flow para sobrevivir a «Atrás»). */
  fallosIniciales: number;
  /** Resultado de la foto ya confirmada (al volver con «Atrás»). */
  resultadoInicial?: ResultadoFoto;
  /** Se llama con el total de intentos fallidos cada vez que una foto no pasa. */
  onFallo: (fallos: number) => void;
}

type Revision =
  | { fase: "revisando" }
  | { fase: "lista"; resultado: ResultadoFoto; aviso?: string }
  | {
      fase: "rechazada";
      motivo: Exclude<MotivoFoto, "sin_soporte" | "sin_resultado">;
      mensaje: string;
      puedeContinuar: boolean;
      fallos: number;
    };

export interface CameraViewerProps {
  /** «oval» para rostro, «rect» para documento. */
  shape: "oval" | "rect";
  /** Lado del documento (solo con shape «rect»): decide la silueta guía dentro del marco. */
  side?: "anverso" | "reverso";
  /** Cámara inicial; el cliente puede alternarla. Rostro: user · documento: environment. */
  facing: CameraFacing;
  captureLabel: string;
  /** Pista de encuadre sobre el visor (sin detección facial: es solo texto fijo). */
  hint?: string;
  /** Se llama con el Blob JPEG cuando el cliente pulsa «Confirmar» sobre la vista previa. */
  onContinue: (blob: Blob, resultado?: ResultadoFoto) => void;
  validacion?: ValidacionVisor;
  /** Captura ya tomada (al volver con «Atrás»): abre directo la vista previa, sin reabrir la cámara. */
  initialBlob?: Blob;
}

const btnBase =
  "min-h-12 w-full rounded-xl px-4 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand disabled:cursor-not-allowed disabled:opacity-50";

/**
 * Visor de cámara en vivo reutilizable (HU #13293): rostro y documento (B6). Estados: abriendo,
 * lista, capturada, permiso denegado, sin cámara y navegador no compatible/sin HTTPS.
 * Sin selector de archivos en ningún punto.
 */
export function CameraViewer({
  shape,
  side = "anverso",
  facing: initialFacing,
  captureLabel,
  hint,
  onContinue,
  initialBlob,
  validacion,
}: CameraViewerProps) {
  const [facing, setFacing] = useState<CameraFacing>(initialFacing);
  const [notice, setNotice] = useState<string | null>(null);
  const [captured, setCaptured] = useState<Blob | null>(initialBlob ?? null);
  const previewRef = useRef<HTMLImageElement | null>(null);
  // Foto ya confirmada (al volver con «Atrás»): conserva su resultado; sin él, queda «sin_resultado».
  const [revision, setRevision] = useState<Revision | null>(
    validacion && initialBlob ? { fase: "lista", resultado: validacion.resultadoInicial ?? noVerificada("sin_resultado") } : null,
  );
  const fallos = useRef(validacion?.fallosIniciales ?? 0);
  const corrida = useRef(0); // descarta el resultado de una foto que ya se repitió
  const { videoRef, status, restart, capture, actualFacing } = useLiveCamera({ facing, enabled: !captured });

  // La URL blob vive exactamente lo que dura el efecto que la creó: se revoca en su cleanup (al
  // reemplazar la captura, al repetir o al desmontar), cuando la <img> ya no la usa. Nunca se
  // revoca durante el render ni se reutiliza una URL ya revocada (StrictMode vuelve a crearla).
  useEffect(() => {
    const img = previewRef.current;
    if (!captured || !img) return;
    const url = URL.createObjectURL(captured);
    img.src = url;
    return () => {
      img.removeAttribute("src");
      URL.revokeObjectURL(url);
    };
  }, [captured]);

  const isDocShape = shape === "rect";

  // Carga diferida de los motores: solo en estos pasos, mientras la persona encuadra.
  const motorValidacion = validacion?.motor;
  const kindValidacion = validacion?.kind;
  const sinReversoValidacion = validacion?.sinReverso ?? false;
  useEffect(() => {
    if (motorValidacion && kindValidacion) motorValidacion.precargar?.(kindValidacion, sinReversoValidacion);
  }, [motorValidacion, kindValidacion, sinReversoValidacion]);
  useEffect(
    () => () => {
      corrida.current++;
    },
    [],
  );

  async function revisar(foto: Blob) {
    if (!validacion) return;
    const id = ++corrida.current;
    setRevision({ fase: "revisando" });
    const v = await validarFoto({ kind: validacion.kind, foto, motor: validacion.motor, sinReverso: validacion.sinReverso });
    if (id !== corrida.current) return;
    if (v.tipo === "ok") {
      setRevision({ fase: "lista", resultado: RESULTADO_VERIFICADA });
    } else if (v.tipo === "sin_soporte") {
      setRevision({ fase: "lista", resultado: noVerificada(v.motivo), aviso: "No pudimos revisar esta foto. Puedes continuar." });
    } else {
      const previos = fallos.current;
      fallos.current = previos + 1;
      validacion.onFallo(fallos.current);
      // Con 3 fotos que no pasaron, la siguiente ya puede continuar aunque tampoco pase.
      setRevision({
        fase: "rechazada",
        motivo: v.motivo,
        mensaje: mensajeRechazo(validacion.kind, v.motivo),
        puedeContinuar: previos >= MAX_INTENTOS,
        fallos: fallos.current,
      });
    }
  }

  async function take() {
    // Documento: se guarda solo lo que está dentro del marco guía (más una holgura mínima), no toda la escena.
    const blob = await capture(isDocShape ? (w, h) => frameRectInVideo(w, h, CROP_PADDING) : undefined);
    if (!blob) {
      setNotice("La cámara aún no está lista, inténtalo de nuevo");
      return;
    }
    setNotice(null);
    setCaptured(blob);
    void revisar(blob);
  }

  // Documento: detección automática; al estar bien encuadrado, enfocado y quieto ~1 s, se captura solo.
  const auto = useDocumentAutoCapture({
    videoRef,
    enabled: isDocShape && status === "ready" && !captured,
    onStable: () => void take(),
  });

  function repeat() {
    corrida.current++;
    setRevision(null);
    setCaptured(null);
    setNotice(null);
    restart();
  }

  if (captured) {
    const revisando = revision?.fase === "revisando";
    const rechazada = revision?.fase === "rechazada" ? revision : null;
    const confirmar = () => {
      if (!validacion) return onContinue(captured);
      if (revision?.fase === "lista") return onContinue(captured, revision.resultado);
      if (rechazada?.puedeContinuar) return onContinue(captured, noVerificada(rechazada.motivo));
    };
    return (
      <div className="flex flex-col gap-4">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img ref={previewRef} alt="Vista previa de la foto capturada" className="block h-auto w-full rounded-2xl bg-slate-900" />
        {revisando ? (
          <p role="status" className="flex items-center justify-center gap-2 text-base text-muted-foreground">
            <Icono nombre="girador" className="size-5 motion-safe:animate-spin" />
            Revisando tu foto…
          </p>
        ) : null}
        {rechazada ? (
          <div role="alert" aria-live="assertive" data-testid="rechazo-foto" className="flex flex-col items-center gap-1 text-center">
            <p className="flex items-center gap-2 text-base font-semibold text-flit-alert">
              <Icono nombre="alerta" className="size-5 shrink-0" />
              {rechazada.mensaje}
            </p>
            {!rechazada.puedeContinuar && rechazada.fallos >= MAX_INTENTOS ? (
              <p className="text-sm text-muted-foreground">Si no pasa otra vez, podrás continuar.</p>
            ) : null}
          </div>
        ) : null}
        {revision?.fase === "lista" && revision.aviso ? (
          <p role="status" aria-live="polite" className="text-center text-sm text-muted-foreground">
            {revision.aviso}
          </p>
        ) : null}
        {rechazada ? (
          <div className="flex flex-col gap-3">
            <button type="button" onClick={repeat} className={`${btnBase} ${BRAND_BTN}`}>
              Repetir
            </button>
            {rechazada.puedeContinuar ? (
              <button type="button" onClick={confirmar} className={`${btnBase} border-2 border-flit-brand text-base font-semibold text-flit-brand`}>
                Continuar sin verificar
              </button>
            ) : null}
          </div>
        ) : (
          /* Dos botones lado a lado (Repetir | Confirmar), mismo alto y sin desborde en 360 px. */
          <div className="flex gap-3">
            <button
              type="button"
              onClick={repeat}
              disabled={revisando}
              className={`${btnBase} min-w-0 flex-1 border-2 border-flit-brand text-base font-semibold text-flit-brand`}
            >
              Repetir
            </button>
            <button type="button" onClick={confirmar} disabled={revisando} className={`${btnBase} min-w-0 flex-1 ${BRAND_BTN}`}>
              Confirmar
            </button>
          </div>
        )}
      </div>
    );
  }

  if (status === "denied" || status === "no-camera" || status === "unsupported" || status === "error") {
    return (
      <div role="alert" className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-secondary p-5 text-center">
        {status === "denied" ? <Icono nombre="alerta" className="size-8 text-flit-alert" /> : <CameraOff aria-hidden="true" className="size-8 text-flit-alert" />}
        <p className="text-base font-semibold text-flit-primary">{TITLES[status]}</p>
        <p className="text-base text-muted-foreground">{BODIES[status]}</p>
        {status === "denied" || status === "error" ? (
          <button type="button" onClick={restart} className={`${btnBase} ${BRAND_BTN}`}>
            Reintentar
          </button>
        ) : null}
      </div>
    );
  }

  const ready = status === "ready";
  const isDoc = shape === "rect";
  // Bug #13449: con una cámara frontal (la del celular o la webcam del computador, que no reporta `facingMode`)
  // la vista sigue el gesto como un espejo, en rostro y en documento. La trasera del celular no se invierte.
  // Solo la vista en vivo: la foto capturada no se invierte. El marco del documento es simétrico, así que el
  // recorte coincide con lo que se ve.
  const mirrored = actualFacing !== "environment";
  return (
    <div className="flex flex-col gap-4">
      <div className={`relative overflow-hidden rounded-2xl bg-slate-900 ${isDoc ? "aspect-[10/9]" : "aspect-square"}`}>
        <video
          ref={videoRef}
          playsInline
          muted
          aria-label="Vista en vivo de la cámara"
          data-mirrored={mirrored ? "true" : "false"}
          className={`size-full object-cover ${mirrored ? "-scale-x-100" : ""}`}
        />
        {!ready ? (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-slate-900 p-6 text-center text-white">
            <Icono nombre="girador" className="size-8 motion-safe:animate-spin" />
            <p className="text-base">Abriendo cámara… toca «Permitir» cuando tu navegador lo pida.</p>
          </div>
        ) : (
          <>
            <Guide shape={shape} side={side} listo={auto.state === "ready"} />
            {hint ? (
              <p
                aria-live="polite"
                data-testid="aviso-encuadre"
                className={
                  isDoc
                    ? "absolute inset-x-[10%] bottom-2 rounded-[28px] bg-slate-900/80 px-4 py-1.5 text-center text-[13px] font-medium leading-snug text-white"
                    : "absolute inset-x-4 bottom-3 rounded-full bg-amber-400/90 px-3 py-1 text-center text-sm font-medium text-slate-900"
                }
              >
                {isDoc && auto.state ? GUIDANCE_HINT[auto.state] : hint}
              </p>
            ) : null}
          </>
        )}
      </div>
      {notice ? (
        <p role="alert" className="text-center text-base font-medium text-flit-alert">
          {notice}
        </p>
      ) : null}
      <button type="button" disabled={!ready} onClick={() => void take()} className={`${btnBase} ${BRAND_BTN}`}>
        {captureLabel}
      </button>
      {ready ? (
        <button
          type="button"
          onClick={() => setFacing((f) => (f === "user" ? "environment" : "user"))}
          className="inline-flex min-h-11 items-center justify-center gap-2 self-center rounded-xl px-4 text-sm font-semibold text-flit-brand-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
        >
          <Icono nombre="reintento" className="size-4" />
          Cambiar cámara
        </button>
      ) : null}
    </div>
  );
}

const TITLES = {
  denied: "No tenemos permiso para usar tu cámara",
  "no-camera": "No encontramos una cámara",
  unsupported: "No se puede continuar en este dispositivo",
  error: "No pudimos abrir la cámara",
} as const;

const BODIES = {
  denied:
    "Habilítala en tu navegador: toca el candado junto a la dirección de la página, entra a Permisos y activa Cámara. Luego toca «Reintentar».",
  "no-camera": "No se puede continuar en este dispositivo porque no detectamos ninguna cámara. Abre este enlace en un celular o computador con cámara.",
  unsupported:
    "Tu navegador no es compatible o la página no se abrió con conexión segura (HTTPS). Abre el enlace en un navegador actualizado.",
  error: "Cierra otras aplicaciones que usen la cámara e inténtalo de nuevo.",
} as const;

function Guide({ shape, side, listo }: { shape: "oval" | "rect"; side: "anverso" | "reverso"; listo: boolean }) {
  if (shape === "oval") {
    return (
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 flex items-center justify-center">
        <div className="relative h-[78%] w-[58%] rounded-[50%] border-[3px] border-white">
          <div className="absolute -inset-2 rounded-[50%] border-[3px] border-dashed border-amber-400" />
        </div>
      </div>
    );
  }
  // Marco del documento: lo de afuera se oscurece (solo se guarda lo de adentro) y el borde pasa a verde
  // cuando el documento está bien encuadrado y quieto, justo antes de la captura automática.
  return (
    <div
      data-testid="marco-documento"
      data-listo={listo ? "true" : "false"}
      className={`pointer-events-none absolute rounded-2xl border-[3px] shadow-[0_0_0_9999px_rgba(15,23,42,0.55)] transition-colors ${
        listo ? "border-emerald-400" : "border-white/85"
      }`}
      style={{
        left: `${DOC_FRAME.x * 100}%`,
        top: `${DOC_FRAME.y * 100}%`,
        width: `${DOC_FRAME.w * 100}%`,
        height: `${DOC_FRAME.h * 100}%`,
      }}
    >
      <DocumentSilhouette side={side} />
    </div>
  );
}

/**
 * Silueta guía del documento. Anverso (réplica de Kyverum): tres líneas a la izquierda y, a la derecha,
 * un recuadro con persona (cabeza y hombros) y la leyenda «tu foto». Reverso: SUPUESTO, no se vio en las
 * capturas de Kyverum; coherente con el anverso: recuadro de huella a la izquierda y barras verticales
 * a lo ancho en la parte inferior del marco.
 */
function DocumentSilhouette({ side }: { side: "anverso" | "reverso" }) {
  const stroke = "rgba(255,255,255,0.55)";
  return (
    <svg
      viewBox="0 0 160 100"
      role="img"
      aria-label={side === "anverso" ? "Guía del anverso: líneas de datos y recuadro de tu foto" : "Guía del reverso: recuadro de huella y código de barras"}
      data-testid="guia-documento"
      data-side={side}
      className="size-full"
      fill="none"
      stroke={stroke}
      strokeWidth="1.6"
      strokeLinecap="round"
    >
      {side === "anverso" ? (
        <>
          <line x1="12" y1="28" x2="88" y2="28" />
          <line x1="12" y1="44" x2="88" y2="44" />
          <line x1="12" y1="60" x2="70" y2="60" />
          <rect x="102" y="16" width="44" height="56" rx="4" />
          <circle cx="124" cy="36" r="8" />
          <path d="M111 66 C111 52 137 52 137 66" />
          <text x="124" y="87" textAnchor="middle" fontSize="8" fill={stroke} stroke="none">
            tu foto
          </text>
        </>
      ) : (
        <>
          <rect x="12" y="14" width="34" height="46" rx="4" />
          <ellipse cx="29" cy="37" rx="9" ry="13" />
          <ellipse cx="29" cy="37" rx="4.5" ry="7" />
          {[14, 20, 24, 32, 36, 44, 50, 56, 62, 70, 74, 82, 88, 96, 100, 108, 114, 122, 128, 136, 146].map((x) => (
            <line key={x} x1={x} y1="72" x2={x} y2="90" />
          ))}
        </>
      )}
    </svg>
  );
}
