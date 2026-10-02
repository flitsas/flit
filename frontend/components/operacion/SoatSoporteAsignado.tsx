'use client';

import { useId, useState } from 'react';
import { ChevronDown, FileUp } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { InlineAlert } from '@/components/atom/InlineAlert';
import {
  cargarSoporteSoatAsignado,
  type ResultadoSoporteSoat,
} from '@/lib/tramites/soporte-soat-asignado';

/**
 * Bug #13194 (P3) — carga del PDF del SOAT con el trámite en `asignado`, dentro del modal «Enviar al
 * OT». Es la salida del gestor cuando el RUNT no reporta el SOAT y la compañía no deja continuar sin
 * él: subir el PDF, registrar su lectura y reintentar el envío. Upload box FLIT (borde punteado azul,
 * icono centrado, texto azul); el flujo vive en `cargarSoporteSoatAsignado`.
 */
export function SoatSoporteAsignado({
  instanceId,
  tenantId,
  abierto,
  onToggle,
  disabled = false,
  onResultado,
}: {
  instanceId: string;
  tenantId?: string;
  /** Desplegado (lo abre el modal al recibir 409 `soat_no_vigente`). */
  abierto: boolean;
  onToggle: (next: boolean) => void;
  disabled?: boolean;
  onResultado?: (r: ResultadoSoporteSoat) => void;
}) {
  const panelId = useId();
  const inputId = useId();
  const [cargando, setCargando] = useState(false);
  const [resultado, setResultado] = useState<ResultadoSoporteSoat | null>(null);

  const onFile = async (file: File | undefined) => {
    if (!file || cargando) return;
    setCargando(true);
    setResultado(null);
    const r = await cargarSoporteSoatAsignado(tramitesClient, instanceId, file, tenantId);
    setResultado(r);
    setCargando(false);
    onResultado?.(r);
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
        Cargar PDF del SOAT
        <ChevronDown
          aria-hidden="true"
          className={`h-4 w-4 transition-transform ${abierto ? 'rotate-180' : ''}`}
        />
      </button>
      {abierto ? (
        <div id={panelId} className="mt-2">
          <p className="text-xs opacity-80">
            Si el RUNT no reporta el SOAT del vehículo, carga el PDF de la póliza vigente y vuelve a
            enviar el trámite.
          </p>
          <label
            htmlFor={inputId}
            className="mt-2 flex cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed bg-white p-4 text-center text-sm font-semibold focus-within:ring-2 focus-within:ring-[#557EFF] dark:bg-transparent"
            style={{
              borderColor: '#557EFF',
              color: 'var(--flit-brand-ink)',
              opacity: disabled || cargando ? 0.6 : 1,
            }}
          >
            <FileUp aria-hidden="true" className="h-5 w-5" />
            {cargando ? 'Cargando y leyendo el PDF del SOAT…' : 'Selecciona el PDF del SOAT'}
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
              Procesando el PDF del SOAT…
            </p>
          ) : null}
          {resultado ? (
            <InlineAlert
              tone={resultado.estado === 'vigente' ? 'success' : 'warning'}
              title={resultado.estado === 'vigente' ? 'SOAT registrado' : 'SOAT pendiente'}
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
