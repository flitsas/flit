"use client";

import { useRef } from "react";
import { Upload, X } from "lucide-react";
import { controlCls } from "@/components/ui/estilos";

/**
 * Selector de un archivo con el estilo de los botones de la app (B9-6): reemplaza el control nativo del navegador, que
 * sale en inglés («Choose File · No file chosen»). Muestra el nombre elegido y permite quitarlo.
 */
export function SelectorArchivo({
  archivo,
  onCambio,
  accept,
  texto = "Elegir archivo",
  disabled = false,
}: {
  archivo: File | null;
  onCambio: (archivo: File | null) => void;
  accept?: string;
  texto?: string;
  disabled?: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <div className="flex flex-wrap items-center gap-2 font-normal">
      <input
        ref={input}
        type="file"
        accept={accept}
        className="hidden"
        onChange={(e) => {
          onCambio(e.target.files?.[0] ?? null);
          // Permite volver a elegir el mismo archivo después de quitarlo.
          e.target.value = "";
        }}
      />
      <button type="button" className={controlCls(false)} disabled={disabled} onClick={() => input.current?.click()}>
        <Upload className="h-3.5 w-3.5" aria-hidden />
        {archivo ? "Cambiar archivo" : texto}
      </button>
      {archivo ? (
        <span className="inline-flex items-center gap-1 text-xs">
          {archivo.name}
          <button type="button" aria-label={`Quitar ${archivo.name}`} className="opacity-60 hover:opacity-100" disabled={disabled} onClick={() => onCambio(null)}>
            <X className="h-3.5 w-3.5" aria-hidden />
          </button>
        </span>
      ) : (
        <span className="text-xs opacity-60">Ningún archivo elegido</span>
      )}
    </div>
  );
}
