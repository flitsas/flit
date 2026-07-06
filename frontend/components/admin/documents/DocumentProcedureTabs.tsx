"use client";

import { useState } from "react";
import { FileStack, Building2, Users, Table2 } from "lucide-react";
import { RequirementsTab } from "@/components/admin/documents/tabs/RequirementsTab";
import { OtOverridesTab } from "@/components/admin/documents/tabs/OtOverridesTab";
import { ClienteOverridesTab } from "@/components/admin/documents/tabs/ClienteOverridesTab";
import { MatrixPreviewTab } from "@/components/admin/documents/tabs/MatrixPreviewTab";

// Contenedor multi-pestaña de la consola documental por trámite (HU #10198,
// AC2–AC5). Clona el patrón de CompanyConfigTabs (tablist/tab/tabpanel WCAG). Cada
// pestaña gestiona su propia carga y sus 4 estados UI (AC7).
type TabId = "documentos" | "ot" | "cliente" | "matriz";

const TABS: { id: TabId; label: string; icon: typeof FileStack; description: string }[] = [
  {
    id: "documentos",
    label: "Documentos",
    icon: FileStack,
    description:
      "Define qué documentos exige este trámite y en qué orden por defecto, marcando cuáles son obligatorios.",
  },
  {
    id: "ot",
    label: "Overrides OT",
    icon: Building2,
    description:
      "Ajusta el orden de los documentos para un organismo de tránsito específico, sin alterar el orden por defecto.",
  },
  {
    id: "cliente",
    label: "Overrides Cliente",
    icon: Users,
    description:
      "Ajusta el orden de los documentos para un cliente puntual. Tiene mayor precedencia que el override de OT.",
  },
  {
    id: "matriz",
    label: "Matriz resuelta",
    icon: Table2,
    description:
      "Previsualiza el orden final que verá un trámite combinando la precedencia Cliente › OT › Default.",
  },
];

export function DocumentProcedureTabs({ procedureTypeId }: { procedureTypeId: string }) {
  const [tab, setTab] = useState<TabId>("documentos");
  const activeTab = TABS.find((t) => t.id === tab);

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className="flex items-center gap-1 overflow-x-auto border-b" role="tablist">
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
              className="relative flex shrink-0 items-center gap-2 px-4 py-2.5 text-xs font-semibold transition"
              style={{ color: active ? "#557EFF" : undefined, opacity: active ? 1 : 0.65 }}
            >
              <Icon className="h-3.5 w-3.5" />
              {t.label}
              {active && (
                <span className="absolute right-2 left-2 -bottom-px h-0.5 rounded-full" style={{ background: "#557EFF" }} />
              )}
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
        {tab === "cliente" && <ClienteOverridesTab procedureTypeId={procedureTypeId} />}
        {tab === "matriz" && <MatrixPreviewTab procedureTypeId={procedureTypeId} />}
      </div>
    </div>
  );
}
