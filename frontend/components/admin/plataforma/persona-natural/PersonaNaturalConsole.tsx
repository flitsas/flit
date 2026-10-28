"use client";

// HU #14074 a #14076 (Épica #14021) — Consola de Plataforma > Persona Natural: pestañas dentro de una sola ruta
// (`/admin/plataforma/persona-natural`). Cada pestaña se monta la primera vez que se abre (no pide su API hasta
// entonces) y se conserva montada, así una tabla recuerda su búsqueda y su página al volver a ella.
import { useCallback, useState } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { OrganismosYTiposTab } from "./OrganismosYTiposTab";
import { ParametrosTab } from "./ParametrosTab";
import { TarifasTab } from "./TarifasTab";

type TabId = "tarifas" | "organismos" | "parametros";

export function PersonaNaturalConsole() {
  const [active, setActive] = useState<TabId>("tarifas");
  const [visited, setVisited] = useState<ReadonlySet<TabId>>(() => new Set<TabId>(["tarifas"]));

  const onChange = useCallback((id: TabId) => {
    setActive(id);
    setVisited((prev) => (prev.has(id) ? prev : new Set(prev).add(id)));
  }, []);

  const pestanas: { id: TabId; label: string; render: () => React.ReactNode }[] = [
    { id: "tarifas", label: "Tarifas", render: () => <TarifasTab /> },
    {
      id: "organismos",
      label: "Organismos y tipos",
      render: () => <OrganismosYTiposTab onIrATarifas={() => onChange("tarifas")} />,
    },
    { id: "parametros", label: "Parámetros", render: () => <ParametrosTab /> },
  ];

  const tabs: SectionTab<TabId>[] = pestanas.map((t) => ({
    id: t.id,
    label: t.label,
    content: visited.has(t.id) ? t.render() : null,
  }));

  return <SectionTabs tabs={tabs} active={active} onChange={onChange} ariaLabel="Secciones de Persona Natural" />;
}
