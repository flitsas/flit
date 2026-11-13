"use client";

import { useId, useRef, useState, type DragEvent, type ReactNode } from "react";
import { cn } from "./cn";
import { useRegionVivaLista } from "./useRegionViva";

/**
 * Zona de carga con arrastre y botón equivalente por teclado (HU #14562 · UI-2 · AC10). Valida extensión y tamaño
 * antes de llamar `onArchivo`; el rechazo se anuncia con `role="alert"` y queda asociado al botón por
 * `aria-describedby`. El arrastre es un atajo: todo se puede hacer con «Elegir archivo».
 * Observación 8 del code review (HU #14562): la región `role="alert"` existe vacía desde el montaje, con el patrón de
 * `useRegionViva`: un error que ya viene al montar se escribe pasado el retardo; el rechazo de una acción del usuario
 * llega después del montaje y se escribe enseguida en la misma región.
 */
export interface ZonaArrastreProps {
  etiqueta: string;
  /** Extensiones admitidas, con punto: [".xlsx", ".csv"]. */
  aceptar: string[];
  maxBytes: number;
  archivo?: File | null;
  onArchivo: (archivo: File | null) => void;
  ayuda?: ReactNode;
  /** Error que viene de fuera (por ejemplo, del servidor). */
  error?: string;
  className?: string;
}

function formatoBytes(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${Math.round((bytes / (1024 * 1024)) * 10) / 10} MB`;
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${bytes} bytes`;
}

export function ZonaArrastre({
  etiqueta,
  aceptar,
  maxBytes,
  archivo,
  onArchivo,
  ayuda,
  error,
  className,
}: ZonaArrastreProps) {
  const base = useId();
  const idInput = `${base}-input`;
  const idAyuda = `${base}-ayuda`;
  const idError = `${base}-error`;
  const inputRef = useRef<HTMLInputElement>(null);
  const [rechazo, setRechazo] = useState<string | null>(null);
  const [encima, setEncima] = useState(false);

  const extensiones = aceptar.map((e) => e.toLowerCase());
  const mensajeError = rechazo ?? error ?? null;
  const regionLista = useRegionVivaLista();
  const textoError = mensajeError && (regionLista || rechazo !== null) ? mensajeError : null;
  const describe = [ayuda ? idAyuda : null, mensajeError ? idError : null].filter(Boolean).join(" ") || undefined;

  const recibir = (f: File | undefined | null) => {
    if (!f) return;
    const nombre = f.name.toLowerCase();
    if (!extensiones.some((e) => nombre.endsWith(e))) {
      setRechazo(`Formato no admitido. Usa ${aceptar.join(", ")}.`);
      return;
    }
    if (f.size > maxBytes) {
      setRechazo(`El archivo supera el tamaño máximo de ${formatoBytes(maxBytes)}.`);
      return;
    }
    setRechazo(null);
    onArchivo(f);
  };

  const alSoltar = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setEncima(false);
    recibir(e.dataTransfer?.files?.[0]);
  };

  return (
    <div className={cn("flex flex-col gap-2", className)}>
      <div
        data-testid="zona-arrastre"
        onDragOver={(e) => {
          e.preventDefault();
          setEncima(true);
        }}
        onDragLeave={() => setEncima(false)}
        onDrop={alSoltar}
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-[var(--di-radius-modal)] border-2 border-dashed bg-[var(--di-surface)] px-6 py-8 text-center motion-safe:transition-colors motion-safe:duration-[var(--di-motion-fast)]",
          encima ? "border-[color:var(--di-action)] bg-[var(--di-accent-soft)]" : "border-[color:var(--kit-azul-texto)]",
          mensajeError ? "border-[color:var(--di-danger)]" : null,
        )}
      >
        <label htmlFor={idInput} className="sr-only">
          {etiqueta}
        </label>
        <input
          ref={inputRef}
          id={idInput}
          type="file"
          accept={aceptar.join(",")}
          className="sr-only"
          tabIndex={-1}
          onChange={(e) => {
            recibir(e.target.files?.[0]);
            e.target.value = "";
          }}
        />
        <p className="text-sm font-semibold text-[color:var(--kit-azul-texto)]">
          Arrastra el archivo aquí o elígelo
        </p>
        <button
          type="button"
          aria-describedby={describe}
          onClick={() => inputRef.current?.click()}
          className="min-h-9 rounded-full bg-[image:var(--di-gradient-action)] px-4 text-sm font-semibold text-[color:var(--di-on-action)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]"
        >
          Elegir archivo<span className="sr-only">: {etiqueta}</span>
        </button>
        {ayuda ? (
          <p id={idAyuda} className="text-xs text-[color:var(--di-text-tertiary)]">
            {ayuda}
          </p>
        ) : null}
      </div>
      {archivo ? (
        <div className="flex items-center gap-3 text-sm text-[color:var(--di-text-primary)]">
          <span className="font-semibold">{archivo.name}</span>
          <span className="text-[color:var(--di-text-tertiary)]">{formatoBytes(archivo.size)}</span>
          <button
            type="button"
            onClick={() => {
              setRechazo(null);
              onArchivo(null);
            }}
            className="min-h-9 rounded-full px-3 font-semibold text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
          >
            Quitar archivo
          </button>
        </div>
      ) : null}
      {/* Vacía queda sr-only: sigue en el árbol accesible y no suma espacio al layout. */}
      <p id={idError} role="alert" className={textoError ? "text-sm font-semibold text-[color:var(--di-danger)]" : "sr-only"}>
        {textoError}
      </p>
    </div>
  );
}
