export type ManualAudience = "Todos" | "Gestor" | "Organismo de Tránsito";

export type ManualCallout = {
  variant: "info" | "tip" | "warning";
  title?: string;
  text: string;
};

export type ManualSectionBlock = {
  id: string;
  title: string;
  paragraphs: string[];
  bullets?: string[];
  callouts?: ManualCallout[];
};

export type ManualArticle = {
  slug: string;
  title: string;
  audience: ManualAudience;
  sectionId: string;
  keywords: string[];
  summary: string;
  blocks: ManualSectionBlock[];
};

export type ManualNavSection = {
  id: string;
  label: string;
  order: number;
};
