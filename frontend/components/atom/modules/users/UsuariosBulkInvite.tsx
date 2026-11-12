"use client";

import { useEffect, useMemo, useState } from "react";
import { iconoComo } from "@flit/ui/Icono";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { SearchableSelect } from "@/components/atom/SearchableSelect";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import { fetchAllTransitOfficeTenants, type TransitOfficeTenantItem } from "@/lib/api/admin-transit-office-tenants";
import {
  createOtBulkInvitationsClient,
  createSecurityBulkInvitationsClient,
  type BulkInvitationReport,
} from "@/lib/api/bulk-invitations-client";
import type { CompanyListItem } from "@/lib/api/types";
import { USER_PROFILE_DESCRIPTION, USER_PROFILE_LABEL, type UserProfileKind } from "@/lib/users/profiles";
import { BulkInviteModal } from "./BulkInviteModal";

/** El perfil FLIT queda fuera de v1 (ADR-0077): no pertenece a ninguna compañía ni organismo. */
type BulkProfile = Exclude<UserProfileKind, "FLIT">;
const BULK_PROFILES: readonly BulkProfile[] = ["GESTOR", "OT"];
const PROFILE_ICON: Record<BulkProfile, ReturnType<typeof iconoComo>> = { GESTOR: iconoComo("escudo"), OT: iconoComo("banco") };

export interface UsuariosBulkInviteProps {
  /** SuperAdmin elige Perfil + destino; AdminCompany tiene el destino fijo (su compañía, perfil Gestor). */
  isSuperAdmin: boolean;
  onClose: () => void;
  onCompleted?: (report: BulkInvitationReport | null) => void;
}

/**
 * Importar Excel desde el módulo Usuarios (HU #14427 · Feature #14420).
 *
 * Arma el contexto de la invitación masiva y se lo pasa a `BulkInviteModal`:
 * - SuperAdmin: Perfil (Gestor u OT, sin FLIT) + Empresa u Organismo destino. Gestor usa
 *   `/security/invitations/bulk*` con `targetTenantId`; OT usa `/admin/ot/users/invite/bulk*` con
 *   `transitOfficeId`. Sin destino elegido no hay cliente y el modal no deja descargar ni cargar.
 * - AdminCompany: sin selector; el JWT fija su compañía.
 * Cambiar Perfil o destino cambia `contextKey` y el modal descarta el archivo ya cargado.
 */
export function UsuariosBulkInvite({ isSuperAdmin, onClose, onCompleted }: UsuariosBulkInviteProps) {
  const [profile, setProfile] = useState<BulkProfile>("GESTOR");
  const [destinationId, setDestinationId] = useState("");
  const [companies, setCompanies] = useState<{ id: string; name: string }[]>([]);
  const [otTenants, setOtTenants] = useState<TransitOfficeTenantItem[]>([]);
  const [destinationsStatus, setDestinationsStatus] = useState<"loading" | "ready" | "error">(
    isSuperAdmin ? "loading" : "ready",
  );
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!isSuperAdmin) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial / reintento de destinos
    setDestinationsStatus("loading");
    Promise.all([fetchAllCompanies(), fetchAllTransitOfficeTenants()])
      .then(([companiesData, otData]) => {
        if (cancelled) return;
        setCompanies(companiesData.map((c: CompanyListItem) => ({ id: c.id, name: c.razonSocial })));
        setOtTenants(otData);
        setDestinationsStatus("ready");
      })
      .catch(() => {
        if (!cancelled) setDestinationsStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [isSuperAdmin, attempt]);

  const client = useMemo(() => {
    if (!isSuperAdmin) return createSecurityBulkInvitationsClient();
    if (!destinationId) return null;
    if (profile === "OT") {
      const tenant = otTenants.find((t) => t.id === destinationId);
      return tenant ? createOtBulkInvitationsClient({ transitOfficeId: tenant.transitOfficeId }) : null;
    }
    return createSecurityBulkInvitationsClient({ targetTenantId: destinationId });
  }, [isSuperAdmin, profile, destinationId, otTenants]);

  function changeProfile(next: BulkProfile) {
    setProfile(next);
    setDestinationId("");
  }

  const destinationOptions =
    profile === "OT"
      ? otTenants.map((t) => ({ value: t.id, label: t.legalName, hint: t.transitOfficeCode }))
      : companies.map((c) => ({ value: c.id, label: c.name }));

  return (
    <BulkInviteModal
      client={client}
      contextKey={isSuperAdmin ? `${profile}:${destinationId}` : "own-company"}
      contextSummary={isSuperAdmin ? undefined : `${USER_PROFILE_LABEL.GESTOR} · tu compañía`}
      contextSlot={
        isSuperAdmin
          ? ({ disabled }) => (
              <div className="flex flex-col gap-3" data-testid="bulk-invite-context-picker">
                <fieldset disabled={disabled} className="min-w-0">
                  <legend className="mb-1 block text-xs font-semibold">Perfil *</legend>
                  <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                    {BULK_PROFILES.map((p) => {
                      const Icon = PROFILE_ICON[p];
                      const active = profile === p;
                      return (
                        <label
                          key={p}
                          className="flex cursor-pointer items-start gap-2 rounded-xl border px-3 py-2 text-xs transition has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-flit-brand"
                          style={{
                            borderColor: active ? "var(--color-flit-brand)" : "var(--di-border)",
                            background: active ? "color-mix(in srgb, var(--color-flit-brand) 6%, transparent)" : undefined,
                          }}
                        >
                          <input
                            type="radio"
                            name="bulk-invite-profile"
                            value={p}
                            checked={active}
                            onChange={() => changeProfile(p)}
                            className="mt-0.5 h-3.5 w-3.5 accent-flit-brand"
                          />
                          <span className="min-w-0">
                            <span className="flex items-center gap-1.5 font-semibold">
                              <Icon className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--color-flit-brand)" }} aria-hidden="true" />
                              {USER_PROFILE_LABEL[p]}
                            </span>
                            <span className="mt-0.5 block opacity-70">{USER_PROFILE_DESCRIPTION[p]}</span>
                          </span>
                        </label>
                      );
                    })}
                  </div>
                </fieldset>

                {destinationsStatus === "error" ? (
                  <InlineAlert
                    tone="error"
                    title="No se pudieron cargar los destinos"
                    action={
                      <button
                        type="button"
                        onClick={() => setAttempt((n) => n + 1)}
                        className="text-xs font-semibold underline"
                      >
                        Reintentar
                      </button>
                    }
                  >
                    Verifica tu conexión e inténtalo de nuevo.
                  </InlineAlert>
                ) : (
                  <SearchableSelect
                    id="bulk-invite-destination"
                    label={profile === "OT" ? "Organismo de tránsito destino *" : "Compañía destino *"}
                    options={destinationOptions}
                    value={destinationId}
                    onChange={setDestinationId}
                    disabled={disabled || destinationsStatus === "loading"}
                    placeholder={destinationsStatus === "loading" ? "Cargando…" : "Buscar destino…"}
                  />
                )}
              </div>
            )
          : undefined
      }
      onClose={onClose}
      onCompleted={onCompleted}
    />
  );
}
