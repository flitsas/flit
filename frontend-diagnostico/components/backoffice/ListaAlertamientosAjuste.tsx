"use client";

import { useId, useState } from "react";
import { FUENTES } from "@/lib/configuracion";
import { rotuloAlerta } from "@/lib/dictamen/api";
import { EFECTOS_DICTAMEN } from "@/lib/rotulos";
import { CODIGO_AJUSTE_FLIT, TEXTO_ALERTAMIENTO_MAX, validarAlertamiento, type FilaAlertamiento } from "@/lib/backoffice/ajusteDictamen";

export interface ListaAlertamientosAjusteProps {
  alertamientos: FilaAlertamiento[];
  onCambiar: (lista: FilaAlertamiento[]) => void;
  /** Errores del API por campo (`alertamientos[i].texto`, 422 de P3). */
  errores?: Readonly<Record<string, string>>;
  deshabilitado?: boolean;
}

const CONTROL =
  "min-h-9 w-full rounded-[9px] border border-[color:var(--di-border-control)] bg-[var(--di-surface)] px-2 text-sm text-[color:var(--di-text-primary)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)] aria-[invalid=true]:border-[color:var(--di-danger)]";
const ETIQUETA = "text-xs font-semibold text-[color:var(--di-text-secondary)]";
const ERROR = "text-xs text-[color:var(--di-danger)]";
const BOTON_TEXTO =
  "min-h-9 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] hover:bg-[color:var(--di-accent-soft)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60";

const numeroDe = (clave: string) => Number.parseInt(clave.replace(/\D+/g, ""), 10) || 0;

/** Título de la fila: rótulo del catálogo, o «Alertamiento libre» para AJUSTE_FLIT. Nunca el código crudo. */
const titulo = (f: FilaAlertamiento) => (f.codigo === CODIGO_AJUSTE_FLIT ? "Alertamiento libre" : rotuloAlerta({ ...f, etiqueta: null }));

/** Índice del alertamiento en un campo de error del API (`alertamientos[1].texto` → 1). */
const indiceDeCampo = (campo: string) => Number.parseInt(/^alertamientos\[(\d+)\]/.exec(campo)?.[1] ?? "", 10);

/**
 * Alertamientos del ajuste FLIT (HU #14562 · AC45, B7): una tarjeta por alertamiento con texto, efecto y fuente, «Agregar
 * alertamiento» (código AJUSTE_FLIT) y «Eliminar». Auditoría M8: se edita en línea, sin guardado por fila; cada cambio llega
 * de inmediato al borrador del panel, así que «Aplicar» (el PUT con la lista completa) nunca sale con un texto viejo, y un
 * alertamiento inválido deja «Aplicar» deshabilitado. Tarjetas y no tabla: en el panel de 640 px una tabla de 5 columnas
 * desplazaba en horizontal (AC56).
 *
 * Errores (AC56): el de validación local (obligatorio, ≤ 500 y, en los libres, sin secuencias de 6 o más dígitos, P3)
 * aparece cuando el usuario edita o deja el texto, nunca al abrir; el del API (422 por `alertamientos[i].texto`) se muestra
 * en cuanto llega. Los dos van junto a su campo con `aria-invalid` y `aria-describedby`.
 */
