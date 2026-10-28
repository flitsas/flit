"use client";

import { FileText, Upload } from "lucide-react";
import { useId } from "react";
import { boton } from "@/components/landing/estilos";
import { ACCEPT_ARCHIVOS } from "@/lib/tramite/requisitos";

export type EstadoArchivo = "vacio" | "cargando" | "ok" | "error";

interface Props {
  title: string;
  hint: string;
  optional?: boolean;
  estado: EstadoArchivo;
  /** Nombre del archivo ya cargado (o el que se está subiendo). */
  nombre?: string | null;
  /** Mensaje bajo el cargador: «Adjunta el archivo requerido» o el motivo por el que no se subió. */
  error?: string | undefined;
  onFile: (archivo: File) => void;
  /** Quita el archivo cargado (solo se ofrece con uno cargado y mientras se pueda editar). */
  onQuitar?: (() => void) | undefined;
  disabled?: boolean;
}

/**
 * Cargador de archivos del diseño de referencia (`Drop` de tramite-traspaso.tsx): misma zona punteada de 176 px, ícono azul de
 * subida o verde con archivo, «Opcional» y borde rojo con error. A diferencia del repo, aquí el archivo SÍ se entrega a quien lo
 * sube; el cargador no guarda nada en el navegador. Operable con teclado (el input nativo es el que recibe el foco).
 */
export function Drop({ title, hint, optional, estado, nombre, error, onFile, onQuitar, disabled }: Props) {
  const idError = useId();
  const cargando = estado === "cargando";
  const conArchivo = estado === "ok" && Boolean(nombre);
  return (
    <div className="space-y-1">
      <label
        className={`relative flex h-44 cursor-pointer flex-col items-center justify-center gap-2 overflow-hidden rounded-[9px] border border-dashed p-5 text-center transition-colors focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/30 hover:border-primary ${error ? "border-destructive" : "border-border"} ${disabled || cargando ? "pointer-events-none opacity-70" : ""}`}
      >
        {optional && <span className="absolute right-3 top-2 text-xs font-light text-muted-foreground">Opcional</span>}
        {conArchivo ? <FileText aria-hidden className="h-7 w-7 text-success" /> : <Upload aria-hidden className="h-7 w-7 text-primary" />}
        <span className="font-medium">{title}</span>
        <span className="line-clamp-2 text-xs font-light text-muted-foreground">{conArchivo ? `${nombre} · Da clic para reemplazar el archivo` : hint}</span>
        {cargando && (
          <span role="status" className="text-xs text-primary">
            Subiendo {nombre ?? "el archivo"}...
          </span>
        )}
        {error && !cargando && (
          <span id={idError} role="alert" className="text-xs text-destructive">
            {error}
          </span>
        )}
        <input
          type="file"
          className="sr-only"
          accept={ACCEPT_ARCHIVOS}
          disabled={disabled || cargando}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? idError : undefined}
          onChange={(e) => {
            const archivo = e.target.files?.[0];
            e.target.value = ""; // permite volver a elegir el mismo archivo tras un error
            if (archivo) onFile(archivo);
          }}
        />
      </label>
      {conArchivo && onQuitar && !disabled && (
        <button type="button" className={boton("back", "h-9 px-4", "none")} onClick={onQuitar} disabled={cargando}>
          Quitar {title}
        </button>
      )}
    </div>
  );
}
