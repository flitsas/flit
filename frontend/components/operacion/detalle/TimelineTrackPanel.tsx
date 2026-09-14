'use client';

import { CampoValorInline } from './primitivos';
import { DETALLE_BLUE, DETALLE_CARD, DETALLE_NAVY } from './detalle-visual';

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
  /** El hito vigente (p. ej. el último estado del trámite) se marca con la etiqueta "Vigente". */
  isActive?: boolean;
}

export interface TimelineTrackPanelProps {
  title: string;
  nodes: TimelineTrackNode[];
  emptyMessage?: string;
}

/**
 * Línea de tiempo vertical — rediseño del track horizontal con scroll que usaba el mockup
 * `DetalleTramiteModal`/`TimelineTrack`.
 *
 * <p><b>Por qué se abandonó el track horizontal.</b> Cada hito era un punto con su etiqueta debajo
 * en una sola línea (`whitespace-nowrap`, sin `overflow-hidden`): con pocos hitos se leía bien,
 * pero con muchos —varios "Reenvío de validación · Comprador" seguidos, por ejemplo— las etiquetas
 * se desbordaban de su columna y se superponían con las del vecino. No era un bug de un caso
 * límite: es la forma que tiene un track horizontal de fallar en cuanto el contenido no cabe.</p>
 *
 * <p><b>Por qué carril vertical y no otro ajuste del track.</b> Truncar las etiquetas escondía
 * justo el dato que este panel existe para mostrar (quién, qué correo, qué empresa). El patrón de
 * carril vertical con una tarjeta por evento ya está probado en FLIT —el Historial de
 * `TramiteTrackingModal`— y no tiene techo: cada evento es su propia tarjeta, nunca compite por
 * ancho con el de al lado, y la lista simplemente crece hacia abajo. Sin scroll interno propio a
 * propósito: el modal contenedor (`DETALLE_SHEET_CLASS`) ya scrollea de punta a punta, y un
 * `max-h` + `overflow-y-auto` aquí dentro solo agregaba una SEGUNDA barra de scroll superpuesta a
 * la del modal — confuso, no una mejora.</p>
 *
 * <p><b>Por qué ya no hay selección máster/detalle.</b> El track horizontal mostraba el detalle de
 * UN hito a la vez (el que estuviera enfocado); los demás quedaban reducidos a un punto mudo. Con
 * el carril vertical todos los hitos muestran su información completa siempre: es más información
 * en pantalla, pero es exactamente la que el panel promete, y evita depender de hover/click para
 * revelarla.</p>
 */
export function TimelineTrackPanel({ title, nodes, emptyMessage }: TimelineTrackPanelProps) {
  if (nodes.length === 0) {
    return (
      <div className={CARD}>
        <h4 className="mb-4 shrink-0 text-sm font-bold" style={{ color: DETALLE_BLUE }}>
          {title}
        </h4>
        <p className="text-xs opacity-70">{emptyMessage ?? 'Sin eventos registrados todavía.'}</p>
      </div>
    );
  }

  return (
    <div className={CARD}>
      <h4 className="mb-4 shrink-0 text-sm font-bold" style={{ color: DETALLE_BLUE }}>
        {title}
      </h4>
      <ol aria-label={title} className="relative space-y-4 py-1 pl-5">
        {nodes.map((n, i) => (
          <li key={`${n.label}-${i}`} className="relative">
            {/* Carril: punto de color propio del hito + línea de conexión hacia el siguiente. El
                color ya trae semántica de estado (verde/azul/rojo…) desde los mappers; "Vigente"
                se dice con texto, no solo con el punto, para no depender solo de color. */}
            <span
              className="absolute -left-5 top-1.5 h-3 w-3 rounded-full"
              style={{ background: n.color, boxShadow: `0 0 0 4px ${n.color}1F` }}
              aria-hidden="true"
            />
            {i < nodes.length - 1 ? (
              <span
                className="absolute -left-[13px] top-6 bottom-[-16px] w-px bg-[#DFE5ED] dark:bg-white/10"
                aria-hidden="true"
              />
            ) : null}

            <div className="rounded-lg border border-[#DFE5ED] p-3 dark:border-white/10">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <p className="text-xs font-bold" style={{ color: DETALLE_NAVY }}>
                  {n.label}
                </p>
                {n.isActive ? (
                  <span className="rounded-full bg-[rgba(0,219,213,0.15)] px-2 py-0.5 text-xs font-semibold text-[#0F766E] dark:bg-[rgba(0,219,213,0.18)] dark:text-[#5EEAD4]">
                    Vigente
                  </span>
                ) : null}
              </div>
              <dl className="grid grid-cols-1 gap-x-6 gap-y-1.5 sm:grid-cols-2">
                <CampoValorInline campo="Gestor" valor={n.info.gestor} />
                <CampoValorInline campo="Correo" valor={n.info.correo} />
                <CampoValorInline campo="Empresa" valor={n.info.empresa} />
                <CampoValorInline campo="Rol" valor={n.info.rol} />
                <CampoValorInline campo="Fecha y hora" valor={n.info.fecha} />
                {n.info.extra ? (
                  <CampoValorInline campo="Detalle" valor={n.info.extra} className="sm:col-span-2" />
                ) : null}
              </dl>
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}
