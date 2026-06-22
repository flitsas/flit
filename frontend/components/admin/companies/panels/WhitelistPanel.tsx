"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { WhitelistTextarea } from "@/components/admin/companies/WhitelistTextarea";
import { addWhitelistEmails, fetchWhitelist } from "@/lib/api/admin-companies";
import type { WhitelistEntry } from "@/lib/api/types";

// Slot de lista blanca (HU #10194, AC3). Carga los correos exentos y delega el
// alta a la API; la validación inline por línea vive en WhitelistTextarea.
export function WhitelistPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [entries, setEntries] = useState<WhitelistEntry[]>([]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchWhitelist(tenantId, signal);
        if (signal?.aborted) {
          return;
        }
        setEntries(data);
        setStatus("ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleSave = async (emails: string[]) => {
    await addWhitelistEmails(tenantId, emails);
    show("Lista blanca actualizada.", "success");
    await load();
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      errorMessage="No se pudo cargar la lista blanca."
      skeletonRows={3}
    >
      <WhitelistTextarea entries={entries} onSave={handleSave} />
    </UiStateBoundary>
  );
}
