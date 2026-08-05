"use client";

import { ChevronDown } from "lucide-react";
import type { DockGroupView } from "./dockGroups";
import { useDisclosureNav } from "./useDisclosureNav";
import { useEdgeClamp } from "./useEdgeClamp";

const FAB_SRC = "/assets/favicon.svg";

type Props = {
  groups: DockGroupView[];
  /** true = scroll abajo: menú grande, solo iconos */
  atBottom: boolean;
  onHome: () => void;
  homeActive: boolean;
};

/**
 * Dock de escritorio: agrupadores con panel hacia arriba.
 * Arriba (scroll): píldoras compactas con nombre.
 * Abajo (scroll): píldoras grandes solo icono.
 */
export function DockDesktop({ groups, atBottom, onHome, homeActive }: Props) {
  const { openSection, toggle, close, navRef, triggerId, panelId } = useDisclosureNav("flit-dock");
  // Al abrir un panel se fuerza modo con etiquetas (más legible).
  const showLabels = !atBottom || !!openSection;
  const large = atBottom && !openSection;

  const half = Math.ceil(groups.length / 2);
  const left = groups.slice(0, half);
  const right = groups.slice(half);
  const sideLen = Math.max(left.length, right.length);
  const leftPad = sideLen - left.length;
  const rightPad = sideLen - right.length;

  return (
    <div className="pointer-events-none absolute inset-x-0 bottom-0 z-40 hidden justify-center px-4 pb-5 lg:flex">
      <nav
        ref={navRef}
        aria-label="Navegación principal"
        className={`dock-capsula pointer-events-auto relative inline-flex max-w-full flex-wrap items-center justify-center gap-0.5 transition-[padding] duration-200 ease-out motion-reduce:transition-none ${
          large ? "p-1.5" : "p-1"
        }`}
      >
        {Array.from({ length: leftPad }).map((_, i) => (
          <DockSpacer key={`lp-${i}`} large={large} showLabels={showLabels} />
        ))}
        {left.map((g) => (
          <DockGroupPill
            key={g.id}
            group={g}
            showLabels={showLabels}
            large={large}
            open={openSection === g.id}
            onToggle={() => toggle(g.id)}
            onNavigate={close}
            triggerId={triggerId(g.id)}
            panelId={panelId(g.id)}
          />
        ))}

        <button
          onClick={onHome}
          className={`mx-2 shrink-0 overflow-hidden rounded-full transition-all duration-[var(--nav-duracion)] ease-[var(--nav-ease)] hover:scale-105 motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2 ${
            large ? "h-14 w-14" : "h-11 w-11"
          }`}
          style={{ boxShadow: "var(--nav-sombra-activo)" }}
          aria-label="Inicio FLIT"
          aria-current={homeActive ? "page" : undefined}
        >
          <img src={FAB_SRC} alt="" aria-hidden="true" className="h-full w-full object-cover" />
        </button>

        {right.map((g) => (
          <DockGroupPill
            key={g.id}
            group={g}
            showLabels={showLabels}
            large={large}
            open={openSection === g.id}
            onToggle={() => toggle(g.id)}
            onNavigate={close}
            triggerId={triggerId(g.id)}
            panelId={panelId(g.id)}
          />
        ))}
        {Array.from({ length: rightPad }).map((_, i) => (
          <DockSpacer key={`rp-${i}`} large={large} showLabels={showLabels} />
        ))}
      </nav>
    </div>
  );
}

function DockSpacer({ large, showLabels }: { large: boolean; showLabels: boolean }) {
  // Reserva aproximada del ancho de una píldora con/sin etiqueta.
  const w = showLabels ? (large ? "w-28" : "w-24") : large ? "w-11" : "w-9";
  const h = large ? "h-11" : "h-9";
  return <span aria-hidden="true" className={`shrink-0 ${h} ${w}`} />;
}

