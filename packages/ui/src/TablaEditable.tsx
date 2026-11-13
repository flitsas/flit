"use client";

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "./cn";

/**
 * Tabla con guardado por fila (HU #14562 · UI-2 · AC9). Cada fila lleva su propio borrador y su estado:
 * sin_cambios, editado (Descartar y Guardar), guardando y error (Reintentar, conserva lo digitado y recibe el foco).
 * Genérica: los valores son texto; para otro control se pasa `editor` en la columna.
 */
export type EstadoFilaEditable = "sin_cambios" | "editado" | "guardando" | "error";

export interface ColumnaEditable<F> {
  campo: keyof F & string;
  titulo: string;
  editable?: boolean;
  /** Control propio: recibe el valor actual, el cambio y el nombre accesible que debe llevar. */
  editor?: (valor: F[keyof F & string], onCambio: (v: F[keyof F & string]) => void, etiqueta: string) => ReactNode;
  /** Presentación de una celda no editable. */
  mostrar?: (fila: F) => ReactNode;
}

export interface TablaEditableProps<F> {
  etiqueta: string;
  filas: F[];
  columnas: ColumnaEditable<F>[];
  clave: (fila: F) => string;
  onGuardar: (fila: F) => Promise<void> | void;
  onDescartar?: (fila: F) => void;
  vacio?: ReactNode;
  className?: string;
}

const TEXTO_ESTADO: Record<EstadoFilaEditable, string> = {
  sin_cambios: "Sin cambios",
  editado: "Editado",
  guardando: "Guardando",
  error: "Error de guardado",
};

const CLASE_ESTADO: Record<EstadoFilaEditable, string> = {
  sin_cambios: "text-[color:var(--di-text-tertiary)]",
  editado: "text-[color:var(--di-info)]",
  guardando: "text-[color:var(--di-info)]",
  error: "text-[color:var(--di-danger)]",
};

const BOTON =
  "min-h-9 rounded-full px-3 text-sm font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60";

const BOTON_PRIMARIO = "bg-[image:var(--di-gradient-action)] text-[color:var(--di-on-action)]";

interface FilaEditableProps<F> {
  fila: F;
  id: string;
  columnas: ColumnaEditable<F>[];
  onGuardar: (fila: F) => Promise<void> | void;
  onDescartar?: (fila: F) => void;
}

function FilaEditable<F>({ fila, id, columnas, onGuardar, onDescartar }: FilaEditableProps<F>) {
  const [borrador, setBorrador] = useState<F>(fila);
  const [estado, setEstado] = useState<EstadoFilaEditable>("sin_cambios");
  const reintentarRef = useRef<HTMLButtonElement>(null);
  const montada = useRef(true);

  useEffect(() => {
    montada.current = true;
    return () => {
      montada.current = false;
    };
  }, []);

  useEffect(() => {
    if (estado === "error") reintentarRef.current?.focus();
  }, [estado]);

  const cambiar = (campo: keyof F & string, valor: F[keyof F & string]) => {
    setBorrador((b) => ({ ...b, [campo]: valor }));
    setEstado("editado");
  };

  const guardar = useCallback(async () => {
    setEstado("guardando");
    try {
      await onGuardar(borrador);
      if (montada.current) setEstado("sin_cambios");
    } catch {
      if (montada.current) setEstado("error");
    }
  }, [borrador, onGuardar]);

  const descartar = () => {
    setBorrador(fila);
    setEstado("sin_cambios");
    onDescartar?.(fila);
  };

  const guardando = estado === "guardando";

  return (
    <tr aria-label={id} className="border-b border-[color:var(--di-divider)]">
      {columnas.map((c, i) => {
        const etiquetaCampo = `${c.titulo} de ${id}`;
        const valor = borrador[c.campo];
        let contenido: ReactNode;
        if (c.editable) {
          contenido = c.editor ? (
            c.editor(valor, (v) => cambiar(c.campo, v), etiquetaCampo)
          ) : (
            <input
              type="text"
              aria-label={etiquetaCampo}
              value={valor == null ? "" : String(valor)}
              disabled={guardando}
              onChange={(e) => cambiar(c.campo, e.target.value as F[keyof F & string])}
              className="min-h-9 w-full rounded-[9px] border border-[color:var(--di-border-control)] bg-[var(--di-surface)] px-2 text-sm text-[color:var(--di-text-primary)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
            />
          );
        } else {
          contenido = c.mostrar ? c.mostrar(borrador) : valor == null ? "" : String(valor);
        }
        return i === 0 && !c.editable ? (
          <th
            key={c.campo}
            scope="row"
            className="px-3 py-2 text-left text-sm font-semibold text-[color:var(--di-text-primary)]"
          >
            {contenido}
          </th>
        ) : (
          <td key={c.campo} className="px-3 py-2 text-sm text-[color:var(--di-text-primary)]">
            {contenido}
          </td>
        );
      })}
      <td className="px-3 py-2 text-xs font-semibold">
        <span className={CLASE_ESTADO[estado]}>{TEXTO_ESTADO[estado]}</span>
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap items-center gap-2">
          {estado === "editado" || guardando ? (
            <>
              <button
                type="button"
                aria-label={`Descartar ${id}`}
                onClick={descartar}
                disabled={guardando}
                className={cn(
                  BOTON,
                  "text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)]",
                )}
              >
                Descartar
              </button>
              <button
                type="button"
                aria-label={`Guardar ${id}`}
                aria-busy={guardando ? "true" : undefined}
                onClick={guardar}
                disabled={guardando}
                className={cn(BOTON, BOTON_PRIMARIO)}
              >
                Guardar
              </button>
            </>
          ) : null}
          {estado === "error" ? (
            <button
              ref={reintentarRef}
              type="button"
              aria-label={`Reintentar ${id}`}
              onClick={guardar}
              className={cn(BOTON, BOTON_PRIMARIO)}
            >
              Reintentar
            </button>
          ) : null}
        </div>
      </td>
    </tr>
  );
}

export function TablaEditable<F>({
  etiqueta,
  filas,
  columnas,
  clave,
  onGuardar,
  onDescartar,
  vacio = "Sin filas",
  className,
}: TablaEditableProps<F>) {
  const cabecera = "px-3 py-2 text-left text-xs font-semibold text-[color:var(--di-text-secondary)]";
  return (
    <div
      className={cn(
        "overflow-x-auto rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface)]",
        className,
      )}
    >
      <table aria-label={etiqueta} className="w-full border-collapse">
        <thead className="bg-[var(--di-surface-sunken)]">
          <tr>
            {columnas.map((c) => (
              <th key={c.campo} scope="col" className={cabecera}>
                {c.titulo}
              </th>
            ))}
            <th scope="col" className={cabecera}>
              Estado
            </th>
            <th scope="col" className={cabecera}>
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {filas.length === 0 ? (
            <tr>
              <td
                colSpan={columnas.length + 2}
                className="px-3 py-6 text-center text-sm text-[color:var(--di-text-tertiary)]"
              >
                {vacio}
              </td>
            </tr>
          ) : (
            filas.map((f) => (
              <FilaEditable
                key={clave(f)}
                id={clave(f)}
                fila={f}
                columnas={columnas}
                onGuardar={onGuardar}
                onDescartar={onDescartar}
              />
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
