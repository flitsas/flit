"use client";

import { CameraViewer } from "./CameraViewer";
import type { CameraFacing } from "@/lib/captura-manual/useLiveCamera";

export interface CapturaConfig {
  title: string;
  instruction: string;
  shape: "oval" | "rect";
  facing: CameraFacing;
  captureLabel: string;
  hint: string;
}

// Rostro: confirmado en vivo en Kyverum. Anverso/Reverso: textos y guía rectangular son SUPUESTO
// (no se vieron en Kyverum); pendientes de ajuste cuando el PO envíe capturas.
export const CAPTURA_CONFIG = {
  rostro: {
    title: "Verificación facial",
    instruction: "Mira de frente, con buena luz, y retira gafas, gorra y otros accesorios.",
    shape: "oval",
    facing: "user",
    captureLabel: "Capturar rostro",
    hint: "Ubica tu rostro dentro del óvalo",
  },
  anverso: {
    title: "Anverso del documento",
    instruction: "Colócalo dentro del marco, sin reflejos, completo y nítido.",
    shape: "rect",
    facing: "environment",
    captureLabel: "Capturar anverso",
    hint: "Ubica el documento dentro del marco",
  },
  reverso: {
    title: "Reverso del documento",
    instruction: "Voltéalo y colócalo dentro del marco, sin reflejos, completo y nítido.",
    shape: "rect",
    facing: "environment",
    captureLabel: "Capturar reverso",
    hint: "Ubica el documento dentro del marco",
  },
} as const satisfies Record<string, CapturaConfig>;

export type CapturaKind = keyof typeof CAPTURA_CONFIG;

/** Pasos 2–4 (HU #13294): visor en vivo + vista previa con «Repetir»/«Continuar» y «Atrás». */
export function PasoCaptura({
  kind,
  blob,
  onCaptured,
  onBack,
}: {
  kind: CapturaKind;
  blob?: Blob;
  onCaptured: (blob: Blob) => void;
  onBack: () => void;
}) {
  const cfg: CapturaConfig = CAPTURA_CONFIG[kind];
  return (
    <section aria-labelledby={`paso-${kind}-titulo`} className="mt-6 flex flex-col gap-4">
      <div>
        <h1 id={`paso-${kind}-titulo`} className="text-xl font-bold text-flit-primary">
          {cfg.title}
        </h1>
        <p className="mt-1 text-base text-muted-foreground">{cfg.instruction}</p>
      </div>
      <CameraViewer
        shape={cfg.shape}
        facing={cfg.facing}
        captureLabel={cfg.captureLabel}
        hint={cfg.hint}
        initialBlob={blob}
        onContinue={onCaptured}
      />
      <button
        type="button"
        onClick={onBack}
        className="min-h-11 w-full rounded-xl border border-flit-brand-ink px-4 text-base font-semibold text-flit-brand-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
      >
        Atrás
      </button>
    </section>
  );
}
