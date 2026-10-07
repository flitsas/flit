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

// Rostro, Anverso y Reverso replican el flujo real de Kyverum (capturas del PO, 2026-10-06): títulos,
// textos y aviso de encuadre. En el flujo manual no hay verificación automática, por eso «Confirmar»
// siempre está habilitado tras capturar.
const DOC_HINT = "Encuadra el documento completo, sin reflejos ni dedos sobre los datos, y toca Capturar";

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
    title: "Documento — anverso",
    instruction: "Muestra el lado de tu documento con tu FOTO. Encuádralo dentro del marco, siguiendo la guía.",
    shape: "rect",
    facing: "environment",
    captureLabel: "Capturar documento",
    hint: DOC_HINT,
  },
  reverso: {
    title: "Documento — reverso",
    instruction: "Ahora el reverso: el lado del código de barras (o QR). Encuádralo dentro del marco.",
    shape: "rect",
    facing: "environment",
    captureLabel: "Capturar documento",
    hint: DOC_HINT,
  },
} as const satisfies Record<string, CapturaConfig>;

export type CapturaKind = keyof typeof CAPTURA_CONFIG;

/** Pasos 2–4 (HU #13294): visor en vivo + vista previa con «Repetir»/«Confirmar». */
export function PasoCaptura({
  kind,
  blob,
  onCaptured,
}: {
  kind: CapturaKind;
  blob?: Blob;
  onCaptured: (blob: Blob) => void;
}) {
  const cfg: CapturaConfig = CAPTURA_CONFIG[kind];
  return (
    <section aria-labelledby={`paso-${kind}-titulo`} className="mt-6 flex flex-col gap-5">
      <div>
        <h1 id={`paso-${kind}-titulo`} className="text-2xl font-bold text-flit-primary">
          {cfg.title}
        </h1>
        <p className="mt-2 text-base text-muted-foreground">{cfg.instruction}</p>
      </div>
      <CameraViewer
        shape={cfg.shape}
        side={kind === "reverso" ? "reverso" : "anverso"}
        facing={cfg.facing}
        captureLabel={cfg.captureLabel}
        hint={cfg.hint}
        initialBlob={blob}
        onContinue={onCaptured}
      />
    </section>
  );
}
