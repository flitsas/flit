"use client";

import { useId, type RefObject } from "react";
import Link from "next/link";
import { ChevronUp } from "lucide-react";
import type { DockEntry, DockGroup } from "../nav";
import { DockGlyph } from "./DockGlyph";
import { useDisclosureNav } from "./useDisclosureNav";
import { useDockScrollCondense } from "./useDockScrollCondense";
import { useEdgeClamp } from "./useEdgeClamp";
import { useDockHomeMark } from "./DockHomeMark";

// Dock inferior flotante de la suite (GUIA-DOCK-INFERIOR-FLOTANTE.md), portado del de Trámites sin su catálogo:
// recibe los grupos ya filtrados (buildDock) y no sabe de ningún producto. Escritorio (lg+); en pantallas pequeñas
// la navegación va en DockMobile (lanzador + hoja), con los mismos grupos.
// Aspecto: el `.dock` del prototipo «Diseño Inteligente» (rama `propuesta`, HU #14479): píldoras con ilustración y
// submenú con franja tinta, entradas ilustradas con su descripción y, si el grupo trae secciones, dos columnas.

type Props = {
  groups: DockGroup[];
  homeHref: string;
  homeLabel: string;
  homeIconSrc: string;
  homeActive: boolean;
  /** Contenedor con scroll (layout `app`); sin él, la ventana. El dock queda dentro de ese contenedor, no fijo. */
  scrollRef?: RefObject<HTMLElement | null>;
};

export function Dock({ groups, homeHref, homeLabel, homeIconSrc, homeActive, scrollRef }: Props) {
  const { openSection, toggle, close, navRef, triggerId, panelId } = useDisclosureNav("flit-suite-dock");
  // Guía §8: al bajar, solo la ilustración (como el dock del prototipo en tableta); al subir o cerca del inicio, con
  // el nombre.
  const atBottom = useDockScrollCondense(scrollRef);
  const showLabels = !atBottom || !!openSection;
  const home = useDockHomeMark(homeIconSrc);

  const left = groups.filter((g) => g.side === "left");
  const right = groups.filter((g) => g.side === "right");
  const sideLen = Math.max(left.length, right.length);

  const pill = (g: DockGroup) => (
    <DockGroupPill
      key={g.id}
      group={g}
      showLabels={showLabels}
      open={openSection === g.id}
      onToggle={() => toggle(g.id)}
      onNavigate={close}
      triggerId={triggerId(g.id)}
      panelId={panelId(g.id)}
    />
  );

  return (
    <div className={`pointer-events-none ${scrollRef ? "absolute" : "fixed"} inset-x-0 bottom-0 z-40 hidden justify-center px-2 pb-[18px] lg:flex`}>
      <nav ref={navRef} aria-label="Navegación principal" className="dock-capsula pointer-events-auto relative inline-flex max-w-full items-center">
        {Array.from({ length: sideLen - left.length }).map((_, i) => (
          <DockSpacer key={`lp-${i}`} showLabels={showLabels} />
        ))}
        {left.map(pill)}
        <Link
          href={homeHref}
          onClick={close}
          className={`dock-fab ${home.shape === "pill" ? "dock-fab--pastilla" : ""}`}
          aria-label={homeLabel}
          aria-current={homeActive ? "page" : undefined}
        >
          {home.content}
        </Link>
        {right.map(pill)}
        {Array.from({ length: sideLen - right.length }).map((_, i) => (
          <DockSpacer key={`rp-${i}`} showLabels={showLabels} />
        ))}
      </nav>
    </div>
  );
}

function DockSpacer({ showLabels }: { showLabels: boolean }) {
  return <span aria-hidden="true" className={`h-11 shrink-0 ${showLabels ? "w-28" : "w-[52px]"}`} />;
}

/** Hojas de la entrada: ella misma o, si es contenedor, sus hijos. */
function leaves(entries: DockEntry[]): DockEntry[] {
  return entries.flatMap((it) => (it.children?.length ? it.children : [it]));
}

type Block = { key: string; title?: string; items: DockEntry[] };

/**
 * Bloques del submenú en el orden del catálogo: las entradas sueltas seguidas van juntas en un bloque sin nombre; cada
 * contenedor (Plataforma, ICT…) es una sección con su nombre, como `.dock__seccion` del prototipo.
 */
function blocks(entries: DockEntry[]): Block[] {
  const out: Block[] = [];
  for (const it of entries) {
    if (it.children?.length) {
      out.push({ key: it.key, title: it.label, items: it.children });
      continue;
    }
    const last = out[out.length - 1];
    if (last && !last.title) last.items.push(it);
    else out.push({ key: `suelta-${it.key}`, items: [it] });
  }
  return out;
}

