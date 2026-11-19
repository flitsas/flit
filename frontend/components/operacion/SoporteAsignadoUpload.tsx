'use client';

import { useId, useState } from 'react';
import { ChevronDown, FileUp } from 'lucide-react';
import { InlineAlert } from '@/components/atom/InlineAlert';

/** Lo que muestra la caja tras procesar el archivo. */
export interface ResultadoSoporteAsignado {
  ok: boolean;
  titulo: string;
  mensaje: string;
}

/**
 * Sección desplegable de carga de un soporte con el trámite en `asignado`, dentro del modal «Enviar al
 * OT»: título que despliega, ayuda y upload box FLIT (borde punteado azul, icono centrado, texto azul).
 * La comparten el PDF del SOAT (Bug #13194) y el del impuesto departamental; cada uno trae su flujo en
 * `onCargar`, que nunca lanza.
 */
export function SoporteAsignadoUpload({
  etiqueta,
  ayuda,
  textoSeleccionar,
  textoCargando,
  textoProcesando,
  abierto,
  onToggle,
  disabled = false,
  onCargar,
}: {
  etiqueta: string;
  ayuda: string;
  textoSeleccionar: string;
  textoCargando: string;
  textoProcesando: string;
  abierto: boolean;
  onToggle: (next: boolean) => void;
  disabled?: boolean;
  onCargar: (file: File) => Promise<ResultadoSoporteAsignado>;
}) {
  const panelId = useId();
  const inputId = useId();
  const [cargando, setCargando] = useState(false);
  const [resultado, setResultado] = useState<ResultadoSoporteAsignado | null>(null);

  const onFile = async (file: File | undefined) => {
    if (!file || cargando) return;
    setCargando(true);
    setResultado(null);
    const r = await onCargar(file);
    setResultado(r);
    setCargando(false);
  };

  return (
    <div className="mt-4">
      <button
        type="button"
        className="flex w-full items-center justify-between gap-2 rounded-xl text-left text-sm font-semibold disabled:opacity-60"
        style={{ color: 'var(--flit-brand-ink)' }}
        aria-expanded={abierto}
        aria-controls={panelId}
        onClick={() => onToggle(!abierto)}
        disabled={disabled}
      >
        {etiqueta}
        <ChevronDown
          aria-hidden="true"
          className={`h-4 w-4 transition-transform ${abierto ? 'rotate-180' : ''}`}
        />
      </button>
      {abierto ? (
        <div id={panelId} className="mt-2">
          <p className="text-xs opacity-80">{ayuda}</p>
          <label
            htmlFor={inputId}
            className="mt-2 flex cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed bg-card p-4 text-center text-sm font-semibold focus-within:ring-2 focus-within:ring-flit-brand dark:bg-transparent"
            style={{
              borderColor: 'var(--color-flit-brand)',
              color: 'var(--flit-brand-ink)',
              opacity: disabled || cargando ? 0.6 : 1,
            }}
          >
            <FileUp aria-hidden="true" className="h-5 w-5" />
            {cargando ? textoCargando : textoSeleccionar}
            <input
              id={inputId}
              type="file"
              accept="application/pdf"
              className="sr-only"
              disabled={disabled || cargando}
              onChange={(e) => {
                const f = e.target.files?.[0];
                e.target.value = '';
                void onFile(f);
              }}
            />
          </label>
          {cargando ? (
            <p role="status" className="mt-2 text-xs opacity-80">
              {textoProcesando}
            </p>
          ) : null}
          {resultado ? (
            <InlineAlert
              tone={resultado.ok ? 'success' : 'warning'}
              title={resultado.titulo}
              className="mt-3"
            >
              {resultado.mensaje}
            </InlineAlert>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
