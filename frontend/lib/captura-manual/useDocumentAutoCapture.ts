"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import {
  ANALYSIS_MARGIN,
  analyzeDocumentFrame,
  decideGuidance,
  frameRectInVideo,
  motionBetween,
  toGray,
  type GuidanceState,
} from "./documentDetection";

const SAMPLE_WIDTH = 192;
const INTERVAL_MS = 180;
/** Cuadros buenos seguidos antes de capturar (≈ 1 s con el intervalo de arriba). */
export const READY_FRAMES = 6;

export interface DocumentGuidance {
  /** `null` hasta que haya un primer análisis: mientras tanto se muestra el aviso fijo. */
  state: GuidanceState | null;
  /** 0–1: avance hacia la captura automática. */
  progress: number;
}

/**
 * Detección automática del documento (sin librerías). Mientras `enabled`, analiza el marco guía ~5 veces por
 * segundo; cuando el documento está en el marco, enfocado y quieto durante ~1 s, llama a `onStable` una vez.
 * Si el navegador no permite leer los píxeles, no hace nada y queda el botón de captura manual.
 */
export function useDocumentAutoCapture({
  videoRef,
  enabled,
  onStable,
}: {
  videoRef: RefObject<HTMLVideoElement | null>;
  enabled: boolean;
  onStable: () => void;
}): DocumentGuidance {
  const [guidance, setGuidance] = useState<DocumentGuidance>({ state: null, progress: 0 });
  const onStableRef = useRef(onStable);
  useEffect(() => {
    onStableRef.current = onStable;
  }, [onStable]);

  useEffect(() => {
    if (!enabled) return;
    let streak = 0;
    let fired = false;
    let prev: Uint8ClampedArray | null = null;
    let canvas: HTMLCanvasElement | null = null;

    const tick = () => {
      const video = videoRef.current;
      if (fired || !video || !video.videoWidth || !video.videoHeight) return;
      try {
        // Región analizada = marco + margen, para poder ver el borde del documento contra el fondo.
        const region = frameRectInVideo(video.videoWidth, video.videoHeight, ANALYSIS_MARGIN);
        const frame = frameRectInVideo(video.videoWidth, video.videoHeight);
        const w = SAMPLE_WIDTH;
        const h = Math.max(8, Math.round((w * region.h) / region.w));
        canvas ??= document.createElement("canvas");
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        if (!ctx) return;
        ctx.drawImage(video, region.x, region.y, region.w, region.h, 0, 0, w, h);
        const gray = toGray(ctx.getImageData(0, 0, w, h).data, w, h);
        const k = w / region.w;
        const inner = { x: (frame.x - region.x) * k, y: (frame.y - region.y) * k, w: frame.w * k, h: frame.h * k };
        const analysis = analyzeDocumentFrame(gray, w, h, inner);
        const motion = prev ? motionBetween(prev, gray) : Infinity;
        prev = gray;
        const state = decideGuidance(analysis, motion);
        streak = state === "ready" ? streak + 1 : 0;
        const progress = Math.min(1, streak / READY_FRAMES);
        setGuidance((g) => (g.state === state && g.progress === progress ? g : { state, progress }));
        if (streak >= READY_FRAMES) {
          fired = true;
          onStableRef.current();
        }
      } catch {
        // Sin lectura de píxeles (navegador/políticas): la captura manual sigue disponible.
      }
    };

    const id = window.setInterval(tick, INTERVAL_MS);
    return () => {
      window.clearInterval(id);
      setGuidance({ state: null, progress: 0 });
    };
  }, [enabled, videoRef]);

  return guidance;
}