function DockGroupPill({
  group,
  showLabels,
  large,
  open,
  onToggle,
  onNavigate,
  triggerId,
  panelId,
}: {
  group: DockGroupView;
  showLabels: boolean;
  large: boolean;
  open: boolean;
  onToggle: () => void;
  onNavigate: () => void;
  triggerId: string;
  panelId: string;
}) {
  const { ref: panelRef, shift } = useEdgeClamp<HTMLDivElement>(open ? group.id : null);
  const multi = group.items.length > 1 || group.forceMenu;
  const sole = !multi ? group.items[0] : null;
  const lit = group.active || open;
  const Icon = sole?.icon ?? group.icon;
  const pillLabel = sole ? sole.label : group.label;

  const base = `dock-pill relative flex items-center justify-center gap-1.5 whitespace-nowrap rounded-full transition-all duration-[var(--nav-duracion)] ease-[var(--nav-ease)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2 ${
    large ? "h-11" : "h-9"
  } ${showLabels ? "px-3" : "w-11 px-0"} ${lit ? "font-semibold text-white" : "font-medium"}`;

  const litStyle = lit
    ? { background: "var(--nav-activo)", boxShadow: "var(--nav-sombra-activo)", color: "#ffffff" }
    : undefined;

  if (sole) {
    return (
      <button
        id={triggerId}
        onClick={() => {
          onNavigate();
          sole.onClick();
        }}
        className={base}
        style={litStyle}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-current={sole.active ? "page" : undefined}
      >
        <Icon
          className={large ? "h-[18px] w-[18px] shrink-0" : "h-4 w-4 shrink-0"}
          strokeWidth={lit ? 2.4 : 1.8}
          aria-hidden="true"
          style={{ color: lit ? "#ffffff" : undefined }}
        />
        <span className={showLabels ? "truncate text-sm" : "sr-only"}>{pillLabel}</span>
      </button>
    );
  }

  return (
    <div className="relative">
      <button
        id={triggerId}
        type="button"
        onClick={onToggle}
        className={base}
        style={litStyle}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-expanded={open}
        aria-controls={panelId}
      >
        <Icon
          className={large ? "h-[18px] w-[18px] shrink-0" : "h-4 w-4 shrink-0"}
          strokeWidth={lit ? 2.4 : 1.8}
          aria-hidden="true"
          style={{ color: lit ? "#ffffff" : undefined }}
        />
        <span className={showLabels ? "truncate text-sm" : "sr-only"}>{pillLabel}</span>
        {showLabels && (
          <ChevronDown
            className={`h-3.5 w-3.5 shrink-0 transition-transform duration-200 motion-reduce:transition-none ${
              open ? "rotate-0" : "rotate-180"
            }`}
            aria-hidden="true"
          />
        )}
      </button>

      {open && (
        <div
          ref={panelRef}
          id={panelId}
          role="region"
          aria-label={group.label}
          className="panel-up absolute bottom-full left-0 z-30 mb-2.5 min-w-[16rem] max-h-[min(70vh,32rem)] overflow-y-auto rounded-[var(--nav-radio-panel)] border border-[var(--nav-borde)] p-2"
          style={{
            background: "var(--nav-panel-bg)",
            boxShadow: "var(--nav-sombra-panel)",
            left: shift ? `${shift}px` : undefined,
            ["--acc" as string]: "#4F74C9",
          }}
        >
          <ul className="flex flex-col gap-0.5">
            {group.items.map((it) => {
              const ItemIcon = it.icon;
              return (
                <li key={it.key}>
                  <button
                    type="button"
                    onClick={() => {
                      onNavigate();
                      it.onClick();
                    }}
                    className="group flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left text-sm text-[var(--nav-texto)] transition-colors hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)] aria-[current=page]:bg-[var(--nav-app-bg)] aria-[current=page]:font-semibold aria-[current=page]:text-[var(--nav-texto-fuerte)]"
                    aria-current={it.active ? "page" : undefined}
                  >
                    <ItemIcon className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span className="truncate">{it.label}</span>
                    <span
                      aria-hidden="true"
                      className="ml-auto h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--nav-borde)] transition-colors group-hover:bg-[var(--acc)] group-aria-[current=page]:bg-[var(--acc)]"
                    />
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
