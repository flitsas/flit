"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { OtTabBar } from "@/components/admin/transit-offices/OtTabBar";
import {
  OT_HUB_TABS,
  otHubListPath,
  otHubModulePath,
  type OtHubTabId,
} from "@/components/admin/transit-offices/ot-nav";

export interface OtHubLayoutProps {
  transitOfficeId: string;
  activeTab: OtHubTabId;
  moduleTitle: string;
  children: ReactNode;
}

/** Layout compartido del hub OT con navegación por pestañas (HU #10236). */
export function OtHubLayout({
  transitOfficeId,
  activeTab,
  moduleTitle,
  children,
}: OtHubLayoutProps) {
  const router = useRouter();
  const [officeLabel, setOfficeLabel] = useState<string | undefined>();

  useEffect(() => {
    const controller = new AbortController();
    void fetchOtProfile(controller.signal)
      .then((profile) => {
        if (controller.signal.aborted) {
          return;
        }
        if (profile.transitOfficeId && profile.transitOfficeId !== transitOfficeId) {
          router.replace(otHubModulePath(profile.transitOfficeId, activeTab));
        }
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [activeTab, router, transitOfficeId]);

  useEffect(() => {
    const controller = new AbortController();
    void fetchTransitOffices(undefined, controller.signal)
      .then((catalog) => {
        if (controller.signal.aborted) {
          return;
        }
        const office = catalog.find((o) => o.id === transitOfficeId);
        if (office) {
          setOfficeLabel(`${office.name} (${office.code})`);
        }
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [transitOfficeId]);

  const subtitle = officeLabel;

  return (
    <main className="app-bg min-h-screen px-4 py-6 md:px-8">
      <button
        type="button"
        onClick={() => router.push(otHubListPath())}
        className="mb-4 flex items-center gap-2 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        Volver al listado de OT
      </button>

      <ModuleTitle title={moduleTitle} subtitle={subtitle} />

      <div className="mt-4 rounded-2xl border bg-white p-4 md:p-6" style={{ borderColor: "#DFE5ED" }}>
        <OtTabBar
          tabs={OT_HUB_TABS.map((t) => ({ id: t.id, label: t.label }))}
          activeId={activeTab}
          onChange={(tabId) =>
            router.push(otHubModulePath(transitOfficeId, tabId as OtHubTabId))
          }
          ariaLabel="Módulos de administración OT"
        />
        <div className="mt-6">{children}</div>
      </div>
    </main>
  );
}
