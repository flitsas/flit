"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "@/lib/api/client";

export interface AccessibleAction {
  id: string;
  slug: string;
  name: string;
}

export interface AccessibleModule {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
  actions: AccessibleAction[];
}

interface State {
  modules: AccessibleModule[];
  loading: boolean;
  error: string | null;
}

export function useAccessibleModules(enabled = true): State {
  const [state, setState] = useState<State>({ modules: [], loading: enabled, error: null });

  useEffect(() => {
    if (!enabled) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setState({ modules: [], loading: false, error: null });
      return;
    }
    let cancelled = false;
    setState({ modules: [], loading: true, error: null });
    apiFetch<AccessibleModule[]>("/api/v1/security/modules")
      .then((modules) => {
        if (!cancelled) setState({ modules, loading: false, error: null });
      })
      .catch((err: unknown) => {
        if (!cancelled)
          setState({ modules: [], loading: false, error: (err as Error).message ?? "Error" });
      });
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return state;
}
