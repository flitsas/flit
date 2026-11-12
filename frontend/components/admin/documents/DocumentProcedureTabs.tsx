"use client";

import { useState, type ComponentType } from "react";
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";
import { FileStack } from "lucide-react";
import { iconoComo } from "@flit/ui/Icono";
import { RequirementsTab } from "@/components/admin/documents/tabs/RequirementsTab";
import { OtOverridesTab } from "@/components/admin/documents/tabs/OtOverridesTab";
import { MatrixPreviewTab } from "@/components/admin/documents/tabs/MatrixPreviewTab";

// Contenedor multi-pestaña de la consola documental por trámite (HU #10198,
// AC2–AC5). Clona el patrón de CompanyConfigTabs (tablist/tab/tabpanel WCAG). Cada
// pestaña gestiona su propia carga y sus 4 estados UI (AC7).
type TabId = "documentos" | "ot" | "matriz";

const TABS: { id: TabId; label: string; icon: ComponentType<{ className?: string }>; description: string }[] = [
  {
    id: "documentos",
    label: "Documentos",
    icon: FileStack,
    description:
      "Define qué documentos exige este trámite y cuáles son obligatorios. El orden lo define exclusivamente la pestaña «Overrides OT».",
  },
  {
    id: "ot",
    label: "Overrides OT",
    icon: iconoComo("empresas"),
    description:
      "Selecciona el organismo de tránsito y define el orden de los documentos: es el único nivel que ordena (RF22).",
  },
  {
    id: "matriz",
    label: "Matriz resuelta",
    icon: iconoComo("reporte-detallado"),
    description:
      "Previsualiza el orden final que verá un trámite combinando la precedencia OT › Default.",
  },
];

export function DocumentProcedureTabs({ procedureTypeId }: { procedureTypeId: string }) {
  const [tab, setTab] = useState<TabId>("documentos");
  const activeTab = TABS.find((t) => t.id === tab);

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className={PESTANAS_LISTA_CLS} role="tablist">
        {TABS.map((t) => {
          const Icon = t.icon;
          const active = tab === t.id;
          return (
            <button
              key={t.id}
              role="tab"
              id={`tab-${t.id}`}
              aria-selected={active}
              aria-controls={`tabpanel-${t.id}`}
              onClick={() => setTab(t.id)}
              className={pestanaClass(active, "relative shrink-0 gap-2")}
            >
              <Icon className="h-3.5 w-3.5" />
              {t.label}
            </button>
          );
        })}
      </div>

      {activeTab && (
        <p className="text-xs opacity-60">{activeTab.description}</p>
      )}

      <div role="tabpanel" id={`tabpanel-${tab}`} aria-labelledby={`tab-${tab}`} className="flex-1">
        {tab === "documentos" && <RequirementsTab procedureTypeId={procedureTypeId} />}
        {tab === "ot" && <OtOverridesTab procedureTypeId={procedureTypeId} />}
        {tab === "matriz" && <MatrixPreviewTab procedureTypeId={procedureTypeId} />}
      </div>
    </div>
  );
}
