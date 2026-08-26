import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ManualShell } from "@/components/manual/ManualShell";
import { ManualArticleView } from "@/components/manual/ManualArticleView";
import { getArticleBySlug, MANUAL_ARTICLES } from "@/lib/manual/catalog";

type PageProps = {
  params: Promise<{ slug: string[] }>;
};

export function generateStaticParams() {
  return MANUAL_ARTICLES.map((a) => ({ slug: a.slug.split("/") }));
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { slug } = await params;
  const article = getArticleBySlug(slug.join("/"));
  if (!article) return { title: "Manual FLIT" };
  return {
    title: `${article.title} · Manual FLIT`,
    description: article.summary,
  };
}

export default async function ManualArticlePage({ params }: PageProps) {
  const { slug } = await params;
  const article = getArticleBySlug(slug.join("/"));
  if (!article) notFound();

  return (
    <ManualShell article={article}>
      <ManualArticleView article={article} />
    </ManualShell>
  );
}
