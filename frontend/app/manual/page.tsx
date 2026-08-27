import type { Metadata } from "next";
import { ManualShell } from "@/components/manual/ManualShell";
import { ManualArticleView } from "@/components/manual/ManualArticleView";
import { getArticleBySlug, MANUAL_HOME_SLUG } from "@/lib/manual/catalog";

export const metadata: Metadata = {
  title: "Centro de Ayuda FLIT",
  description: "Manual operativo para Gestor y Organismo de Tránsito",
};

export default function ManualHomePage() {
  const article = getArticleBySlug(MANUAL_HOME_SLUG);
  if (!article) {
    return (
      <ManualShell>
        <p>No se encontró el artículo de bienvenida.</p>
      </ManualShell>
    );
  }
  return (
    <ManualShell article={article}>
      <ManualArticleView article={article} />
    </ManualShell>
  );
}