function DockGroupPill({
  group,
  showLabels,
  open,
  onToggle,
  onNavigate,
  triggerId,
  panelId,
}: {
  group: DockGroup;
  showLabels: boolean;
  open: boolean;
  onToggle: () => void;
  onNavigate: () => void;
  triggerId: string;
  panelId: string;
}) {
  // Guía §5.3: una sola opción sin submenú → enlace directo, sin panel.
  const sole = group.items.length === 1 && !group.items[0].children?.length ? group.items[0] : null;
  // Una sola entrada con submenú: la píldora lleva su nombre (Administradores → solo «Plataforma» → «Plataforma»).
  const only = group.items.length === 1 ? group.items[0] : null;
  const glyph = only ? { icon: only.icon, ilustracion: only.ilustracion ?? group.ilustracion } : group;
  const pillLabel = only ? only.label : group.label;
  const parts = blocks(group.items);
  // Dos o más secciones con nombre: el panel ancho en dos columnas (`.dock__menu--ancho`).
  const wide = parts.filter((b) => b.title).length >= 2;
  const { ref: panelRef, shift } = useEdgeClamp<HTMLDivElement>(open && !wide ? group.id : null);
  const count = leaves(group.items).length;

  const pillClass = `dock-pill ${showLabels ? "" : "dock-pill--compacta"}`;
  const label = <span className={showLabels ? "truncate" : "sr-only"}>{pillLabel}</span>;
  const art = <DockGlyph icon={glyph.icon} ilustracion={glyph.ilustracion} className="dock-pill__glifo" />;

  if (sole) {
    return (
      <Link
        id={triggerId}
        href={sole.href}
        onClick={onNavigate}
        className={pillClass}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-current={sole.active ? "page" : undefined}
      >
        {art}
        {label}
      </Link>
    );
  }

  return (
    // El panel ancho se centra sobre todo el dock (su contenedor posicionado es el <nav>), no sobre la píldora.
    <div className={wide ? "static" : "relative"}>
      <button
        id={triggerId}
        type="button"
        onClick={onToggle}
        className={pillClass}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-expanded={open}
        aria-controls={panelId}
        data-ancestor-active={group.active ? "true" : undefined}
      >
        {art}
        {label}
        {showLabels && <ChevronUp className="dock-flecha" aria-hidden="true" />}
      </button>

      {open && (
        <div
          ref={panelRef}
          id={panelId}
          role="region"
          aria-label={group.label}
          className={`dock-menu panel-up ${wide ? "dock-menu--ancho" : ""}`}
          style={shift ? { marginLeft: `${shift}px` } : undefined}
        >
          {/* Franja tinta del prototipo: ilustración, nombre del grupo y cuántos módulos tiene. Decorativa: la región ya
              se anuncia con ese nombre. */}
          <div aria-hidden="true" className="dock-menu__cabeza">
            <DockGlyph icon={group.icon} ilustracion={group.ilustracion} className="dock-menu__cabeza-glifo" />
            <span className="dock-menu__cabeza-texto">
              <strong>{group.label}</strong>
              <small>
                {count} {count === 1 ? "módulo" : "módulos"}
              </small>
            </span>
          </div>
          <div className={wide ? "dock-menu__secciones" : "dock-menu__lista"}>
            {parts.map((b) => (
              <DockBlock key={b.key} block={b} panelId={panelId} onNavigate={onNavigate} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function DockBlock({ block, panelId, onNavigate }: { block: Block; panelId: string; onNavigate: () => void }) {
  const list = (
    <ul className="dock-menu__entradas">
      {block.items.map((it) => (
        <li key={it.key}>
          <DockEntryLink item={it} onNavigate={onNavigate} />
        </li>
      ))}
    </ul>
  );
  if (!block.title) return list;
  const titleId = `${panelId}-seccion-${block.key}`;
  return (
    <div role="group" aria-labelledby={titleId} className="dock-menu__seccion">
      <p id={titleId} className="dock-menu__seccion-titulo">
        {block.title}
      </p>
      {list}
    </div>
  );
}

/**
 * Entrada del submenú (también la usa la hoja móvil): ilustración de 32 px, nombre y, si el catálogo la trae, la
 * descripción corta. El nombre accesible es solo el nombre; la descripción se anuncia como descripción.
 */
export function DockEntryLink({ item, onNavigate }: { item: DockEntry; onNavigate: () => void }) {
  const uid = useId();
  const descId = item.descripcion ? `${uid}-desc` : undefined;
  return (
    <Link
      href={item.href}
      onClick={onNavigate}
      className="dock-entrada"
      aria-label={item.label}
      aria-describedby={descId}
      aria-current={item.active ? "page" : undefined}
    >
      <DockGlyph icon={item.icon} ilustracion={item.ilustracion} className="dock-entrada__glifo" />
      <span className="dock-entrada__texto">
        <strong>{item.label}</strong>
        {item.descripcion && <small id={descId}>{item.descripcion}</small>}
      </span>
    </Link>
  );
}
