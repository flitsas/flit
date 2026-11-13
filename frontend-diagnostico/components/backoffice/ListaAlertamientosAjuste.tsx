"use client";

import { useState } from "react";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";
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
  "min-h-9 w-full rounded-[9px] border border-[color:var(--di-border-control)] bg-[var(--di-surface)] px-2 text-sm text-[color:var(--di-text-primary)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]";
const BOTON_TEXTO =
  "min-h-9 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] hover:bg-[color:var(--di-accent-soft)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] disabled:opacity-60";

const numeroDe = (clave: string) => Number.parseInt(clave.replace(/\D+/g, ""), 10) || 0;

/** Título de la fila: rótulo del catálogo, o «Alertamiento libre» para AJUSTE_FLIT. Nunca el código crudo. */
const titulo = (f: FilaAlertamiento) => (f.codigo === CODIGO_AJUSTE_FLIT ? "Alertamiento libre" : rotuloAlerta({ ...f, etiqueta: null }));

/**
 * Alertamientos del ajuste FLIT (HU #14562 · AC45, B7): `TablaEditable` con guardado por fila (texto, efecto y fuente),
 * «Agregar alertamiento» (código AJUSTE_FLIT) y «Eliminar» por fila. Guardar una fila solo actualiza el borrador del
 * panel; el PUT lo hace «Aplicar» con la lista completa. Antes de guardar valida el texto (obligatorio, ≤ 500 y, en los
 * libres, sin secuencias de 6 o más dígitos, P3): si falla, la fila queda en «Error de guardado» con su mensaje.
 */
export function ListaAlertamientosAjuste({ alertamientos, onCambiar, errores = {}, deshabilitado = false }: ListaAlertamientosAjusteProps) {
  const [erroresFila, setErroresFila] = useState<Record<string, string>>({});

  const indice = (clave: string) => alertamientos.findIndex((a) => a.clave === clave);

  const guardar = (fila: FilaAlertamiento) => {
    const error = validarAlertamiento(fila);
    if (error) {
      setErroresFila((m) => ({ ...m, [fila.clave]: error }));
      throw new Error(error);
    }
    setErroresFila((m) => Object.fromEntries(Object.entries(m).filter(([clave]) => clave !== fila.clave)));
    onCambiar(alertamientos.map((a) => (a.clave === fila.clave ? { ...fila, ajuste: a.ajuste === "AGREGADO" ? "AGREGADO" : "EDITADO" } : a)));
  };

  const agregar = () => {
    const siguiente = alertamientos.reduce((max, a) => Math.max(max, numeroDe(a.clave)), 0) + 1;
    onCambiar([...alertamientos, { clave: `Alertamiento ${siguiente}`, codigo: CODIGO_AJUSTE_FLIT, texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" }]);
  };

  const eliminar = (clave: string) => onCambiar(alertamientos.filter((a) => a.clave !== clave));

  const columnas: ColumnaEditable<FilaAlertamiento>[] = [
    { campo: "codigo", titulo: "Alertamiento", mostrar: (f) => `${f.clave} · ${titulo(f)}` },
    {
      campo: "texto",
      titulo: "Texto",
      editable: true,
      editor: (valor, cambiar, etiqueta) => (
        <input
          type="text"
          aria-label={etiqueta}
          value={String(valor ?? "")}
          maxLength={TEXTO_ALERTAMIENTO_MAX}
          disabled={deshabilitado}
          onChange={(e) => cambiar(e.target.value)}
          className={CONTROL}
        />
      ),
    },
    {
      campo: "efecto",
      titulo: "Efecto",
      editable: true,
      editor: (valor, cambiar, etiqueta) => (
        <select aria-label={etiqueta} value={String(valor)} disabled={deshabilitado} onChange={(e) => cambiar(e.target.value)} className={CONTROL}>
          {Object.entries(EFECTOS_DICTAMEN).map(([codigo, nombre]) => (
            <option key={codigo} value={codigo}>
              {nombre}
            </option>
          ))}
        </select>
      ),
    },
    {
      campo: "fuente",
      titulo: "Fuente",
      editable: true,
      editor: (valor, cambiar, etiqueta) => (
        <select aria-label={etiqueta} value={String(valor)} disabled={deshabilitado} onChange={(e) => cambiar(e.target.value)} className={CONTROL}>
          {FUENTES.map((f) => (
            <option key={f.codigo} value={f.codigo}>
              {f.nombre}
            </option>
          ))}
        </select>
      ),
    },
    {
      campo: "clave",
      titulo: "Quitar",
      mostrar: (f) => (
        <button type="button" aria-label={`Eliminar ${f.clave}`} disabled={deshabilitado} onClick={() => eliminar(f.clave)} className={BOTON_TEXTO}>
          Eliminar
        </button>
      ),
    },
  ];

  const mensajes = [
    ...Object.entries(erroresFila).map(([clave, mensaje]) => `${clave}: ${mensaje}`),
    ...Object.entries(errores)
      .map(([campo, mensaje]) => {
        const i = Number.parseInt(/^alertamientos\[(\d+)\]/.exec(campo)?.[1] ?? "", 10);
        const fila = Number.isInteger(i) ? alertamientos[i] : undefined;
        return fila && indice(fila.clave) >= 0 ? `${fila.clave}: ${mensaje}` : null;
      })
      .filter((m): m is string => m !== null),
  ];

  return (
    <div className="flex flex-col gap-2">
      <TablaEditable
        etiqueta="Alertamientos del ajuste"
        filas={alertamientos}
        columnas={columnas}
        clave={(f) => f.clave}
        onGuardar={guardar}
        vacio="Sin alertamientos. Un dictamen Bloqueado necesita al menos uno."
      />
      {mensajes.length > 0 && (
        <div role="alert" className="flex flex-col gap-1 text-sm text-[color:var(--di-danger)]">
          {mensajes.map((m) => (
            <p key={m}>{m}</p>
          ))}
        </div>
      )}
      <div>
        <button type="button" onClick={agregar} disabled={deshabilitado} className={BOTON_TEXTO}>
          Agregar alertamiento
        </button>
      </div>
    </div>
  );
}
