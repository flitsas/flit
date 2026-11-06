"use client";

import { useCallback, useMemo, useState } from "react";
import { DomainRegisterForm } from "@/components/admin/domain/DomainRegisterForm";
import { DomainStatusPanel, type DomainClient } from "@/components/admin/domain/DomainStatusPanel";
import {
  deletePortalDominio,
  getPortalDominio,
  registerPortalDominio,
  toTenantDomain,
  verifyPortalDominio,
} from "@/lib/api/admin-ot-portal-dominio";
import type { TenantDomainResponse } from "@/lib/api/types";

/**
 * Dominio propio del portal Persona Natural del organismo (Épica #14297, HU #14385). Mismo proceso que marca blanca
 * (DNS, comprobación, certificado, activación) con las pantallas ya existentes y el cliente del organismo inyectado.
 * Sin dominio activo, el portal sigue en el enlace del punto.
 */
export function OtPortalDomainSection({ transitOfficeId }: { transitOfficeId: string }) {
  const [domain, setDomain] = useState<TenantDomainResponse | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const client = useMemo<DomainClient>(() => {
    const scope = { transitOfficeId };
    return {
      get: async () => {
        const found = await getPortalDominio(scope);
        return found ? toTenantDomain(found) : null;
      },
      register: async (body) => toTenantDomain(await registerPortalDominio(body.host, scope)),
      remove: () => deletePortalDominio(scope),
      verify: async () => toTenantDomain(await verifyPortalDominio(scope)),
    };
  }, [transitOfficeId]);

  const handleLoaded = useCallback((loaded: TenantDomainResponse | null) => setDomain(loaded), []);

  return (
    <section
      className="mt-6 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      aria-labelledby="portal-domain-title"
    >
      <div className="mb-3">
        <h2
          id="portal-domain-title"
          className="text-sm font-bold"
          style={{ color: "var(--color-flit-primary)" }}
        >
          Dominio propio del portal
        </h2>
        <p className="text-xs opacity-70">
          Publica el portal en una dirección propia. Mientras no esté activo, el portal sigue en el
          enlace del punto.
        </p>
      </div>

      <DomainStatusPanel
        mode="admin"
        client={client}
        reasonVerbatim
        emptyMessage="Este organismo aún no tiene un dominio registrado."
        onLoaded={handleLoaded}
        reloadToken={reloadToken}
      />

      <div className="mt-4">
        <DomainRegisterForm
          tenantId={transitOfficeId}
          purpose="portal"
          label="Dominio del portal"
          ownerLabel="este organismo"
          client={client}
          domain={domain}
          onRegistered={(updated) => {
            setDomain(updated);
            setReloadToken((n) => n + 1);
          }}
          onRemoved={() => {
            setDomain(null);
            setReloadToken((n) => n + 1);
          }}
        />
      </div>
    </section>
  );
}
