'use client';

import { useState } from 'react';
import { CampoValorInline } from './primitivos';
import { DETALLE_BLUE, DETALLE_CARD, DETALLE_NAVY, DETALLE_BORDER } from './detalle-visual';

const CARD = `${DETALLE_CARD} flex h-full flex-col p-5`;

export interface TimelineTrackNode {
  label: string;
  color: string;
  info: {
    gestor: string;
    correo: string;
    empresa: string;
    rol: string;
    fecha: string;
    extra?: string;
  };
  /** Si true, se preselecciona al montar (p. ej. último hito de la línea de tiempo). */
  isActive?: boolean;
}

export interface TimelineTrackPanelProps {
  title: string;
  nodes: TimelineTrackNode[];
  emptyMessage?: string;
}

/**
 * Línea de tiempo horizontal con puntos 16px (mockup `DetalleTramiteModal` / `TimelineTrack`).
 * Transformación puramente visual — los datos los mapean `timeline-mappers.ts`.
 */
export function TimelineTrackPanel({ title, nodes, emptyMessage }: TimelineTrackPanelProps) {
  const defaultOpen = Math.max(
    0,
    nodes.findIndex((n) => n.isActive) >= 0 ? nodes.findIndex((n) => n.isActive) : nodes.length - 1,
  );
  const [open, setOpen] = useState(defaultOpen);

  if (nodes.length === 0) {
    return (
      <div className={`${CARD} flex h-full flex-col p-5`}>
        <h4 className="mb-4 shrink-0 text-sm font-bold" style={{ color: BLUE }}>
          {title}
        </h4>
        <p className="text-xs opacity-70">{emptyMessage ?? 'Sin eventos registrados todavía.'}</p>
      </div>
    );
  }

  const active = nodes[open] ?? nodes[0]!;

  return (
    <div className={`${CARD} flex h-full flex-col p-5`}>
        <h4 className="mb-6 shrink-0 text-sm font-bold" style={{ color: DETALLE_BLUE }}>
        {title}
      </h4>
      <div className="flex select-none items-start overflow-x-auto pb-2">
        {nodes.map((n, i) => (
          <div key={`${n.label}-${i}`} className="flex min-w-[160px] flex-1 items-center">
            <button
              type="button"
              onClick={() => setOpen(i)}
              onMouseEnter={() => setOpen(i)}
              className="group flex min-w-[150px] shrink-0 flex-col items-center gap-2 px-3"
              aria-pressed={open === i}
            >
              <span
                className="h-4 w-4 rounded-full transition"
                style={{
                  background: n.color,
                  boxShadow:
                    open === i ? `0 0 0 5px ${n.color}33` : `0 0 0 3px ${n.color}1F`,
                }}
              />
              <span
                className="whitespace-nowrap text-[11px] transition"
                style={{ color: open === i ? DETALLE_BLUE : DETALLE_NAVY, fontWeight: open === i ? 700 : 500 }}
              >
                {n.label}
              </span>
            </button>
            {i < nodes.length - 1 ? (
              <div
                className="mx-1 mb-6 h-0.5 min-w-[24px] flex-1 rounded-full"
                style={{ background: '#DFE5ED' }}
              />
            ) : null}
          </div>
        ))}
      </div>

      <div className="mt-5 flex-1 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10">
        <div className="mb-2 flex items-center gap-2">
          <span className="h-2.5 w-2.5 rounded-full" style={{ background: active.color }} />
          <p className="text-xs font-bold" style={{ color: DETALLE_NAVY }}>
            {active.label}
          </p>
        </div>
        <dl className="grid grid-cols-1 gap-x-6 gap-y-1.5 sm:grid-cols-2">
          <CampoValorInline campo="Gestor" valor={active.info.gestor} />
          <CampoValorInline campo="Correo" valor={active.info.correo} />
          <CampoValorInline campo="Empresa" valor={active.info.empresa} />
          <CampoValorInline campo="Rol" valor={active.info.rol} />
          <CampoValorInline campo="Fecha y hora" valor={active.info.fecha} />
          {active.info.extra ? (
            <CampoValorInline campo="Detalle" valor={active.info.extra} className="sm:col-span-2" />
          ) : null}
        </dl>
      </div>
    </div>
  );
}
