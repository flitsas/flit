"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useEffect, useRef, useState } from "react";
import { AlertTriangle, CameraOff, Loader2, RefreshCw } from "lucide-react";
import { useLiveCamera, type CameraFacing } from "@/lib/captura-manual/useLiveCamera";

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
  onContinue: (blob: Blob) => void;
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
}: CameraViewerProps) {
  const [facing, setFacing] = useState<CameraFacing>(initialFacing);
  const [notice, setNotice] = useState<string | null>(null);
  const [captured, setCaptured] = useState<Blob | null>(initialBlob ?? null);
  const previewRef = useRef<HTMLImageElement | null>(null);
  const { videoRef, status, restart, capture } = useLiveCamera({ facing, enabled: !captured });

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

  async function take() {
    const blob = await capture();
    if (!blob) {
      setNotice("La cámara aún no está lista, inténtalo de nuevo");
      return;
    }
    setNotice(null);
    setCaptured(blob);
  }

  function repeat() {
    setCaptured(null);
    setNotice(null);
    restart();
  }

  if (captured) {
    return (
      <div className="flex flex-col gap-4">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img ref={previewRef} alt="Vista previa de la foto capturada" className="block h-auto w-full rounded-2xl bg-slate-900" />
        {/* Dos botones lado a lado (Repetir | Confirmar), mismo alto y sin desborde en 360 px. */}
        <div className="flex gap-3">
          <button type="button" onClick={repeat} className={`${btnBase} min-w-0 flex-1 border-2 border-flit-brand text-base font-semibold text-flit-brand`}>
            Repetir
          </button>
          <button type="button" onClick={() => onContinue(captured)} className={`${btnBase} min-w-0 flex-1 ${BRAND_BTN}`}>
            Confirmar
          </button>
        </div>
      </div>
    );
  }

  if (status === "denied" || status === "no-camera" || status === "unsupported" || status === "error") {
    return (
      <div role="alert" className="flex flex-col items-center gap-3 rounded-2xl border border-flit-gray bg-flit-bg p-5 text-center">
        {status === "denied" ? <AlertTriangle aria-hidden="true" className="size-8 text-flit-alert" /> : <CameraOff aria-hidden="true" className="size-8 text-flit-alert" />}
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
  return (
    <div className="flex flex-col gap-4">
      <div className={`relative overflow-hidden rounded-2xl bg-slate-900 ${isDoc ? "aspect-[4/3]" : "aspect-square"}`}>
        <video ref={videoRef} playsInline muted aria-label="Vista en vivo de la cámara" className="size-full object-cover" />
        {!ready ? (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-slate-900 p-6 text-center text-white">
            <Loader2 aria-hidden="true" className="size-8 motion-safe:animate-spin" />
            <p className="text-base">Abriendo cámara… toca «Permitir» cuando tu navegador lo pida.</p>
          </div>
        ) : (
          <>
            <Guide shape={shape} side={side} />
            {hint ? (
              <p
                aria-live="polite"
                data-testid="aviso-encuadre"
                className={
                  isDoc
                    ? "absolute inset-x-8 bottom-3 rounded-[28px] bg-slate-900/80 px-5 py-3 text-center text-base font-medium text-white"
                    : "absolute inset-x-4 bottom-3 rounded-full bg-amber-400/90 px-3 py-1 text-center text-sm font-medium text-slate-900"
                }
              >
                {hint}
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
          <RefreshCw aria-hidden="true" className="size-4" />
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

function Guide({ shape, side }: { shape: "oval" | "rect"; side: "anverso" | "reverso" }) {
  if (shape === "oval") {
    return (
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 flex items-center justify-center">
        <div className="relative h-[78%] w-[58%] rounded-[50%] border-[3px] border-white">
          <div className="absolute -inset-2 rounded-[50%] border-[3px] border-dashed border-amber-400" />
        </div>
      </div>
    );
  }
  // Marco rectangular redondeado, borde blanco semitransparente, en la parte alta del visor (la
  // píldora de aviso queda debajo, como en Kyverum). La silueta guía vive dentro del marco.
  return (
    <div className="pointer-events-none absolute inset-x-[5%] top-[7%] aspect-[2/1] rounded-2xl border-[3px] border-white/80">
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
      viewBox="0 0 200 100"
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
          <line x1="52" y1="30" x2="118" y2="30" />
          <line x1="52" y1="46" x2="118" y2="46" />
          <line x1="52" y1="62" x2="102" y2="62" />
          <rect x="132" y="22" width="30" height="48" rx="4" />
          <circle cx="147" cy="40" r="6" />
          <path d="M137 63 C137 52 157 52 157 63" />
          <text x="147" y="82" textAnchor="middle" fontSize="8" fill={stroke} stroke="none">
            tu foto
          </text>
        </>
      ) : (
        <>
          <rect x="18" y="18" width="30" height="40" rx="4" />
          <ellipse cx="33" cy="38" rx="8" ry="11" />
          <ellipse cx="33" cy="38" rx="4" ry="6" />
          {[62, 70, 76, 86, 92, 102, 110, 118, 126, 136, 142, 152, 160, 170, 178].map((x) => (
            <line key={x} x1={x} y1="68" x2={x} y2="88" />
          ))}
        </>
      )}
    </svg>
  );
}
