"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ChevronDown, ChevronRight } from "lucide-react";
import type { DockEntry, DockGroup } from "../nav";
import { useDisclosureNav } from "./useDisclosureNav";
import { useEdgeClamp } from "./useEdgeClamp";

// Dock inferior flotante de la suite (GUIA-DOCK-INFERIOR-FLOTANTE.md), portado del de Trámites sin su catálogo:
// recibe los grupos ya filtrados (buildDock) y no sabe de ningún producto. Escritorio (lg+); en pantallas pequeñas
// la navegación va en el menú móvil de SuiteShell, con los mismos grupos.

type Props = {
  groups: DockGroup[];
  homeHref: string;
  homeLabel: string;
  homeIconSrc: string;
  homeActive: boolean;
};

/** Condensado por scroll (guía §8): cerca del final de la página, píldoras grandes solo con icono. */
function useAtBottom(): boolean {
  const [atBottom, setAtBottom] = useState(false);
  useEffect(() => {
    const onScroll = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight;
      setAtBottom(max > 80 && window.scrollY >= max - 80);
    };
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll);
    return () => {
      window.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onScroll);
    };
  }, []);
  return atBottom;
}

export function Dock({ groups, homeHref, homeLabel, homeIconSrc, homeActive }: Props) {
  const { openSection, toggle, close, navRef, triggerId, panelId } = useDisclosureNav("flit-suite-dock");
  const atBottom = useAtBottom();
  const showLabels = !atBottom || !!openSection;
  const large = atBottom && !openSection;

  const left = groups.filter((g) => g.side === "left");
  const right = groups.filter((g) => g.side === "right");
  const sideLen = Math.max(left.length, right.length);

  const pill = (g: DockGroup) => (
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
  );

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-40 hidden justify-center px-4 pb-5 lg:flex">
      <nav
        ref={navRef}
        aria-label="Navegación principal"
        className={`dock-capsula pointer-events-auto relative inline-flex max-w-full flex-wrap items-center justify-center gap-0.5 transition-[padding] duration-200 ease-out motion-reduce:transition-none ${
          large ? "p-1.5" : "p-1"
        }`}
      >
        {Array.from({ length: sideLen - left.length }).map((_, i) => (
          <DockSpacer key={`lp-${i}`} large={large} showLabels={showLabels} />
        ))}
        {left.map(pill)}
        <Link
          href={homeHref}
          onClick={close}
          className={`mx-2 shrink-0 overflow-hidden rounded-full transition-all duration-[var(--nav-duracion)] ease-[var(--nav-ease)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2 ${
            large ? "h-14 w-14" : "h-11 w-11"
          }`}
          style={{ boxShadow: "var(--nav-sombra-activo)" }}
          aria-label={homeLabel}
          aria-current={homeActive ? "page" : undefined}
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={homeIconSrc} alt="" aria-hidden="true" className="h-full w-full object-cover" />
        </Link>
        {right.map(pill)}
        {Array.from({ length: sideLen - right.length }).map((_, i) => (
          <DockSpacer key={`rp-${i}`} large={large} showLabels={showLabels} />
        ))}
      </nav>
    </div>
  );
}

