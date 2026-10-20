"use client";

import { createElement, useEffect, useRef, useState } from "react";
import { ChevronDown, Menu, X } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { appIcon } from "@flit/shell/apps";
import { ThemeToggle } from "@flit/shell/theme";
import { PRODUCTS } from "@/lib/products";
import { StatusChip } from "@/components/ui/StatusChip";

const LINKS = [
  { href: "#por-que-flit", label: "Por qué FLIT" },
  { href: "#como-empezar", label: "Cómo empezar" },
  { href: "#nosotros", label: "Nosotros" },
  { href: "#contacto", label: "Contacto" },
];

/** Barra del landing: productos (menú desplegable), secciones, tema y el acceso a la suite. */
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
    "rounded-full px-3 py-2 text-sm font-medium text-[var(--nav-texto)] transition hover:text-flit-primary dark:text-white/75 dark:hover:text-white";

  return (
    <header className="sticky top-0 z-50 border-b border-[var(--color-flit-gray)]/80 bg-[var(--color-flit-bg)]/85 backdrop-blur-md dark:border-white/10 dark:bg-[#05060A]/85">
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
              <ChevronDown className={`h-4 w-4 transition ${productsOpen ? "rotate-180" : ""}`} aria-hidden="true" />
            </button>
            {productsOpen && (
              <div
                id="landing-productos"
                className="absolute left-0 top-full mt-3 w-[26rem] rounded-2xl border border-[var(--color-flit-gray)] bg-white p-2 shadow-[var(--nav-sombra-panel)] dark:border-white/10 dark:bg-[#0B0F14]"
              >
                {PRODUCTS.map((p) => (
                  <a
                    key={p.code}
                    href={`#producto-${p.code}`}
                    onClick={() => setProductsOpen(false)}
                    className="flex items-start gap-3 rounded-xl p-3 transition hover:bg-[var(--color-flit-bg)] dark:hover:bg-white/5"
                  >
                    <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-flit-brand/10 text-flit-brand">
                      {createElement(appIcon(p.icon), { className: "h-5 w-5", "aria-hidden": true })}
                    </span>
                    <span className="min-w-0">
                      <span className="flex items-center gap-2 text-sm font-semibold text-flit-primary dark:text-white">
                        {p.name}
                        <StatusChip status={p.status} />
                      </span>
                      <span className="mt-0.5 block text-xs leading-relaxed text-[var(--nav-texto)] dark:text-white/60">{p.tagline}</span>
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
            className="hidden rounded-full bg-flit-brand px-5 py-2 text-sm font-semibold text-white shadow-[var(--nav-sombra-activo)] transition hover:opacity-90 sm:inline-block"
          >
            Iniciar sesión
          </a>
          <button
            type="button"
            className="grid h-9 w-9 place-items-center rounded-full text-flit-primary hover:bg-black/5 lg:hidden dark:text-white dark:hover:bg-white/10"
            aria-label={mobileOpen ? "Cerrar menú" : "Abrir menú"}
            aria-expanded={mobileOpen}
            aria-controls="landing-menu-movil"
            onClick={() => setMobileOpen((v) => !v)}
          >
            {mobileOpen ? <X className="h-5 w-5" aria-hidden="true" /> : <Menu className="h-5 w-5" aria-hidden="true" />}
          </button>
        </div>
      </nav>

      {mobileOpen && (
        <div id="landing-menu-movil" className="border-t border-[var(--color-flit-gray)] px-4 pb-4 pt-2 lg:hidden dark:border-white/10">
          <p className="px-3 pt-2 text-xs font-semibold uppercase tracking-wide text-[var(--nav-texto)] dark:text-white/50">Productos</p>
          {PRODUCTS.map((p) => (
            <a key={p.code} href={`#producto-${p.code}`} onClick={() => setMobileOpen(false)} className={`${linkClass} flex items-center gap-2`}>
              {p.name}
              <StatusChip status={p.status} />
            </a>
          ))}
          <div className="my-2 h-px bg-[var(--color-flit-gray)] dark:bg-white/10" />
          {LINKS.map((l) => (
            <a key={l.href} href={l.href} onClick={() => setMobileOpen(false)} className={`${linkClass} block`}>
              {l.label}
            </a>
          ))}
          <a href={loginUrl} className="mt-3 block rounded-full bg-flit-brand px-5 py-2.5 text-center text-sm font-semibold text-white">
            Iniciar sesión
          </a>
        </div>
      )}
    </header>
  );
}
