"use client";

import type { ManualSectionBlock } from "@/lib/manual/types";

export function ManualToc({ blocks }: { blocks: ManualSectionBlock[] }) {
  if (blocks.length <= 1) return null;

  return (
    <aside className="hidden w-52 shrink-0 xl:block">
      <div className="sticky top-28">
        <p
          className="mb-3 text-[11px] font-bold uppercase tracking-[0.16em]"
          style={{ color: "var(--manual-brand)" }}
        >
          En este artículo
        </p>
        <ol className="space-y-2 list-none m-0 p-0 counter-reset-none">
          {blocks.map((b, i) => (
            <li key={b.id}>
              <a
                href={`#${b.id}`}
                className="block text-[13px] leading-snug transition-colors hover:underline"
                style={{ color: "var(--manual-text-muted)" }}
              >
                <span className="font-medium" style={{ color: "var(--manual-brand)" }}>
                  {i + 1}.
                </span>{" "}
                {b.title.replace(/^\d+\.\s*/, "")}
              </a>
            </li>
          ))}
        </ol>
      </div>
    </aside>
  );
}
