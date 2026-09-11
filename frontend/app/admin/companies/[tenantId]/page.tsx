"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { ArrowLeft, Network } from "lucide-react";
import Link from "next/link";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider } from "@/components/admin/Toast";
import { CompanyConfigTabs } from "@/components/admin/companies/CompanyConfigTabs";
import { AdminChildContextBanner } from "@/components/admin/companies/AdminChildContextBanner";
import { CompanyChildrenSection } from "@/components/admin/companies/CompanyChildrenSection";
import { WhitelistPanel } from "@/components/admin/companies/panels/WhitelistPanel";
import { OTConfigTablePanel } from "@/components/admin/companies/panels/OTConfigTablePanel";
import { TransitBlocksPanel } from "@/components/admin/companies/panels/TransitBlocksPanel";
import {
  resolveOtConfigPanelMode,
  showSuperAdminTransitBlocksPanel,
} from "@/lib/companies/ot-config-mode";
import { AuditLogPanel } from "@/components/admin/companies/panels/AuditLogPanel";
import { PlatePreassignViewer } from "@/components/admin/companies/panels/PlatePreassignViewer";
import { CompanyDocumentParamsPanel } from "@/components/admin/documents/CompanyDocumentParamsPanel";
import { RepresentativesAndVaultTab } from "@/components/admin/companies/legal-representatives/RepresentativesAndVaultTab";
import { CompanyMandatariosPanel } from "@/components/admin/companies/mandate-signers/CompanyMandatariosPanel";
import { CompanyUsersPanel } from "@/components/admin/companies/panels/CompanyUsersPanel";
import { fetchCompany, fetchCompanyChildren, fetchTenantSettings, updateTenantSettings } from "@/lib/api/admin-companies";
import { isHeadTenantType } from "@/lib/api/types";
import type { CompanyListItem, TenantSettings, TenantSettingsUpdate } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";

export default function AdminCompanyDetailPage() {
  return (
    <ToastProvider>
      <CompanyDetail />
    </ToastProvider>
  );
}

