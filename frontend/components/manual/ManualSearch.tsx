"use client";

import { Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { searchManualArticles } from "@/lib/manual/catalog";

export function ManualSearch() {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  const hits = useMemo(
    () => (query.trim().length >= 2 ? searchManualArticles(query, 8) : []),
    [query],
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen(true);
        queueMicrotask(() => inputRef.current?.focus());
      }
      if (e.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  useEffect(() => {
    const onPointer = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onPointer);
    return () => document.removeEventListener("mousedown", onPointer);
  }, []);

  return (
    <div ref={rootRef} className="relative mx-auto w-full max-w-lg">
      <label className="sr-only" htmlFor="manual-search">
        Buscar en el manual
      </label>
      <div
        className="flex items-center gap-2.5 rounded-xl border px-4 py-3 shadow-sm"
        style={{
          borderColor: "var(--manual-border)",
          background: "#ffffff",
        }}
      >
        <Search className="h-4 w-4 shrink-0" style={{ color: "var(--manual-text-soft)" }} aria-hidden />
        <input
          ref={inputRef}
          id="manual-search"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          placeholder="Buscar en el manual..."
          className="min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-[#9aa6b4]"
          style={{ color: "var(--manual-text)" }}
          autoComplete="off"
        />
        <kbd
          className="hidden shrink-0 rounded-md border px-2 py-0.5 text-[10px] font-semibold sm:inline"
          style={{
            borderColor: "var(--manual-border)",
            color: "var(--manual-text-soft)",
            background: "var(--manual-bg)",
          }}
        >
          ⌘ K
        </kbd>
      </div>

      {open && query.trim().length >= 2 && (
        <ul
          className="absolute left-0 right-0 z-20 mt-2 max-h-80 overflow-y-auto rounded-xl border p-2 shadow-xl list-none m-0"
          style={{
            borderColor: "var(--manual-border)",
            background: "#ffffff",
          }}
          role="listbox"
        >
          {hits.length === 0 ? (
            <li className="px-3 py-2 text-sm" style={{ color: "var(--manual-text-muted)" }}>
              Sin resultados para «{query.trim()}»
            </li>
          ) : (
            hits.map((hit) => (
              <li key={hit.slug}>
                <button
                  type="button"
                  className="flex w-full flex-col rounded-lg px-3 py-2.5 text-left transition-colors hover:bg-[var(--manual-active-bg)]"
                  onClick={() => {
                    setOpen(false);
                    setQuery("");
                    router.push(hit.href);
                  }}
                >
                  <span className="text-sm font-semibold" style={{ color: "var(--manual-text)" }}>
                    {hit.title}
                  </span>
                  <span className="mt-0.5 text-xs" style={{ color: "var(--manual-text-soft)" }}>
                    Aplica para: {hit.audience} · {hit.summary}
                  </span>
                </button>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}
