"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { Modal } from "@/components/atom/Modal";

export interface MandatarioOrganismosProps {
  /** Nombre del mandatario: titula el modal y da nombre accesible al botón. */
  signerName: string;
  ids: readonly string[];
  nombrePorId: ReadonlyMap<string, string>;
  /** Organismos donde está habilitado pero no podría firmar (HU #11717). */
  sinFirmaIds: readonly string[];
  motivo: string;
}

const plural = (n: number) => (n === 1 ? "1 organismo" : `${n} organismos`);

/**
 * Organismos de un mandatario, en una sola píldora que abre un modal con el detalle. Con 2 o con 30
 * organismos la fila mide lo mismo; el aviso «no puede firmar» se ve por el tono ámbar y el motivo se
 * dice una sola vez dentro del modal. HU #11717 — se SEÑALA, no se inhabilita: los trámites en curso
 * siguen emitiendo su mandato como hoy.
 */
export function MandatarioOrganismos({
  signerName,
  ids,
  nombrePorId,
  sinFirmaIds,
  motivo,
}: MandatarioOrganismosProps) {
  const [open, setOpen] = useState(false);
  if (ids.length === 0) return <>—</>;

  const sinFirma = new Set(sinFirmaIds);
  const items = ids.map((id) => ({ id, nombre: nombrePorId.get(id) ?? id, bloqueado: sinFirma.has(id) }));
  const nBloqueados = items.filter((o) => o.bloqueado).length;
  const conAviso = nBloqueados > 0;
  const todos = nBloqueados === items.length;
  const tone = conAviso ? "warning" : "info";

  const etiqueta = !conAviso
    ? plural(items.length)
    : todos
      ? `${plural(items.length)} · sin firma`
      : `${plural(items.length)} · ${nBloqueados} sin firma`;

  return (
    <div data-testid="mandatario-organismos">
      <button
        type="button"
        onClick={() => setOpen(true)}
        title={items.map((o) => o.nombre).join("\n")}
        aria-label={`Ver organismos de ${signerName}: ${etiqueta}`}
        aria-haspopup="dialog"
        data-aviso={conAviso ? "true" : undefined}
        className="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2.5 py-1 text-xs font-semibold transition hover:brightness-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
        style={{
          background: `var(--badge-${tone}-bg)`,
          color: `var(--badge-${tone}-fg)`,
          borderColor: `var(--badge-${tone}-border)`,
        }}
      >
        {conAviso ? (
          <Icono nombre="alerta" className="h-3.5 w-3.5 shrink-0" />
        ) : (
          <Icono nombre="empresas" className="h-3.5 w-3.5 shrink-0" />
        )}
        {etiqueta}
      </button>

      {open && (
        <Modal
          open
          onClose={() => setOpen(false)}
          icono="empresas"
          iconBg="var(--color-flit-brand)"
          title={`Organismos de ${signerName}`}
          titleClassName="text-base font-bold text-flit-primary"
          size="md"
        >
          <div className="space-y-3 text-xs" data-testid="mandatario-organismos-detalle">
            <p className="text-muted-foreground">
              Organismos de tránsito donde está habilitado para firmar mandatos de la compañía.
            </p>

            <ul className="max-h-72 divide-y divide-border overflow-y-auto rounded-xl border border-border">
              {items.map((o) => (
                <li
                  key={o.id}
                  className="flex flex-col gap-0.5 px-3 py-2.5 sm:flex-row sm:items-center sm:justify-between sm:gap-3"
                  data-bloqueado={o.bloqueado ? "true" : undefined}
                >
                  <span className="font-medium text-flit-primary">{o.nombre}</span>
                  <span
                    className="inline-flex w-fit shrink-0 items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold"
                    style={{
                      background: `var(--badge-${o.bloqueado ? "warning" : "success"}-bg)`,
                      color: `var(--badge-${o.bloqueado ? "warning" : "success"}-fg)`,
                      borderColor: `var(--badge-${o.bloqueado ? "warning" : "success"}-border)`,
                    }}
                  >
                    {o.bloqueado ? (
                      <Icono nombre="alerta" className="h-3 w-3" />
                    ) : (
                      <Icono nombre="check" className="h-3 w-3" />
                    )}
                    {o.bloqueado ? "No puede firmar" : "Puede firmar"}
                  </span>
                </li>
              ))}
            </ul>

            {conAviso && (
              <div
                className="rounded-xl border px-3 py-2 leading-relaxed"
                style={{ borderColor: "var(--badge-warning-border)", background: "var(--badge-warning-bg)", color: "var(--badge-warning-fg)" }}
                role="note"
              >
                {todos
                  ? `No puede firmar todavía: ${motivo.toLowerCase()}`
                  : `No puede firmar en los organismos marcados: ${motivo.toLowerCase()}`}
              </div>
            )}

            <div className="flex justify-end pt-1">
              <button
                type="button"
                onClick={() => setOpen(false)}
                className="rounded-xl border px-4 py-2 text-xs font-semibold"
              >
                Cerrar
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
