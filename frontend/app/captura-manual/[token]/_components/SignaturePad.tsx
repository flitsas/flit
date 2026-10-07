"use client";

import { useEffect, useImperativeHandle, useRef, type PointerEvent as ReactPointerEvent, type Ref } from "react";

const INK = "#000000"; // trazo negro grueso, como en el flujo real de Kyverum
const W = 760;
const H = 340;

/** Permite que los botones «Borrar» / «Firmar y autorizar» vivan fuera del lienzo (fila de dos botones). */
export interface SignaturePadHandle {
  clear: () => void;
}

/**
 * Lienzo de firma SOLO de trazo (HU #13295): pointer events para dedo y mouse, `touch-action: none`
 * para que trazar no haga scroll. Exporta un PNG (Blob) con fondo blanco. A propósito NO existe modo
 * «cargar archivo» (regla «solo en vivo» de la Épica #13202); por eso no se reutiliza SignatureCapture,
 * que incluye ese modo, pero la lógica del lienzo es la misma (coordenadas escaladas, trazo redondeado).
 * Limitación aceptada: una firma manuscrita no tiene alternativa de teclado.
 */
export function SignaturePad({
  initialBlob,
  onChange,
  disabled = false,
  ref,
}: {
  ref?: Ref<SignaturePadHandle>;
  /** Firma ya trazada (al volver al paso): se repinta en el lienzo. */
  initialBlob?: Blob;
  onChange: (png: Blob | null) => void;
  disabled?: boolean;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const drawing = useRef(false);
  const dirty = useRef(!!initialBlob);

  const paintBackground = () => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
  };

  useEffect(() => {
    paintBackground();
    if (!initialBlob) return;
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx || typeof Image === "undefined") return;
    const url = URL.createObjectURL(initialBlob);
    const img = new Image();
    img.onload = () => {
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
      URL.revokeObjectURL(url);
    };
    img.onerror = () => URL.revokeObjectURL(url);
    img.src = url;
    // Solo al montar: el Blob inicial no cambia durante la vida del lienzo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const ctxOf = (canvas: HTMLCanvasElement) => {
    const ctx = canvas.getContext("2d");
    if (ctx) {
      ctx.strokeStyle = INK;
      ctx.lineWidth = 7;
      ctx.lineCap = "round";
      ctx.lineJoin = "round";
    }
    return ctx;
  };

  const point = (canvas: HTMLCanvasElement, e: ReactPointerEvent<HTMLCanvasElement>) => {
    const rect = canvas.getBoundingClientRect();
    return {
      x: (e.clientX - rect.left) * (canvas.width / (rect.width || canvas.width)),
      y: (e.clientY - rect.top) * (canvas.height / (rect.height || canvas.height)),
    };
  };

  const start = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    if (disabled || (e.pointerType === "mouse" && e.button !== 0)) return;
    const canvas = canvasRef.current;
    const ctx = canvas && ctxOf(canvas);
    if (!canvas || !ctx) return;
    canvas.setPointerCapture?.(e.pointerId);
    drawing.current = true;
    const p = point(canvas, e);
    ctx.beginPath();
    ctx.moveTo(p.x, p.y);
  };

  const move = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas && ctxOf(canvas);
    if (!canvas || !ctx) return;
    const p = point(canvas, e);
    ctx.lineTo(p.x, p.y);
    ctx.stroke();
    dirty.current = true;
  };

  const end = () => {
    if (!drawing.current) return;
    drawing.current = false;
    const canvas = canvasRef.current;
    if (!canvas || !dirty.current) return;
    canvas.toBlob((blob) => onChange(blob), "image/png");
  };

  const clear = () => {
    paintBackground();
    dirty.current = false;
    onChange(null);
  };

  useImperativeHandle(ref, () => ({ clear }));

  return (
    <div className="overflow-hidden rounded-2xl border-2 border-dashed border-slate-300 bg-white">
      <canvas
        ref={canvasRef}
        width={W}
        height={H}
        role="img"
        aria-label="Lienzo para trazar tu firma con el dedo o el mouse"
        className={`aspect-[760/340] h-auto w-full touch-none bg-white ${disabled ? "cursor-not-allowed" : "cursor-crosshair"}`}
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={end}
        onPointerLeave={end}
        onPointerCancel={end}
      />
    </div>
  );
}
