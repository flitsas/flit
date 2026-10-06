"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Disclosure WAI-ARIA para paneles del dock (GUIA-DOCK §9).
 * Sin role="menu": los ítems son botones/enlaces de navegación.
 */
export function useDisclosureNav(idPrefix: string) {
  const navRef = useRef<HTMLElement>(null);
  const [openSection, setOpenSection] = useState<string | null>(null);

  const triggerId = useCallback((s: string) => `${idPrefix}-trigger-${s}`, [idPrefix]);
  const panelId = useCallback((s: string) => `${idPrefix}-panel-${s}`, [idPrefix]);

  const toggle = useCallback((s: string) => {
    setOpenSection((prev) => (prev === s ? null : s));
  }, []);

  const close = useCallback(() => setOpenSection(null), []);

  useEffect(() => {
    if (!openSection) return;
    const onPointerDown = (e: PointerEvent) => {
      if (!navRef.current?.contains(e.target as Node)) setOpenSection(null);
    };
    window.addEventListener("pointerdown", onPointerDown);
    return () => window.removeEventListener("pointerdown", onPointerDown);
  }, [openSection]);

  useEffect(() => {
    if (!openSection) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      const trigger = navRef.current?.querySelector<HTMLButtonElement>(
        `#${CSS.escape(triggerId(openSection))}`,
      );
      setOpenSection(null);
      trigger?.focus();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [openSection, triggerId]);

  return { openSection, toggle, close, navRef, triggerId, panelId };
}