function CompanyDetail() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const params = useParams<{ tenantId: string }>();
  const tenantId = params.tenantId;
  const networkHeadId = searchParams.get("networkHead");
  const { isAdminCompany, isSuperAdmin, isGroupParent, tenantId: callerTenantId } = usePermissions();

  const [status, setStatus] = useState<UiStatus>("loading");
  const [settings, setSettings] = useState<TenantSettings | null>(null);
  const [isNew, setIsNew] = useState(false);
  const [company, setCompany] = useState<CompanyListItem | null>(null);
  const [activeChildrenCount, setActiveChildrenCount] = useState(0);
  const [parentCompany, setParentCompany] = useState<CompanyListItem | null>(null);
  const [accessChecked, setAccessChecked] = useState(false);

  const managingChild =
    Boolean(networkHeadId) &&
    isAdminCompany &&
    !isSuperAdmin &&
    callerTenantId === networkHeadId &&
    tenantId !== callerTenantId;

  const otPanelMode = resolveOtConfigPanelMode({
    company,
    parentTenantType: parentCompany?.tenantType ?? null,
    isSuperAdmin,
    isAdminCompany,
  });

  const showTransitBlocksPanel = showSuperAdminTransitBlocksPanel(company, isSuperAdmin);

  const blocksTenantId =
    company?.parentTenantId && parentCompany?.tenantType === "MARCA_BLANCA"
      ? parentCompany.id
      : tenantId;

  const showChildrenSection = isSuperAdmin && company && isHeadTenantType(company.tenantType);

  const showNetworkLink =
    (isGroupParent && callerTenantId === tenantId && isAdminCompany && !isSuperAdmin) ||
    (isSuperAdmin && company && isHeadTenantType(company.tenantType));

  useEffect(() => {
    let cancelled = false;

    async function verifyAccess() {
      if (isSuperAdmin) {
        setAccessChecked(true);
        return;
      }
      if (!isAdminCompany || !callerTenantId) {
        router.replace("/403");
        return;
      }
      if (tenantId === callerTenantId) {
        setAccessChecked(true);
        return;
      }
      if (networkHeadId === callerTenantId) {
        try {
          const child = await fetchCompany(tenantId);
          if (cancelled) return;
          if (child?.parentTenantId === callerTenantId) {
            setAccessChecked(true);
            return;
          }
        } catch {
          /* fallthrough */
        }
      }
      router.replace(`/admin/companies/${callerTenantId}`);
    }

    void verifyAccess();
    return () => {
      cancelled = true;
    };
  }, [isAdminCompany, isSuperAdmin, callerTenantId, tenantId, networkHeadId, router]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      if (!accessChecked) return;
      setStatus("loading");
      try {
        const [data, identity] = await Promise.all([
          fetchTenantSettings(tenantId, signal),
          fetchCompany(tenantId, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setCompany(identity);
        setIsNew(data === null);
        setSettings(data ?? defaultSettings(tenantId));

        if (identity?.parentTenantId) {
          const parent = await fetchCompany(identity.parentTenantId, signal);
          if (!signal?.aborted) {
            setParentCompany(parent);
          }
        } else if (!signal?.aborted) {
          setParentCompany(null);
        }

        if (identity && isSuperAdmin && isHeadTenantType(identity.tenantType)) {
          try {
            const children = await fetchCompanyChildren(identity.id, signal);
            if (!signal?.aborted) {
              setActiveChildrenCount(children.filter((c) => c.estadoActivo).length);
            }
          } catch {
            if (!signal?.aborted) setActiveChildrenCount(0);
          }
        }

        setStatus("ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId, accessChecked],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleSaveSettings = async (update: TenantSettingsUpdate) => {
    const updated = await updateTenantSettings(tenantId, update);
    setSettings(updated);
    setIsNew(false);
  };

  const backHref = useMemo(() => {
    if (managingChild && networkHeadId) {
      return `/admin/companies/${networkHeadId}/children`;
    }
    if (isAdminCompany && !isSuperAdmin) {
      return isGroupParent ? `/admin/companies/${callerTenantId}/children` : "/";
    }
    return "/admin/companies";
  }, [managingChild, networkHeadId, isAdminCompany, isSuperAdmin, isGroupParent, callerTenantId]);

  const backLabel = managingChild
    ? "Volver al panel de red"
    : isAdminCompany && !isSuperAdmin
      ? "Volver al inicio"
      : "Volver al listado";

  if (!accessChecked) {
    return null;
  }

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push(backHref)}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> {backLabel}
      </button>

      {managingChild && company && <AdminChildContextBanner childName={company.razonSocial} />}

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Configuración de compañía"
          subtitle="Edita las políticas operativas y revisa el historial de cambios."
        />
        {showNetworkLink && (
          <Link
            href={`/admin/companies/${tenantId}/children`}
            className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-semibold"
            style={{ color: "#557EFF", borderColor: "#557EFF" }}
          >
            <Network className="h-3.5 w-3.5" aria-hidden />
            Panel de red
          </Link>
        )}
      </div>

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          errorMessage="No se pudo cargar la configuración de la compañía."
        >
          {settings && (
            <>
              {isNew && (
                <div
                  className="mb-3 rounded-xl border px-3 py-2 text-xs"
                  style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
                  role="status"
                >
                  Esta compañía aún no tiene configuración. Define los valores y pulsa
                  &nbsp;<strong>Guardar todo</strong>&nbsp;para crearla.
                </div>
              )}
              <CompanyConfigTabs
                settings={settings}
                company={company}
                onSaveSettings={handleSaveSettings}
                whitelistSlot={<WhitelistPanel tenantId={tenantId} />}
                otSlot={
                  <>
                    {showTransitBlocksPanel && (
                      <div className="mb-4 rounded-2xl border p-4">
                        <TransitBlocksPanel
                          tenantId={tenantId}
                          activeChildrenCount={activeChildrenCount}
                        />
                      </div>
                    )}
                    <OTConfigTablePanel
                      tenantId={tenantId}
                      mode={otPanelMode}
                      grantScopeWarningCount={
                        otPanelMode === "superadmin-concession" ? activeChildrenCount : 0
                      }
                      blocksTenantId={blocksTenantId}
                    />
                  </>
                }
                auditSlot={<AuditLogPanel tenantId={tenantId} />}
                documentosSlot={<CompanyDocumentParamsPanel tenantId={tenantId} />}
                platesSlot={<PlatePreassignViewer tenantId={tenantId} />}
                legalRepresentativesSlot={<RepresentativesAndVaultTab tenantId={tenantId} />}
                mandatariosSlot={<CompanyMandatariosPanel tenantId={tenantId} />}
                usuariosSlot={
                  isSuperAdmin ? <CompanyUsersPanel tenantId={tenantId} /> : undefined
                }
              />
            </>
          )}
        </UiStateBoundary>
      </div>

      {showChildrenSection && company && <CompanyChildrenSection company={company} />}
    </main>
  );
}

function defaultSettings(tenantId: string): TenantSettings {
  return {
    tenantId,
    switchesMatricula: {
      allowInitialRegistration: false,
      allowMiscNewVehicles: false,
      onlyOwnVehicles: false,
      onlyOwnVehiclesByFamily: { matriculas: false, traspaso: false, otros: false },
      blockProcedureFamily: { matriculas: true, traspaso: false, otros: false },
    },
    baulFirmasActivo: false,
    preasignacionPlacaActiva: false,
    enrutamientoSMTP: "FLIT_SMTP",
    avisosAprobacionActivos: true,
    avisosRechazoActivos: true,
    destinatariosNotificacion: {
      comprador: true,
      vendedorOPropietario: true,
      radicador: true,
      extraEmail: null,
    },
    metodosRecaudo: [],
  };
}
