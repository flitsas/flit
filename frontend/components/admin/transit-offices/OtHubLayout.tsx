"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin, isSuperAdmin } from "@/lib/auth/jwt";
import { OtTabBar } from "@/components/admin/transit-offices/OtTabBar";
import {
  OT_HUB_TABS,
  otHubListPath,
  otHubModulePath,
  rememberOtTransitOfficeId,
  type OtHubTabId,
} from "@/components/admin/transit-offices/ot-nav";

export interface OtHubLayoutProps {
  transitOfficeId: string;
  activeTab: OtHubTabId;
  moduleTitle: string;
  children: ReactNode;
}

/** Layout compartido del hub OT (HU #10236 / #11225). Pestañas solo SuperAdmin; Admin OT navega por dock. */
export function OtHubLayout({
  transitOfficeId,
  activeTab,
  moduleTitle,
  children,
}: OtHubLayoutProps) {
  const router = useRouter();
  const [officeLabel, setOfficeLabel] = useState<string | undefined>();
  const [inactiveTenant, setInactiveTenant] = useState(false);
  const [showTabBar, setShowTabBar] = useState(false);
  const [showBackToList, setShowBackToList] = useState(false);

  useEffect(() => {
    rememberOtTransitOfficeId(transitOfficeId);
  }, [transitOfficeId]);

  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    const superAdmin = isSuperAdmin(payload);
    const otAdmin = isOtAdmin(payload);
    // Admin OT: sin pestañas (viven en el dock). SuperAdmin conserva la barra.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setShowTabBar(superAdmin);
    setShowBackToList(superAdmin && !otAdmin);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const superAdmin = isSuperAdmin(decodeJwtPayload(getToken()));
    void fetchOtProfile(controller.signal, { transitOfficeId: transitOfficeId })
      .then((profile) => {
        if (controller.signal.aborted) {
          return;
        }
        if (
          !superAdmin &&
          profile.transitOfficeId &&
          profile.transitOfficeId !== transitOfficeId
        ) {
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

  useEffect(() => {
    const controller = new AbortController();
    void fetchTransitOfficesOperationalStatus(controller.signal)
      .then((rows) => {
        if (controller.signal.aborted) {
          return;
        }
        const office = rows.find((o) => o.id === transitOfficeId);
        setInactiveTenant(Boolean(office?.hasTenant && office.estadoActivo === false));
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setInactiveTenant(false);
        }
      });
    return () => controller.abort();
  }, [transitOfficeId]);

  const subtitle = officeLabel;

  return (
    <main className="app-bg min-h-screen px-4 py-6 md:px-8">
      {showBackToList && (
        <button
          type="button"
          onClick={() => router.push(otHubListPath())}
          className="mb-4 flex items-center gap-2 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 rounded-md"
          style={{ color: "#557EFF" }}
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Volver al listado de OT
        </button>
      )}

      <ModuleTitle title={moduleTitle} subtitle={subtitle} />

      {inactiveTenant && (
        <div
          role="status"
          data-testid="ot-inactive-banner"
          className="mt-3 rounded-[10px] border border-[#F5C6C8] bg-[#FDECEC] px-4 py-3 text-sm text-[#8A1C1C] dark:border-red-500/40 dark:bg-red-950/40 dark:text-red-100"
        >
          Organismo inactivo — puedes consultar y ajustar la configuración; el tenant permanece
          desactivado hasta que lo reactives desde el listado.
        </div>
      )}

      <div className="mt-4 rounded-2xl border bg-card p-4 md:p-6">
        {showTabBar && (
          <OtTabBar
            tabs={OT_HUB_TABS.map((t) => ({ id: t.id, label: t.label }))}
            activeId={activeTab}
            onChange={(tabId) =>
              router.push(otHubModulePath(transitOfficeId, tabId as OtHubTabId))
            }
            ariaLabel="Módulos de administración OT"
          />
        )}
        <div className={showTabBar ? "mt-6" : undefined}>{children}</div>
      </div>
    </main>
  );
}
