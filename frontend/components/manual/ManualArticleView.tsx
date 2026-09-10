import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { ManualArticle } from "@/lib/manual/catalog";
import { getAdjacentArticles } from "@/lib/manual/catalog";
import { ManualCalloutBox } from "./ManualCallout";

export function ManualArticleView({ article }: { article: ManualArticle }) {
  const { prev, next } = getAdjacentArticles(article.slug);

  return (
    <article
      className="rounded-[var(--manual-radius-card)] border p-6 sm:p-8 lg:p-10"
      style={{
        background: "var(--manual-card-bg)",
        borderColor: "var(--manual-border-soft)",
        boxShadow: "var(--manual-shadow)",
      }}
    >
      <p
        className="mb-4 inline-flex rounded-full px-3 py-1 text-xs font-semibold"
        style={{
          background: "var(--manual-badge-bg)",
          color: "var(--manual-badge-text)",
        }}
      >
        Aplica para: {article.audience}
      </p>

      <h1
        className="text-2xl font-bold tracking-tight sm:text-3xl lg:text-[2rem] lg:leading-tight"
        style={{ color: "var(--manual-heading)" }}
      >
        {article.title}
      </h1>
      <p className="mt-3 text-base leading-relaxed" style={{ color: "var(--manual-text-muted)" }}>
        {article.summary}
      </p>

      <div className="mt-10 space-y-10">
        {article.blocks.map((block) => (
          <section key={block.id} id={block.id} className="scroll-mt-28">
            <h2
              className="text-lg font-bold sm:text-xl"
              style={{ color: "var(--manual-heading)" }}
            >
              {block.title}
            </h2>
            {block.paragraphs.map((p) => (
              <p
                key={p.slice(0, 40)}
                className="mt-3 text-[15px] leading-7"
                style={{ color: "var(--manual-text)" }}
              >
                {p}
              </p>
            ))}
            {block.bullets && block.bullets.length > 0 && (
              <ul
                className="mt-4 space-y-2.5 list-disc pl-5 text-[15px] leading-7"
                style={{ color: "var(--manual-text)" }}
              >
                {block.bullets.map((b) => (
                  <li key={b.slice(0, 40)}>{b}</li>
                ))}
              </ul>
            )}
            {block.callouts?.map((c) => (
              <ManualCalloutBox key={c.text.slice(0, 32)} callout={c} />
            ))}
          </section>
        ))}
      </div>

      <nav
        className="mt-12 grid gap-3 border-t pt-8 sm:grid-cols-2"
        style={{ borderColor: "var(--manual-border)" }}
        aria-label="Artículos adyacentes"
      >
        {prev ? (
          <Link
            href={`/manual/${prev.slug}`}
            className="group flex flex-col rounded-xl border p-4 transition-colors hover:border-[var(--manual-tech)]"
            style={{ borderColor: "var(--manual-border)", background: "var(--manual-bg)" }}
          >
            <span
              className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide"
              style={{ color: "var(--manual-text-soft)" }}
            >
              <ChevronLeft className="h-4 w-4" aria-hidden />
              Anterior tema
            </span>
            <span
              className="mt-2 text-sm font-semibold group-hover:underline"
              style={{ color: "var(--manual-text)" }}
            >
              {prev.title}
            </span>
          </Link>
        ) : (
          <span />
        )}
        {next ? (
          <Link
            href={`/manual/${next.slug}`}
            className="group flex flex-col rounded-xl border p-4 text-right transition-colors hover:border-[var(--manual-tech)] sm:justify-self-end"
            style={{ borderColor: "var(--manual-border)", background: "var(--manual-bg)" }}
          >
            <span
              className="flex items-center justify-end gap-1 text-xs font-semibold uppercase tracking-wide"
              style={{ color: "var(--manual-text-soft)" }}
            >
              Siguiente tema
              <ChevronRight className="h-4 w-4" aria-hidden />
            </span>
            <span
              className="mt-2 text-sm font-semibold group-hover:underline"
              style={{ color: "var(--manual-text)" }}
            >
              {next.title}
            </span>
          </Link>
        ) : null}
      </nav>
    </article>
  );
}
