"use client";

import { useEffect, useRef, useState } from "react";
import { AlertTriangle, Camera, CameraOff, Loader2, RefreshCw } from "lucide-react";
import { useLiveCamera, type CameraFacing } from "@/lib/captura-manual/useLiveCamera";

export interface CameraViewerProps {
  /** «oval» para rostro, «rect» para documento. */
  shape: "oval" | "rect";
  /** Cámara inicial; el cliente puede alternarla. Rostro: user · documento: environment. */
  facing: CameraFacing;
  captureLabel: string;
  /** Pista de encuadre sobre el visor (sin detección facial: es solo texto fijo). */
  hint?: string;
  /** Se llama con el Blob JPEG cuando el cliente pulsa «Continuar» sobre la vista previa. */
  onContinue: (blob: Blob) => void;
  /** Captura ya tomada (al volver con «Atrás»): abre directo la vista previa, sin reabrir la cámara. */
  initialBlob?: Blob;
}

const btnBase =
  "min-h-11 w-full rounded-xl px-4 text-base font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand disabled:cursor-not-allowed disabled:opacity-50";

/**
 * Visor de cámara en vivo reutilizable (HU #13293): rostro y documento (B6). Estados: abriendo,
 * lista, capturada, permiso denegado, sin cámara y navegador no compatible/sin HTTPS.
 * Sin selector de archivos en ningún punto.
 */
export function CameraViewer({ shape, facing: initialFacing, captureLabel, hint, onContinue, initialBlob }: CameraViewerProps) {
  const [facing, setFacing] = useState<CameraFacing>(initialFacing);
  const urlRef = useRef<string | null>(null);
  const [captured, setCaptured] = useState<{ blob: Blob; url: string } | null>(() => {
    if (!initialBlob) return null;
    return { blob: initialBlob, url: URL.createObjectURL(initialBlob) };
  });
  const { videoRef, status, restart, capture } = useLiveCamera({ facing, enabled: !captured });

  // urlRef refleja la URL vigente para liberarla al desmontar.
  useEffect(() => {
    urlRef.current = captured?.url ?? null;
  }, [captured]);
  useEffect(
    () => () => {
      if (urlRef.current) URL.revokeObjectURL(urlRef.current);
    },
    [],
  );

  async function take() {
    const blob = await capture();
    if (!blob) return;
    setCaptured({ blob, url: URL.createObjectURL(blob) });
  }

  function repeat() {
    if (captured) URL.revokeObjectURL(captured.url);
    setCaptured(null);
    restart();
  }

  if (captured) {
    return (
      <div className="flex flex-col gap-3">
        <div className="relative aspect-square overflow-hidden rounded-2xl bg-slate-900">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={captured.url} alt="Vista previa de la foto capturada" className="size-full object-contain" />
        </div>
        <div className="flex gap-3">
          <button type="button" onClick={repeat} className={`${btnBase} border border-flit-brand-ink text-flit-brand-ink`}>
            Repetir
          </button>
          <button type="button" onClick={() => onContinue(captured.blob)} className={`${btnBase} bg-flit-brand text-flit-primary`}>
            Continuar
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
          <button type="button" onClick={restart} className={`${btnBase} bg-flit-brand text-flit-primary`}>
            Reintentar
          </button>
        ) : null}
      </div>
    );
  }

  const ready = status === "ready";
  return (
    <div className="flex flex-col gap-3">
      <div className="relative aspect-square overflow-hidden rounded-2xl bg-slate-900">
        <video ref={videoRef} playsInline muted aria-label="Vista en vivo de la cámara" className="size-full object-cover" />
        {!ready ? (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-slate-900 p-6 text-center text-white">
            <Loader2 aria-hidden="true" className="size-8 motion-safe:animate-spin" />
            <p className="text-base">Abriendo cámara… toca «Permitir» cuando tu navegador lo pida</p>
          </div>
        ) : (
          <>
            <Guide shape={shape} />
            {hint ? (
              <p aria-live="polite" className="absolute inset-x-4 bottom-3 rounded-full bg-amber-400/90 px-3 py-1 text-center text-sm font-medium text-slate-900">
                {hint}
              </p>
            ) : null}
          </>
        )}
      </div>
      <button type="button" disabled={!ready} onClick={() => void take()} className={`${btnBase} bg-flit-brand text-flit-primary`}>
        <Camera aria-hidden="true" className="mr-2 inline size-5" />
        {captureLabel}
      </button>
      {ready ? (
        <button
          type="button"
          onClick={() => setFacing((f) => (f === "user" ? "environment" : "user"))}
          className={`${btnBase} border border-flit-gray text-flit-primary`}
        >
          <RefreshCw aria-hidden="true" className="mr-2 inline size-4" />
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

function Guide({ shape }: { shape: "oval" | "rect" }) {
  if (shape === "oval") {
    return (
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 flex items-center justify-center">
        <div className="relative h-[78%] w-[58%] rounded-[50%] border-[3px] border-white">
          <div className="absolute -inset-2 rounded-[50%] border-[3px] border-dashed border-amber-400" />
        </div>
      </div>
    );
  }
  return (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0 flex items-center justify-center">
      <div className="aspect-[1.586/1] w-[86%] rounded-xl border-[3px] border-white" />
    </div>
  );
}
