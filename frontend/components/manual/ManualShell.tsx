"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useMemo, useState } from "react";
import { ChevronDown, FileText, FolderOpen, Menu, X } from "lucide-react";
import { getNavTree, type ManualArticle } from "@/lib/manual/catalog";
import { MANUAL_VERSION, MANUAL_VERSION_DATE } from "@/lib/manual/articles/meta";
import { ManualSearch } from "./ManualSearch";
import { ManualToc } from "./ManualToc";

function ManualLogo() {
  return (
    <Link href="/manual" className="flex items-center gap-2.5 px-5 py-5">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src="/assets/favicon.svg"
        alt=""
        aria-hidden
        className="h-10 w-10 shrink-0 rounded-xl shadow-sm"
        draggable={false}
      />
      <span className="leading-tight">
        <span className="block text-base font-bold tracking-tight" style={{ color: "var(--manual-text)" }}>
          FLIT
        </span>
        <span className="block text-[11px] font-semibold uppercase tracking-[0.2em]" style={{ color: "var(--manual-brand)" }}>
          Docs
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
              className="flex w-full items-center gap-2 rounded-lg px-2 py-2.5 text-left text-[11px] font-bold uppercase tracking-[0.14em] transition-colors hover:bg-[var(--manual-border-soft)]"
              style={{ color: "var(--manual-text-muted)" }}
              onClick={() => setOpenSections((s) => ({ ...s, [section.id]: !open }))}
              aria-expanded={open}
            >
              <FolderOpen className="h-4 w-4 shrink-0 opacity-70" aria-hidden />
              <span className="flex-1">{section.label}</span>
              <ChevronDown
                className={`h-4 w-4 shrink-0 opacity-60 transition-transform ${open ? "" : "-rotate-90"}`}
                aria-hidden
              />
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
                        className="flex items-start gap-2 rounded-lg px-2.5 py-2 text-[13px] leading-snug transition-colors"
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
                        <FileText
                          className="mt-0.5 h-3.5 w-3.5 shrink-0 opacity-60"
                          aria-hidden
                        />
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
      {/* Mobile overlay */}
      {mobileOpen && (
        <button
          type="button"
          className="fixed inset-0 z-40 bg-black/40 lg:hidden"
          aria-label="Cerrar menú"
          onClick={() => setMobileOpen(false)}
        />
      )}

      <div className="flex min-h-screen items-start">
        {/* Sidebar — altura fija al viewport; no estirar con el contenido del artículo */}
        <aside
          className={`fixed inset-y-0 left-0 z-50 flex h-dvh max-h-dvh w-[280px] shrink-0 flex-col border-r transition-transform lg:sticky lg:top-0 lg:z-auto lg:max-h-dvh lg:translate-x-0 ${
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
              className="m-4 rounded-lg p-2 lg:hidden"
              onClick={() => setMobileOpen(false)}
              aria-label="Cerrar menú lateral"
            >
              <X className="h-5 w-5" style={{ color: "var(--manual-text)" }} />
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
            style={{ borderColor: "var(--manual-border)", color: "var(--manual-text-soft)" }}
          >
            <p className="font-medium">Versión {MANUAL_VERSION}</p>
            <p>{MANUAL_VERSION_DATE}</p>
          </div>
        </aside>

        {/* Main column */}
        <div className="flex min-w-0 flex-1 flex-col">
          <header
            className="sticky top-0 z-30 flex items-center gap-3 border-b px-4 py-4 backdrop-blur-md lg:px-8"
            style={{
              background: "rgba(238, 245, 255, 0.92)",
              borderColor: "var(--manual-border)",
            }}
          >
            <button
              type="button"
              className="rounded-lg border p-2 lg:hidden"
              style={{ borderColor: "var(--manual-border)", background: "white" }}
              onClick={() => setMobileOpen(true)}
              aria-label="Abrir menú"
            >
              <Menu className="h-5 w-5" style={{ color: "var(--manual-text)" }} />
            </button>
            <div className="min-w-0 flex-1">
              <ManualSearch />
            </div>
          </header>

          <div className="flex min-w-0 flex-1 justify-center gap-6 px-4 py-8 lg:px-8">
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