export function ListaAlertamientosAjuste({ alertamientos, onCambiar, errores = {}, deshabilitado = false }: ListaAlertamientosAjusteProps) {
  const id = useId();
  const [tocados, setTocados] = useState<ReadonlySet<string>>(() => new Set());

  const tocar = (clave: string) => setTocados((t) => (t.has(clave) ? t : new Set(t).add(clave)));

  const cambiar = (clave: string, cambio: Partial<Pick<FilaAlertamiento, "texto" | "efecto" | "fuente">>) =>
    onCambiar(alertamientos.map((a) => (a.clave === clave ? { ...a, ...cambio, ajuste: a.ajuste === "AGREGADO" ? "AGREGADO" : "EDITADO" } : a)));

  const agregar = () => {
    const siguiente = alertamientos.reduce((max, a) => Math.max(max, numeroDe(a.clave)), 0) + 1;
    onCambiar([...alertamientos, { clave: `Alertamiento ${siguiente}`, codigo: CODIGO_AJUSTE_FLIT, texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" }]);
  };

  const eliminar = (clave: string) => onCambiar(alertamientos.filter((a) => a.clave !== clave));

  const erroresApi = new Map<string, string>();
  for (const [campo, mensaje] of Object.entries(errores)) {
    const fila = alertamientos[indiceDeCampo(campo)];
    if (fila) erroresApi.set(fila.clave, mensaje);
  }

  const botonAgregar = (
    <div>
      <button type="button" onClick={agregar} disabled={deshabilitado} className={BOTON_TEXTO}>
        Agregar alertamiento
      </button>
    </div>
  );

  if (alertamientos.length === 0) {
    return (
      <div className="flex flex-col gap-2">
        <p className="text-sm text-[color:var(--di-text-tertiary)]">Sin alertamientos. Un dictamen Bloqueado necesita al menos uno.</p>
        {botonAgregar}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <ul aria-label="Alertamientos del ajuste" className="flex flex-col gap-3">
        {alertamientos.map((f, i) => {
          const base = `${id}-${i}`;
          const error = erroresApi.get(f.clave) ?? (tocados.has(f.clave) ? validarAlertamiento(f) : null);
          return (
            <li key={f.clave} className="rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-3">
              <fieldset className="flex flex-col gap-3">
                <legend className="mb-1 text-sm font-semibold text-[color:var(--di-heading)]">{`${f.clave} · ${titulo(f)}`}</legend>
                <div className="flex flex-col gap-1">
                  <label htmlFor={`${base}-texto`} className={ETIQUETA}>
                    Texto
                  </label>
                  <input
                    id={`${base}-texto`}
                    aria-label={`Texto de ${f.clave}`}
                    type="text"
                    value={f.texto}
                    maxLength={TEXTO_ALERTAMIENTO_MAX}
                    disabled={deshabilitado}
                    aria-invalid={error ? true : undefined}
                    aria-describedby={error ? `${base}-error` : undefined}
                    onChange={(e) => {
                      tocar(f.clave);
                      cambiar(f.clave, { texto: e.target.value });
                    }}
                    onBlur={() => tocar(f.clave)}
                    className={CONTROL}
                  />
                  {error && (
                    <p id={`${base}-error`} className={ERROR}>
                      {error}
                    </p>
                  )}
                </div>
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="flex flex-col gap-1">
                    <label htmlFor={`${base}-efecto`} className={ETIQUETA}>
                      Efecto
                    </label>
                    <select
                      id={`${base}-efecto`}
                      aria-label={`Efecto de ${f.clave}`}
                      value={f.efecto}
                      disabled={deshabilitado}
                      onChange={(e) => cambiar(f.clave, { efecto: e.target.value as FilaAlertamiento["efecto"] })}
                      className={CONTROL}
                    >
                      {Object.entries(EFECTOS_DICTAMEN).map(([codigo, nombre]) => (
                        <option key={codigo} value={codigo}>
                          {nombre}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="flex flex-col gap-1">
                    <label htmlFor={`${base}-fuente`} className={ETIQUETA}>
                      Fuente
                    </label>
                    <select
                      id={`${base}-fuente`}
                      aria-label={`Fuente de ${f.clave}`}
                      value={f.fuente}
                      disabled={deshabilitado}
                      onChange={(e) => cambiar(f.clave, { fuente: e.target.value })}
                      className={CONTROL}
                    >
                      {FUENTES.map((fuente) => (
                        <option key={fuente.codigo} value={fuente.codigo}>
                          {fuente.nombre}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div>
                  <button type="button" aria-label={`Eliminar ${f.clave}`} disabled={deshabilitado} onClick={() => eliminar(f.clave)} className={BOTON_TEXTO}>
                    Eliminar
                  </button>
                </div>
              </fieldset>
            </li>
          );
        })}
      </ul>
      {botonAgregar}
    </div>
  );
}
