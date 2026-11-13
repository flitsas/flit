"use client";

import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from "react";
import { cn } from "./cn";

/**
 * Tabla con guardado por fila (HU #14562 · UI-2 · AC9). Cada fila lleva su propio borrador y su estado:
 * sin_cambios, editado (Descartar y Guardar), guardando y error (Reintentar, conserva lo digitado y recibe el foco).
 * Genérica: los valores son texto; para otro control se pasa `editor` en la columna.
 *
 * HU #14562 · M3 (auditoría de la oleada): al guardar o descartar, el foco pasa a la celda de estado de la fila, salvo
 * que el usuario ya esté en otro lado. Mientras guarda, los botones quedan `aria-disabled` y conservan el foco. Cada
 * cambio de estado se anuncia en una región viva de la tabla («A: Guardando», «A: Guardado»), que existe vacía desde
 * el montaje. `errorFila` pinta el motivo en la fila y lo asocia a «Reintentar».
 *
 * HU #14562 · M12 (diseno.md §2.3): `estadoFila` es un estado controlado opcional. Si devuelve un estado, o un estado
 * con rótulo y tono propios, manda sobre el interno, por ejemplo para «Revalidando», «Corregida» o «Inválido» en Carga.
 * Si devuelve null o undefined, o no se pasa, la fila usa su estado interno como siempre. `rotulosEstado` cambia los
 * rótulos por defecto. Además, el borrador se vuelve a sincronizar con la fila mientras no haya una edición pendiente.
 * En modo controlado, «Descartar» y «Guardar» solo aparecen si la fila tiene una edición propia (estado interno editado,
 * o guardando desde «Guardar»): un «guardando» que impone el padre, como «Revalidando», no ofrece esas acciones.
 */
export type EstadoFilaEditable = "sin_cambios" | "editado" | "guardando" | "error";

export type TonoEstadoFila = "info" | "danger" | "success" | "neutral";

export interface EstadoFilaControlado {
  estado: EstadoFilaEditable;
  /** Rótulo visible y anunciado; por defecto el de `rotulosEstado` o el del kit. */
  texto?: string;
  tono?: TonoEstadoFila;
}

export interface ColumnaEditable<F> {
  campo: keyof F & string;
  titulo: string;
  editable?: boolean;
  /**
   * Control propio: recibe el valor actual, el cambio, el nombre accesible que debe llevar y la fila (el borrador), por
   * si el control necesita otros campos.
   */
  editor?: (valor: F[keyof F & string], onCambio: (v: F[keyof F & string]) => void, etiqueta: string, fila: F) => ReactNode;
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
  /** Estado controlado de la fila. null o undefined: el estado interno. */
  estadoFila?: (fila: F) => EstadoFilaEditable | EstadoFilaControlado | null | undefined;
  /** Rótulos propios de los estados (por defecto «Sin cambios», «Editado», «Guardando», «Error de guardado»). */
  rotulosEstado?: Partial<Record<EstadoFilaEditable, string>>;
  /** Motivo del error de la fila: se pinta en la celda de estado y describe a «Reintentar». */
  errorFila?: (fila: F) => string | null | undefined;
  vacio?: ReactNode;
  className?: string;
}

const TEXTO_ESTADO: Record<EstadoFilaEditable, string> = {
  sin_cambios: "Sin cambios",
  editado: "Editado",
  guardando: "Guardando",
  error: "Error de guardado",
};

const TONO_ESTADO: Record<EstadoFilaEditable, TonoEstadoFila> = {
  sin_cambios: "neutral",
  editado: "info",
  guardando: "info",
  error: "danger",
};

const CLASE_TONO: Record<TonoEstadoFila, string> = {
  neutral: "text-[color:var(--di-text-tertiary)]",
  info: "text-[color:var(--di-info)]",
  success: "text-[color:var(--di-success)]",
  danger: "text-[color:var(--di-danger)]",
};

/** Anuncio de un guardado con éxito en el modo interno (la fila vuelve a «Sin cambios»). */
const TEXTO_GUARDADO = "Guardado";

const BOTON =
  "min-h-9 rounded-full px-3 text-sm font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60 aria-disabled:cursor-progress aria-disabled:opacity-60";

const BOTON_PRIMARIO = "bg-[image:var(--di-gradient-action)] text-[color:var(--di-on-action)]";

/** El foco está «libre» para la fila: en body (se perdió), en un nodo desmontado o dentro de la propia fila. */
function focoEnFila(fila: HTMLElement | null): boolean {
  const a = typeof document === "undefined" ? null : document.activeElement;
  return !a || a === document.body || !a.isConnected || (!!fila && fila.contains(a));
}

interface FilaEditableProps<F> {
  fila: F;
  id: string;
  columnas: ColumnaEditable<F>[];
  onGuardar: (fila: F) => Promise<void> | void;
  onDescartar?: (fila: F) => void;
  controlado: EstadoFilaEditable | EstadoFilaControlado | null | undefined;
  rotulos?: Partial<Record<EstadoFilaEditable, string>>;
  error: string | null | undefined;
  anunciar: (texto: string) => void;
}

