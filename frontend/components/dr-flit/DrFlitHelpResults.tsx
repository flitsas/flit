"use client";

import { BookOpen } from "lucide-react";
import type { DrFlitHelpResult } from "./dr-flit-types";

export function DrFlitHelpResults({
  results,
  onOpen,
}: {
  results: DrFlitHelpResult[];
  onOpen: (href: string) => void;
}) {
  if (results.length === 0) return null;

  return (
    <ul className="flex flex-col gap-2 list-none m-0 p-0" aria-label="Artículos del manual">
      {results.map((item) => (
        <li key={item.slug}>
          <button
            type="button"
            onClick={() => onOpen(item.href)}
            className="flex w-full items-start gap-3 rounded-[var(--dr-flit-radius-card)] border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
            style={{
              borderColor: "var(--dr-flit-border)",
              background: "var(--dr-flit-card-bg)",
              boxShadow: "var(--dr-flit-shadow-card)",
            }}
          >
            <span
              className="grid h-10 w-10 shrink-0 place-items-center rounded-full"
              style={{ background: "var(--dr-flit-icon-tint)" }}
              aria-hidden="true"
            >
              <BookOpen
                className="h-5 w-5"
                style={{ color: "var(--dr-flit-brand-blue)" }}
              />
            </span>
            <span className="min-w-0 flex-1">
              <span
                className="block text-sm font-semibold"
                style={{ color: "var(--dr-flit-brand-title)" }}
              >
                {item.title}
              </span>
              <span
                className="mt-0.5 block text-[11px] font-medium"
                style={{ color: "var(--dr-flit-brand-blue)" }}
              >
                Aplica para: {item.audience}
              </span>
              <span
                className="mt-1 block text-xs"
                style={{ color: "var(--dr-flit-text-secondary)" }}
              >
                {item.summary}
              </span>
              <span
                className="mt-2 block text-xs font-semibold"
                style={{ color: "var(--dr-flit-brand)" }}
              >
                Abrir en documentación →
              </span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}
