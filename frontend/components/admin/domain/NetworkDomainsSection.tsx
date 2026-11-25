"use client";

import { useCallback, useEffect, useState } from "react";
import { Power, RefreshCw } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { DomainRegisterForm } from "@/components/admin/domain/DomainRegisterForm";
import { DomainStatusPanel } from "@/components/admin/domain/DomainStatusPanel";
import { activateNetwork, domainErrorCode, domainErrorMessage, listNetworkDomains } from "@/lib/api/domain-client";
import type { NetworkDomainsResponse, TenantDomainResponse } from "@/lib/api/types";

const PRODUCT_NAMES: Record<string, string> = {
  tramites: "Trámites",
  comparendos: "Comparendos",
  diagnostico: "Diagnóstico",
};

const HUB = "HUB";

function purposeName(purpose: string): string {
  return purpose === HUB ? "dominio principal" : (PRODUCT_NAMES[purpose] ?? purpose);
}

/**
 * Alta de los dominios de una red Marca Blanca: el principal (login de la red), uno por cada producto habilitado, y la
 * activación, que va aparte porque corta el acceso de los usuarios por el dominio de FLIT.
 */
export function NetworkDomainsSection({ tenantId }: { tenantId: string }) {
  const [network, setNetwork] = useState<NetworkDomainsResponse | null>(null);
  const [panelsToken, setPanelsToken] = useState(0);

  const refresh = useCallback(async () => {
    try {
      setNetwork(await listNetworkDomains(tenantId));
    } catch {
      setNetwork(null);
    }
  }, [tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void refresh();
  }, [refresh]);

  const refreshAll = useCallback(() => {
    setPanelsToken((n) => n + 1);
    void refresh();
  }, [refresh]);

  return (
    <section className="rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60" aria-labelledby="domain-admin-title">
      <div className="mb-3">
        <h2 id="domain-admin-title" className="text-sm font-bold" style={{ color: "var(--color-flit-primary)" }}>
          Dominios de la red
        </h2>
        <p className="text-[11px] opacity-60">
          Dominios propios para el acceso y las comunicaciones de esta red Marca Blanca.
        </p>
      </div>

      <DomainEditor
        tenantId={tenantId}
        title="Dominio principal"
        hint="Aquí inician sesión los usuarios de la red."
        label="Dominio principal"
        reloadToken={panelsToken}
        onChanged={refresh}
      />

      {network?.products.map((product) => (
        <div key={product} className="mt-6 border-t pt-6" style={{ borderColor: "#DFE5ED" }}>
          <DomainEditor
            tenantId={tenantId}
            purpose={product}
            title={`Dominio de ${purposeName(product)}`}
            hint={`Aquí trabajan los usuarios de la red en ${purposeName(product)}.`}
            label={`Dominio de ${purposeName(product)}`}
            reloadToken={panelsToken}
            onChanged={refresh}
          />
        </div>
      ))}

      {network && <ActivationCard tenantId={tenantId} network={network} onRefresh={refreshAll} onActivated={setNetwork} />}
    </section>
  );
}

interface DomainEditorProps {
  tenantId: string;
  purpose?: string;
  title: string;
  hint: string;
  label: string;
  reloadToken: number;
  onChanged: () => Promise<void> | void;
}

function DomainEditor({ tenantId, purpose, title, hint, label, reloadToken, onChanged }: DomainEditorProps) {
  const [domain, setDomain] = useState<TenantDomainResponse | null>(null);
  const [localToken, setLocalToken] = useState(0);

  const handleLoaded = useCallback(
    (loaded: TenantDomainResponse | null) => {
      setDomain(loaded);
      void onChanged();
    },
    [onChanged],
  );

  return (
    <div>
      <h3 className="text-xs font-bold" style={{ color: "var(--color-flit-primary)" }}>
        {title}
      </h3>
      <p className="mb-3 text-[11px] opacity-60">{hint}</p>

      <DomainStatusPanel
        mode="admin"
        tenantId={tenantId}
        purpose={purpose}
        onLoaded={handleLoaded}
        reloadToken={reloadToken + localToken}
      />

      <div className="mt-4">
        <DomainRegisterForm
          tenantId={tenantId}
          purpose={purpose}
          label={label}
          domain={domain}
          onRegistered={(updated) => {
            setDomain(updated);
            setLocalToken((n) => n + 1);
          }}
          onRemoved={() => {
            setDomain(null);
            setLocalToken((n) => n + 1);
          }}
        />
      </div>
    </div>
  );
}

interface ActivationCardProps {
  tenantId: string;
  network: NetworkDomainsResponse;
  onRefresh: () => void;
  onActivated: (network: NetworkDomainsResponse) => void;
}

function ActivationCard({ tenantId, network, onRefresh, onActivated }: ActivationCardProps) {
  const [confirming, setConfirming] = useState(false);
  const [activating, setActivating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { ready, active, pending } = network.activation;
  const hubHost = network.domains.find((d) => d.purpose === HUB)?.host;

  async function handleActivate() {
    setActivating(true);
    setError(null);
    try {
      onActivated(await activateNetwork(tenantId));
      setConfirming(false);
    } catch (err) {
      setError(domainErrorMessage(domainErrorCode(err)) || "No se pudo activar la red.");
      setConfirming(false);
    } finally {
      setActivating(false);
    }
  }

  return (
    <div className="mt-6 rounded-xl border p-4" style={{ borderColor: "#DFE5ED" }}>
      <h3 className="text-xs font-bold" style={{ color: "var(--color-flit-primary)" }}>
        Activación de la red
      </h3>

      <p className="mt-1 text-xs" role="status">
        {active && (hubHost ? `La red está activa. Sus usuarios entran por ${hubHost}.` : "La red está activa.")}
        {ready && "Todo está listo. Actívala cuando hayas avisado a los usuarios."}
        {!active && !ready && `Falta completar: ${pending.map(purposeName).join(", ")}.`}
      </p>

      {error && (
        <p role="alert" className="mt-2 text-xs font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}

      <div className="mt-3 flex flex-wrap gap-3">
        {!active && (
          <button
            type="button"
            onClick={() => setConfirming(true)}
            disabled={!ready}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))" }}
          >
            <Power className="h-3.5 w-3.5" aria-hidden /> Activar red
          </button>
        )}
        <button
          type="button"
          onClick={onRefresh}
          className="inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden /> Actualizar estado
        </button>
      </div>

      <Modal open={confirming} onClose={() => setConfirming(false)} busy={activating} size="sm" icon={Power} title="Activar la red">
        <p className="text-sm opacity-80">
          {`Los usuarios de esta red dejarán de poder iniciar sesión por el dominio de FLIT y deberán entrar por ${hubHost ?? "su dominio principal"}.`}
        </p>
        <p className="mt-2 text-sm font-medium" style={{ color: "#8a6000" }} role="note">
          Avísales antes. El login por el dominio nuevo puede tardar hasta 5 minutos en quedar disponible.
        </p>

        <div className="mt-5 flex gap-3">
          <button
            type="button"
            onClick={() => setConfirming(false)}
            disabled={activating}
            className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleActivate()}
            disabled={activating}
            className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))" }}
          >
            {activating ? "Activando…" : "Activar"}
          </button>
        </div>
      </Modal>
    </div>
  );
}
