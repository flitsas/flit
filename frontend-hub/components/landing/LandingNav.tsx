"use client";

import { useEffect, useRef, useState } from "react";
import { Menu } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { ThemeToggle } from "@flit/shell/theme";
import { PRODUCTS } from "@/lib/products";
import { StatusChip } from "@/components/ui/StatusChip";
import { ProductArt } from "@/components/ui/ProductIcon";
import { buttonClass } from "@flit/ui/Button";

const LINKS = [
  { href: "#por-que-flit", label: "Por qué FLIT" },
  { href: "#como-empezar", label: "Cómo empezar" },
  { href: "#nosotros", label: "Nosotros" },
  { href: "#contacto", label: "Contacto" },
];

/**
 * Barra del landing: productos (menú desplegable), secciones, tema y el acceso a la suite. Con el aspecto de la barra de
 * la propuesta (HU #14487): el menú de productos lleva la ilustración de cada uno, como `.suite-menu-productos`.
 */
export function LandingNav({ loginUrl }: { loginUrl: string }) {
  const [productsOpen, setProductsOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const productsRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!productsOpen) return;
    const onPointer = (e: PointerEvent) => {
      if (!productsRef.current?.contains(e.target as Node)) setProductsOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setProductsOpen(false);
    window.addEventListener("pointerdown", onPointer);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("pointerdown", onPointer);
      window.removeEventListener("keydown", onKey);
    };
  }, [productsOpen]);

  const linkClass =
    "rounded-full px-3 py-2 text-sm font-medium text-[color:var(--di-text-secondary)] transition hover:bg-[color:var(--di-accent-soft)] hover:text-[color:var(--di-action)]";

  return (
    <header className="sticky top-0 z-50 border-b border-[color:var(--di-divider)] bg-[color:color-mix(in_srgb,var(--di-surface-elevated)_88%,transparent)] backdrop-blur-md">
      <nav aria-label="Principal" className="mx-auto flex h-16 max-w-7xl items-center gap-4 px-4 md:px-6">
        <a href="#inicio" aria-label="FLIT, inicio" className="shrink-0">
          <BrandLogo variant="dark" className="h-9 w-auto dark:hidden" />
          <BrandLogo variant="white" className="hidden h-9 w-auto dark:block" />
        </a>

        <div className="ml-6 hidden items-center gap-1 lg:flex">
          <div ref={productsRef} className="relative">
            <button
              type="button"
              aria-expanded={productsOpen}
              aria-controls="landing-productos"
              onClick={() => setProductsOpen((v) => !v)}
              className={`${linkClass} inline-flex items-center gap-1`}
            >
              Productos
              <Icono nombre="abajo" className={`h-4 w-4 transition ${productsOpen ? "rotate-180" : ""}`} />
            </button>
            {productsOpen && (
              <div
                id="landing-productos"
                className="absolute left-0 top-full mt-3 w-[26rem] rounded-2xl border border-[color:var(--di-border)] bg-[color:var(--di-surface-elevated)] p-2 shadow-[var(--shadow-modal)]"
              >
                {PRODUCTS.map((p) => (
                  <a
                    key={p.code}
                    href={`#producto-${p.code}`}
                    onClick={() => setProductsOpen(false)}
                    className="flex items-center gap-3 rounded-xl px-3 py-2 transition hover:bg-[color:var(--di-accent-soft)]"
                  >
                    <ProductArt code={p.code} icon={p.icon} size="sm" muted={p.status === "soon"} />
                    <span className="min-w-0">
                      <span className="flex items-center gap-2 text-sm font-semibold text-[color:var(--di-heading)]">
                        {p.name}
                        <StatusChip status={p.status} />
                      </span>
                      <span className="mt-0.5 block text-xs leading-relaxed text-[color:var(--di-text-tertiary)]">{p.tagline}</span>
                    </span>
                  </a>
                ))}
              </div>
            )}
          </div>
          {LINKS.map((l) => (
            <a key={l.href} href={l.href} className={linkClass}>
              {l.label}
            </a>
          ))}
        </div>

        <div className="ml-auto flex items-center gap-3">
          <ThemeToggle />
          <a
            href={loginUrl}
            className={buttonClass({ size: "sm", className: "hidden px-5 sm:inline-flex" })}
          >
            Iniciar sesión
          </a>
          <button
            type="button"
            className="grid h-10 w-10 place-items-center rounded-full text-[color:var(--di-heading)] hover:bg-[color:var(--di-accent-soft)] lg:hidden"
            aria-label={mobileOpen ? "Cerrar menú" : "Abrir menú"}
            aria-expanded={mobileOpen}
            aria-controls="landing-menu-movil"
            onClick={() => setMobileOpen((v) => !v)}
          >
            {mobileOpen ? <Icono nombre="cerrar" className="h-5 w-5" /> : <Menu className="h-5 w-5" aria-hidden="true" />}
          </button>
        </div>
      </nav>

      {mobileOpen && (
        <div id="landing-menu-movil" className="border-t border-[color:var(--di-divider)] px-4 pb-4 pt-2 lg:hidden">
          <p className="px-3 pt-2 text-xs font-semibold uppercase tracking-wide text-[color:var(--di-text-tertiary)]">Productos</p>
          {PRODUCTS.map((p) => (
            <a key={p.code} href={`#producto-${p.code}`} onClick={() => setMobileOpen(false)} className={`${linkClass} flex items-center gap-2`}>
              <ProductArt code={p.code} icon={p.icon} size="sm" muted={p.status === "soon"} />
              {p.name}
              <StatusChip status={p.status} />
            </a>
          ))}
          <div className="my-2 h-px bg-[color:var(--di-divider)]" />
          {LINKS.map((l) => (
            <a key={l.href} href={l.href} onClick={() => setMobileOpen(false)} className={`${linkClass} block`}>
              {l.label}
            </a>
          ))}
          <a href={loginUrl} className={buttonClass({ className: "mt-3 w-full" })}>
            Iniciar sesión
          </a>
        </div>
      )}
    </header>
  );
}