function DockSpacer({ large, showLabels }: { large: boolean; showLabels: boolean }) {
  const w = showLabels ? (large ? "w-28" : "w-24") : large ? "w-11" : "w-9";
  return <span aria-hidden="true" className={`shrink-0 ${large ? "h-11" : "h-9"} ${w}`} />;
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
  group: DockGroup;
  showLabels: boolean;
  large: boolean;
  open: boolean;
  onToggle: () => void;
  onNavigate: () => void;
  triggerId: string;
  panelId: string;
}) {
  const { ref: panelRef, shift } = useEdgeClamp<HTMLDivElement>(open ? group.id : null);
  // Guía §5.3: una sola opción sin submenú → enlace directo, sin panel.
  const sole = group.items.length === 1 && !group.items[0].children?.length ? group.items[0] : null;
  const pillActive = sole ? sole.active : group.active;
  const Icon = sole?.icon ?? group.icon;
  const pillLabel = sole ? sole.label : group.label;
  const iconClass = large ? "h-[18px] w-[18px] shrink-0" : "h-4 w-4 shrink-0";

  const base = `dock-pill relative flex items-center justify-center gap-1.5 whitespace-nowrap rounded-full transition-all duration-[var(--nav-duracion)] ease-[var(--nav-ease)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2 ${
    large ? "h-11" : "h-9"
  } ${showLabels ? "px-3" : "w-11 px-0"} ${pillActive || open ? "font-semibold" : "font-medium"}`;

  if (sole) {
    return (
      <Link
        id={triggerId}
        href={sole.href}
        onClick={onNavigate}
        className={base}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-current={sole.active ? "page" : undefined}
      >
        <Icon className={iconClass} strokeWidth={pillActive ? 2.4 : 1.8} aria-hidden="true" />
        <span className={showLabels ? "truncate text-sm" : "sr-only"}>{pillLabel}</span>
      </Link>
    );
  }

  return (
    <div className="relative">
      <button
        id={triggerId}
        type="button"
        onClick={onToggle}
        className={base}
        aria-label={pillLabel}
        title={!showLabels ? pillLabel : undefined}
        aria-expanded={open}
        aria-controls={panelId}
        data-ancestor-active={group.active ? "true" : undefined}
      >
        <Icon className={iconClass} strokeWidth={pillActive ? 2.4 : 1.8} aria-hidden="true" />
        <span className={showLabels ? "truncate text-sm" : "sr-only"}>{pillLabel}</span>
        {showLabels && (
          <ChevronDown
            className={`h-3.5 w-3.5 shrink-0 transition-transform duration-200 motion-reduce:transition-none ${open ? "rotate-0" : "rotate-180"}`}
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
          className="panel-up absolute bottom-full left-0 z-30 mb-2.5 max-h-[min(70vh,32rem)] min-w-[16rem] overflow-y-auto rounded-[var(--nav-radio-panel)] border border-[var(--nav-borde)] p-2"
          style={{ background: "var(--nav-panel-bg)", boxShadow: "var(--nav-sombra-panel)", left: shift ? `${shift}px` : undefined }}
        >
          <ul className="flex flex-col gap-0.5">
            {group.items.map((it) => (
              <DockPanelItem key={it.key} item={it} onNavigate={onNavigate} />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

const itemClass = (active: boolean) =>
  `dock-panel-item group flex w-full items-center gap-2.5 rounded-full px-3 py-2 text-left text-sm transition-colors ${
    active ? "text-white" : "text-[var(--nav-texto)] hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)]"
  }`;

function DockLink({ item, onNavigate }: { item: DockEntry; onNavigate: () => void }) {
  const ItemIcon = item.icon;
  return (
    <Link href={item.href} onClick={onNavigate} className={itemClass(item.active)} aria-current={item.active ? "page" : undefined}>
      <ItemIcon className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span className="truncate">{item.label}</span>
      <span
        aria-hidden="true"
        className="ml-auto h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--nav-borde)] transition-colors group-hover:bg-[var(--color-flit-brand)] group-aria-[current=page]:bg-white"
      />
    </Link>
  );
}

function DockPanelItem({ item, onNavigate }: { item: DockEntry; onNavigate: () => void }) {
  const kids = item.children;
  const [nestedOpen, setNestedOpen] = useState(() => Boolean(kids?.some((c) => c.active) || item.active));
  if (!kids?.length) {
    return (
      <li>
        <DockLink item={item} onNavigate={onNavigate} />
      </li>
    );
  }

  const ItemIcon = item.icon;
  const nestedId = `flit-suite-dock-nested-${item.key}`;
  return (
    <li>
      <button
        type="button"
        onClick={() => setNestedOpen((v) => !v)}
        className={`group flex w-full items-center gap-2.5 rounded-full px-3 py-2 text-left text-sm transition-colors hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)] ${
          item.active || kids.some((c) => c.active) ? "font-semibold text-[var(--nav-texto-fuerte)]" : "text-[var(--nav-texto)]"
        }`}
        aria-expanded={nestedOpen}
        aria-controls={nestedId}
      >
        <ItemIcon className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span className="truncate">{item.label}</span>
        <ChevronRight
          className={`ml-auto h-3.5 w-3.5 shrink-0 transition-transform duration-200 motion-reduce:transition-none ${nestedOpen ? "rotate-90" : ""}`}
          aria-hidden="true"
        />
      </button>
      {nestedOpen && (
        <ul id={nestedId} className="ml-3 mt-0.5 flex flex-col gap-0.5 border-l border-[var(--nav-borde)] pl-2">
          {kids.map((child) => (
            <li key={child.key}>
              <DockLink item={child} onNavigate={onNavigate} />
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}
