"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useMemo, useState } from "react";
import { Icono, IconoModulo } from "@flit/ui/Icono";
import { getNavTree, type ManualArticle } from "@/lib/manual/catalog";
import { MANUAL_VERSION, MANUAL_VERSION_DATE } from "@/lib/manual/articles/meta";
import { ManualSearch } from "./ManualSearch";
import { ManualToc } from "./ManualToc";
import { MODAL_VELO_CLS } from "@flit/ui/Modal";

// Homologación (HU #14511): deja el logo «FLIT Docs» y usa el acceso de la vista «Ayuda» de la propuesta (`vAyuda`).
function ManualLogo() {
  return (
    <Link
      href="/manual"
      className="m-3 flex min-h-[72px] items-center gap-3.5 rounded-xl px-3 py-3 transition-colors hover:bg-[var(--manual-active-bg)]"
    >
      <IconoModulo clave="ayuda" disco size={40} className="shrink-0" />
      <span className="leading-tight">
        <span className="block text-base font-bold" style={{ color: "var(--manual-heading)" }}>
          Ayuda
        </span>
        <span className="block text-xs" style={{ color: "var(--manual-text-soft)" }}>
          Manual de usuario
        </span>
      </span>
    </Link>
  );
}

function SidebarNav({
  pathname,
  openSections,
  setOpenSections,
  onNavigate,
}: {
  pathname: string;
  openSections: Record<string, boolean>;
  setOpenSections: React.Dispatch<React.SetStateAction<Record<string, boolean>>>;
  onNavigate?: () => void;
}) {
  const tree = useMemo(() => getNavTree(), []);

  return (
    <nav className="min-h-0 flex-1 overflow-y-auto px-3 pb-4" aria-label="Manual">
      {tree.map(({ section, articles }) => {
        const open = openSections[section.id] ?? true;
        return (
          <div key={section.id} className="mb-1">
            <button
              type="button"
              className="flex w-full items-center gap-2 rounded-xl px-2 py-2.5 text-left text-[11px] font-bold uppercase tracking-[0.14em] transition-colors hover:bg-[var(--manual-active-bg)]"
              style={{ color: "var(--manual-text-muted)" }}
              onClick={() => setOpenSections((s) => ({ ...s, [section.id]: !open }))}
              aria-expanded={open}
            >
              <Icono nombre="carpeta" className="h-4 w-4 shrink-0 opacity-70" />
              <span className="flex-1">{section.label}</span>
              <Icono nombre="abajo" className={`h-4 w-4 shrink-0 opacity-60 transition-transform ${open ? "" : "-rotate-90"}`} />
            </button>
            {open && (
              <ul className="mt-0.5 space-y-0.5 list-none m-0 p-0 pl-1">
                {articles.map((a) => {
                  const href = `/manual/${a.slug}`;
                  const active = pathname === href;
                  return (
                    <li key={a.slug}>
                      <Link
                        href={href}
                        onClick={onNavigate}
                        className="flex items-start gap-2 rounded-xl px-2.5 py-2 text-[13px] leading-snug transition-colors hover:bg-[var(--manual-active-bg)]"
                        style={
                          active
                            ? {
                                background: "var(--manual-active-bg)",
                                color: "var(--manual-active-text)",
                                fontWeight: 600,
                              }
                            : { color: "var(--manual-text)" }
                        }
                      >
                        <Icono nombre="documento" className="mt-0.5 h-3.5 w-3.5 shrink-0 opacity-60" />
                        <span>{a.title}</span>
                      </Link>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        );
      })}
    </nav>
  );
}

export function ManualShell({
  children,
  article,
}: {
  children: React.ReactNode;
  article?: ManualArticle | null;
}) {
  const pathname = usePathname();
  const [openSections, setOpenSections] = useState<Record<string, boolean>>(() => {
    const initial: Record<string, boolean> = {};
    for (const { section } of getNavTree()) initial[section.id] = true;
    return initial;
  });
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <div className="manual-root min-h-screen" style={{ background: "var(--manual-bg)" }}>
      {/* Mobile overlay: velo del Modal */}
      {mobileOpen && (
        <button
          type="button"
          className={`fixed inset-0 z-40 lg:hidden ${MODAL_VELO_CLS}`}
          aria-label="Cerrar menú"
          onClick={() => setMobileOpen(false)}
        />
      )}

      <div className="mx-auto flex min-h-screen max-w-[1440px] items-start gap-6 lg:px-8 lg:py-6">
        {/* Lista de guías: tarjeta fija al viewport en escritorio; cajón con radio y sombra del Modal en celular. */}
        <aside
          className={`fixed inset-y-0 left-0 z-50 flex h-dvh max-h-dvh w-[300px] max-w-[calc(100vw-2rem)] shrink-0 flex-col overflow-clip rounded-r-[var(--di-radius-modal)] border-r shadow-[var(--shadow-modal)] transition-transform lg:sticky lg:top-6 lg:z-auto lg:h-[calc(100dvh-3rem)] lg:max-h-[calc(100dvh-3rem)] lg:translate-x-0 lg:rounded-[var(--manual-radius-card)] lg:border lg:shadow-[var(--manual-shadow)] ${
            mobileOpen ? "translate-x-0" : "-translate-x-full lg:translate-x-0"
          }`}
          style={{
            background: "var(--manual-sidebar-bg)",
            borderColor: "var(--manual-border)",
          }}
        >
          <div className="flex shrink-0 items-start justify-between lg:block">
            <ManualLogo />
            <button
              type="button"
              className="m-4 grid h-10 w-10 place-items-center rounded-full transition-colors hover:bg-[var(--manual-active-bg)] lg:hidden"
              onClick={() => setMobileOpen(false)}
              aria-label="Cerrar menú lateral"
            >
              <Icono nombre="cerrar" className="h-5 w-5" style={{ color: "var(--manual-text)" }} />
            </button>
          </div>

          <SidebarNav
            pathname={pathname}
            openSections={openSections}
            setOpenSections={setOpenSections}
            onNavigate={() => setMobileOpen(false)}
          />

          <div
            className="mt-auto shrink-0 border-t px-5 py-4 text-[11px] leading-relaxed"
            style={{ borderColor: "var(--manual-border-soft)", color: "var(--manual-text-soft)" }}
          >
            <p className="font-medium">Versión {MANUAL_VERSION}</p>
            <p>{MANUAL_VERSION_DATE}</p>
          </div>
        </aside>

        {/* Main column */}
        <div className="flex min-w-0 flex-1 flex-col gap-5 px-4 py-4 lg:px-0 lg:py-0">
          {/* Buscador en tarjeta (`section.card` de la vista «Ayuda»). */}
          <header
            className="sticky top-4 z-30 flex items-center gap-3 rounded-[var(--manual-radius-card)] border p-3 sm:p-4"
            style={{
              background: "var(--manual-card-bg)",
              borderColor: "var(--manual-border)",
              boxShadow: "var(--manual-shadow)",
            }}
          >
            <button
              type="button"
              className="grid h-11 w-11 shrink-0 place-items-center rounded-xl border lg:hidden"
              style={{ borderColor: "var(--di-border-control)", background: "var(--manual-card-bg)" }}
              onClick={() => setMobileOpen(true)}
              aria-label="Abrir menú"
            >
              <Icono nombre="lista" className="h-5 w-5" style={{ color: "var(--manual-text)" }} />
            </button>
            <div className="min-w-0 flex-1">
              <ManualSearch />
            </div>
          </header>

          <div className="flex min-w-0 flex-1 justify-center gap-6">
            <main className="min-w-0 w-full max-w-3xl">{children}</main>
            {article && article.blocks.length > 0 && (
              <ManualToc blocks={article.blocks} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
