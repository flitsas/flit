'use client';

import { useState } from 'react';

interface HelpSection {
  id: string;
  title: string;
  body: string;
}

const HELP_SECTIONS: HelpSection[] = [
  {
    id: 'entrada',
    title: 'Entrada',
    body: 'Define cómo entra el vehículo: VIN (vehículo nuevo), placa (ya matriculado) o ambas.',
  },
  {
    id: 'validaciones',
    title: 'Validaciones iniciales',
    body: 'Regla de compañía, operabilidad del OT y duplicidad de trámite, activables por tipo.',
  },
  {
    id: 'fuentes',
    title: 'Fuentes',
    body: 'Fuentes externas a consultar (RUNT, SIMIT, …), su orden y el modo de SIMIT.',
  },
  {
    id: 'actores',
    title: 'Actores',
    body: 'Participantes del trámite (propietario, comprador, locatario) y su perfil de validación.',
  },
  {
    id: 'documentos',
    title: 'Documentos',
    body: 'Documentos requeridos y buzones dummy (no bloquean el avance del paso).',
  },
  {
    id: 'identidad-firma',
    title: 'Identidad y firma',
    body: 'Biometría por actor y firma electrónica / FUR, según el tipo.',
  },
  {
    id: 'placa',
    title: 'Solicitud de placa',
    body: 'Activa el paso de solicitud de placa (FEATURE-04) cuando el tipo lo exige.',
  },
];

/**
 * FEATURE-08 / HU-FE-04 (CFD-11) — ayuda contextual del configurador. Acordeón accesible (WCAG) con
 * una sección por área de parametrización. Componente PURO: no llama a ninguna API.
 */
export function ParametrizationHelp() {
  const [openId, setOpenId] = useState<string | null>(null);

  const toggle = (id: string) => setOpenId((prev) => (prev === id ? null : id));

  return (
    <aside aria-label="Ayuda del configurador" className="space-y-2">
      <h2 className="text-sm font-bold mb-1">Ayuda</h2>
      {HELP_SECTIONS.map((section) => {
        const open = openId === section.id;
        const panelId = `help-panel-${section.id}`;
        const buttonId = `help-button-${section.id}`;
        return (
          <div key={section.id} className="rounded-xl border" style={{ borderColor: '#DFE5ED' }}>
            <h3 className="m-0">
              <button
                type="button"
                id={buttonId}
                aria-expanded={open}
                aria-controls={panelId}
                onClick={() => toggle(section.id)}
                className="w-full flex items-center justify-between px-3 py-2 text-left text-xs font-semibold"
              >
                {section.title}
                <span aria-hidden="true">{open ? '−' : '+'}</span>
              </button>
            </h3>
            {open && (
              <div
                id={panelId}
                role="region"
                aria-labelledby={buttonId}
                className="px-3 pb-3 text-[11px] opacity-70"
              >
                {section.body}
              </div>
            )}
          </div>
        );
      })}
    </aside>
  );
}