function FilaEditable<F>({ fila, id, columnas, onGuardar, onDescartar, controlado, rotulos, error, anunciar }: FilaEditableProps<F>) {
  const [borrador, setBorrador] = useState<F>(fila);
  const [estado, setEstado] = useState<EstadoFilaEditable>("sin_cambios");
  const [filaPrevia, setFilaPrevia] = useState<F>(fila);
  const [desdeReintentar, setDesdeReintentar] = useState(false);
  const reintentarRef = useRef<HTMLButtonElement>(null);
  const estadoRef = useRef<HTMLSpanElement>(null);
  const filaRef = useRef<HTMLTableRowElement>(null);
  const montada = useRef(true);
  const enfocar = useRef<"estado" | "reintentar" | null>(null);
  const recienGuardada = useRef(false);
  const anuncioPrevio = useRef<string | null>(null);
  const errorId = useId();

  // M12: si el padre cambia la fila y no hay edición pendiente, el borrador toma el valor nuevo.
  if (fila !== filaPrevia) {
    setFilaPrevia(fila);
    if (estado === "sin_cambios") setBorrador(fila);
  }

  const control = controlado == null ? null : typeof controlado === "string" ? { estado: controlado } : controlado;
  const visible = control?.estado ?? estado;
  const texto = control?.texto ?? rotulos?.[visible] ?? TEXTO_ESTADO[visible];
  const tono = control?.tono ?? TONO_ESTADO[visible];
  const guardando = visible === "guardando" || estado === "guardando";

  useEffect(() => {
    montada.current = true;
    return () => {
      montada.current = false;
    };
  }, []);

  // M3: el foco sigue a la acción que lo tenía (Guardar o Descartar desaparecen al terminar).
  useEffect(() => {
    const destino = enfocar.current;
    if (!destino) return;
    enfocar.current = null;
    if (destino === "reintentar" && reintentarRef.current) reintentarRef.current.focus();
    else estadoRef.current?.focus();
  });

  // M3: cada cambio de estado visible (o de su motivo) se anuncia en la región viva de la tabla.
  const firma = `${visible}|${texto}|${error ?? ""}`;
  const controladaFila = control !== null;
  useEffect(() => {
    if (anuncioPrevio.current === null) {
      anuncioPrevio.current = firma;
      return;
    }
    if (anuncioPrevio.current === firma) return;
    anuncioPrevio.current = firma;
    const rotulo = recienGuardada.current && !controladaFila && visible === "sin_cambios" ? TEXTO_GUARDADO : texto;
    recienGuardada.current = false;
    anunciar(`${id}: ${rotulo}${error && visible === "error" ? `. ${error}` : ""}`);
  }, [firma, controladaFila, visible, texto, error, id, anunciar]);

  const cambiar = (campo: keyof F & string, valor: F[keyof F & string]) => {
    setBorrador((b) => ({ ...b, [campo]: valor }));
    setEstado("editado");
  };

  const guardar = useCallback(async (reintento = false) => {
    if (estado === "guardando") return;
    setDesdeReintentar(reintento);
    setEstado("guardando");
    try {
      await onGuardar(borrador);
      if (!montada.current) return;
      if (focoEnFila(filaRef.current)) enfocar.current = "estado";
      recienGuardada.current = true;
      setEstado("sin_cambios");
    } catch {
      if (!montada.current) return;
      if (focoEnFila(filaRef.current)) enfocar.current = "reintentar";
      setEstado("error");
    }
  }, [borrador, estado, onGuardar]);

  const descartar = () => {
    if (guardando) return;
    if (focoEnFila(filaRef.current)) enfocar.current = "estado";
    setBorrador(fila);
    setEstado("sin_cambios");
    onDescartar?.(fila);
  };

  return (
    <tr ref={filaRef} aria-label={id} className="border-b border-[color:var(--di-divider)]">
      {columnas.map((c, i) => {
        const etiquetaCampo = `${c.titulo} de ${id}`;
        const valor = borrador[c.campo];
        let contenido: ReactNode;
        if (c.editable) {
          contenido = c.editor ? (
            c.editor(valor, (v) => cambiar(c.campo, v), etiquetaCampo, borrador)
          ) : (
            <input
              type="text"
              aria-label={etiquetaCampo}
              value={valor == null ? "" : String(valor)}
              readOnly={guardando}
              aria-readonly={guardando ? "true" : undefined}
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
        <span
          ref={estadoRef}
          tabIndex={-1}
          data-tono={tono}
          className={cn(CLASE_TONO[tono], "focus:outline-none focus-visible:outline-2 focus-visible:outline-[var(--di-action)]")}
        >
          {texto}
        </span>
        {error ? (
          <span id={errorId} className="mt-1 block font-normal text-[color:var(--di-danger)]">
            {error}
          </span>
        ) : null}
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap items-center gap-2">
          {(controladaFila ? estado === "editado" || (estado === "guardando" && !desdeReintentar) : visible === "editado" || visible === "guardando") &&
          visible !== "error" ? (
            <>
              <button
                type="button"
                aria-label={`Descartar ${id}`}
                onClick={descartar}
                aria-disabled={guardando ? "true" : undefined}
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
                aria-disabled={guardando ? "true" : undefined}
                onClick={() => {
                  if (!guardando) void guardar();
                }}
                className={cn(BOTON, BOTON_PRIMARIO)}
              >
                Guardar
              </button>
            </>
          ) : null}
          {visible === "error" ? (
            <button
              ref={reintentarRef}
              type="button"
              aria-label={`Reintentar ${id}`}
              aria-describedby={error ? errorId : undefined}
              onClick={() => void guardar(true)}
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
  estadoFila,
  rotulosEstado,
  errorFila,
  vacio = "Sin filas",
  className,
}: TablaEditableProps<F>) {
  const [anuncio, setAnuncio] = useState("");
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
                controlado={estadoFila?.(f)}
                rotulos={rotulosEstado}
                error={errorFila?.(f)}
                anunciar={setAnuncio}
              />
            ))
          )}
        </tbody>
      </table>
      {/* M3: región viva de la tabla, montada vacía desde el principio para que el primer cambio también se anuncie. */}
      <p role="status" aria-label={`Estado de ${etiqueta}`} className="sr-only">
        {anuncio}
      </p>
    </div>
  );
}
