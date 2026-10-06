"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export type CameraStatus = "opening" | "ready" | "denied" | "no-camera" | "unsupported" | "error";
export type CameraFacing = "user" | "environment";

/** Resolución máxima del JPEG capturado (lado mayor) para no superar el límite del backend. */
export const MAX_CAPTURE_SIDE = 1280;
export const JPEG_QUALITY = 0.85;

class UnsupportedError extends Error {}

function classify(error: unknown): CameraStatus {
  if (error instanceof UnsupportedError) return "unsupported";
  const name = (error as { name?: string } | null)?.name;
  if (name === "NotAllowedError" || name === "SecurityError" || name === "PermissionDeniedError") return "denied";
  if (name === "NotFoundError" || name === "DevicesNotFoundError" || name === "OverconstrainedError") return "no-camera";
  return "error";
}

/**
 * Cámara en vivo con getUserMedia (HU #13293). Abre el stream mientras `enabled`, lo asigna al
 * <video> de `videoRef`, y detiene TODOS los tracks al deshabilitar, reintentar o desmontar.
 * Solo captura fotogramas del video: no hay selector de archivos ni detección facial.
 */
export function useLiveCamera({ facing, enabled = true }: { facing: CameraFacing; enabled?: boolean }) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [attempt, setAttempt] = useState(0);
  const key = `${facing}:${attempt}`;
  const [result, setResult] = useState<{ key: string; status: CameraStatus }>({ key: "", status: "opening" });
  // «opening» se deriva (la clave cambió y aún no hay resultado): evita setState síncrono en el efecto.
  const status: CameraStatus = result.key === key ? result.status : "opening";

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    let stream: MediaStream | null = null;
    let detach: () => void = () => undefined;
    const video = videoRef.current;
    const supported =
      typeof window !== "undefined" &&
      window.isSecureContext !== false &&
      typeof navigator !== "undefined" &&
      !!navigator.mediaDevices?.getUserMedia;
    const request = supported
      ? navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: facing }, width: { ideal: 1920 }, height: { ideal: 1080 } },
          audio: false,
        })
      : Promise.reject(new UnsupportedError());

    request
      .then((s) => {
        if (cancelled) {
          s.getTracks().forEach((t) => t.stop());
          return;
        }
        stream = s;
        if (!video) {
          setResult({ key, status: "ready" });
          return;
        }
        // «ready» solo cuando el <video> ya tiene dimensiones reales: antes, «Capturar» no podría tomar nada.
        const markReady = () => {
          if (cancelled || !video.videoWidth) return;
          detach();
          setResult({ key, status: "ready" });
        };
        detach = () => {
          video.removeEventListener("loadedmetadata", markReady);
          video.removeEventListener("loadeddata", markReady);
        };
        video.addEventListener("loadedmetadata", markReady);
        video.addEventListener("loadeddata", markReady);
        video.srcObject = s;
        // jsdom no implementa play(); un rechazo por autoplay no impide mostrar el video.
        void Promise.resolve(video.play?.()).catch(() => undefined);
        markReady();
      })
      .catch((e: unknown) => {
        if (!cancelled) setResult({ key, status: classify(e) });
      });

    return () => {
      cancelled = true;
      detach();
      stream?.getTracks().forEach((t) => t.stop());
      if (video) video.srcObject = null;
    };
  }, [enabled, facing, key]);

  /** Reabre la cámara (reintentar tras denegar, o «Repetir» tras una captura). */
  const restart = useCallback(() => setAttempt((n) => n + 1), []);

  /** Captura el fotograma actual como Blob JPEG (lado mayor ≤ MAX_CAPTURE_SIDE). */
  const capture = useCallback(async (): Promise<Blob | null> => {
    const video = videoRef.current;
    if (!video || !video.videoWidth || !video.videoHeight) return null;
    const scale = Math.min(1, MAX_CAPTURE_SIDE / Math.max(video.videoWidth, video.videoHeight));
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(video.videoWidth * scale);
    canvas.height = Math.round(video.videoHeight * scale);
    const ctx = canvas.getContext("2d");
    if (!ctx) return null;
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    return new Promise((resolve) => canvas.toBlob((b) => resolve(b), "image/jpeg", JPEG_QUALITY));
  }, []);

  return { videoRef, status, restart, capture };
}
