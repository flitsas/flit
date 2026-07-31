"use client";

import { useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import { Eraser, PenLine, Upload } from "lucide-react";

// Captura del artefacto de firma (ADR D9 · v1): dibujo en pantalla (canvas nativo,
// sin librería externa) o carga de un PNG. Ambos modos reducen la firma a un data URL
// PNG base64 que se entrega vía `onChange` (el backend tolera el prefijo data:image/png).
// WCAG 2.1 AA: modos como radiogroup, canvas etiquetado, alternativa por carga de archivo.

export type CaptureMode = "draw" | "upload";

export interface SignatureCaptureProps {
  /** data URL PNG capturado, o null si aún no hay artefacto. */
  value: string | null;
  onChange: (dataUrl: string | null) => void;
  disabled?: boolean;
  /** Mensaje de error (p. ej. artefacto_invalido) a mostrar bajo el capturador. */
  error?: string | null;
}

export function SignatureCapture({ value, onChange, disabled = false, error }: SignatureCaptureProps) {
  const [mode, setMode] = useState<CaptureMode>("draw");
  const [uploadError, setUploadError] = useState<string | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const drawing = useRef(false);
  const dirty = useRef(false);

  const ctxOf = (canvas: HTMLCanvasElement) => {
    const ctx = canvas.getContext("2d");
    if (ctx) {
      ctx.strokeStyle = "#162744";
      ctx.lineWidth = 2.2;
      ctx.lineCap = "round";
      ctx.lineJoin = "round";
    }
    return ctx;
  };

  const pointFromEvent = (canvas: HTMLCanvasElement, e: ReactPointerEvent<HTMLCanvasElement>) => {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    return { x: (e.clientX - rect.left) * scaleX, y: (e.clientY - rect.top) * scaleY };
  };

  const startDraw = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    if (disabled) return;
    const canvas = canvasRef.current;
    const ctx = canvas && ctxOf(canvas);
    if (!canvas || !ctx) return;
    canvas.setPointerCapture(e.pointerId);
    drawing.current = true;
    const p = pointFromEvent(canvas, e);
    ctx.beginPath();
    ctx.moveTo(p.x, p.y);
  };

  const moveDraw = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas && ctxOf(canvas);
    if (!canvas || !ctx) return;
    const p = pointFromEvent(canvas, e);
    ctx.lineTo(p.x, p.y);
    ctx.stroke();
    dirty.current = true;
  };

  const endDraw = () => {
    if (!drawing.current) return;
    drawing.current = false;
    const canvas = canvasRef.current;
    if (canvas && dirty.current) {
      onChange(canvas.toDataURL("image/png"));
    }
  };

  const clearCanvas = () => {
    const canvas = canvasRef.current;
    if (canvas) {
      canvas.getContext("2d")?.clearRect(0, 0, canvas.width, canvas.height);
    }
    dirty.current = false;
    onChange(null);
  };

  const handleFile = (file: File | undefined) => {
    setUploadError(null);
    if (!file) return;
    const isPng = file.type === "image/png" || file.name.toLowerCase().endsWith(".png");
    if (!isPng) {
      setUploadError("El archivo debe ser una imagen PNG.");
      onChange(null);
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : null;
      if (result && result.startsWith("data:image/png")) {
        onChange(result);
      } else {
        setUploadError("No se pudo leer el PNG. Intenta con otro archivo.");
        onChange(null);
      }
    };
    reader.onerror = () => setUploadError("No se pudo leer el archivo.");
    reader.readAsDataURL(file);
  };

  const switchMode = (next: CaptureMode) => {
    if (next === mode) return;
    setMode(next);
    setUploadError(null);
    dirty.current = false;
    onChange(null);
    if (next === "draw") {
      // Al volver a dibujo se limpia el lienzo (aún montado en el DOM).
      const canvas = canvasRef.current;
      canvas?.getContext("2d")?.clearRect(0, 0, canvas.width, canvas.height);
    }
  };

  return (
    <div className="space-y-3">
      <div role="radiogroup" aria-label="Método de captura de la firma" className="flex gap-2">
        <ModeButton active={mode === "draw"} disabled={disabled} onClick={() => switchMode("draw")}>
          <PenLine className="h-3.5 w-3.5" /> Dibujar en pantalla
        </ModeButton>
        <ModeButton active={mode === "upload"} disabled={disabled} onClick={() => switchMode("upload")}>
          <Upload className="h-3.5 w-3.5" /> Cargar PNG
        </ModeButton>
      </div>

      {/* El canvas se mantiene montado para conservar el trazo; se oculta en modo carga. */}
      <div className={mode === "draw" ? "space-y-2" : "hidden"}>
        <div className="overflow-hidden rounded-xl border-2 border-dashed" style={{ borderColor: "#DFE5ED" }}>
          <canvas
            ref={canvasRef}
            width={760}
            height={200}
            aria-label="Lienzo para dibujar la firma del apoderado"
            role="img"
            className={`h-[200px] w-full touch-none bg-white ${disabled ? "cursor-not-allowed" : "cursor-crosshair"}`}
            onPointerDown={startDraw}
            onPointerMove={moveDraw}
            onPointerUp={endDraw}
            onPointerLeave={endDraw}
            onPointerCancel={endDraw}
          />
        </div>
        <div className="flex items-center justify-between">
          <button
            type="button"
            onClick={clearCanvas}
            disabled={disabled}
            className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
          >
            <Eraser className="h-3.5 w-3.5" /> Limpiar
          </button>
          {value && mode === "draw" && (
            <span className="text-[11px] font-semibold" style={{ color: "#0a8f8b" }}>
              ✓ Firma capturada
            </span>
          )}
        </div>
        <p className="text-[11px] opacity-60">
          Dibuja la firma con el mouse o el dedo. Al soltar se guarda automáticamente como PNG.
        </p>
      </div>

      <div className={mode === "upload" ? "space-y-2" : "hidden"}>
        <label
          htmlFor="sv-upload"
          className="flex cursor-pointer flex-col items-center gap-2 rounded-xl border-2 border-dashed py-8 text-xs font-semibold"
          style={{ borderColor: "#557EFF", color: "#557EFF" }}
        >
          <Upload className="h-6 w-6" />
          Selecciona un archivo PNG con la firma
          <input
            id="sv-upload"
            type="file"
            accept="image/png"
            disabled={disabled}
            className="sr-only"
            onChange={(e) => handleFile(e.target.files?.[0])}
          />
        </label>
        {value && mode === "upload" && (
          <div className="rounded-xl border p-2">
            <p className="mb-1 text-[10px] font-semibold uppercase opacity-60">Vista previa</p>
            {/* Vista previa del PNG cargado; no se sube ningún binario al listar. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={value} alt="Vista previa de la firma cargada" className="max-h-32 w-auto" />
          </div>
        )}
      </div>

      {(uploadError || error) && (
        <p role="alert" className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
          {uploadError ?? error}
        </p>
      )}
    </div>
  );
}

function ModeButton({
  active,
  disabled,
  onClick,
  children,
}: {
  active: boolean;
  disabled?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      disabled={disabled}
      onClick={onClick}
      className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold transition disabled:opacity-50"
      style={active ? { background: "#557EFF", borderColor: "#557EFF", color: "#fff" } : { borderColor: "#DFE5ED" }}
    >
      {children}
    </button>
  );
}
