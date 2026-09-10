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
  /**
   * La petición ya resolvió (con datos o con error) al menos una vez.
   *
   * <p>No es lo mismo que `!loading`, y confundirlos era un fallo real: con `enabled` en false el
   * hook se declara en `{ modules: [], loading: false }`, es decir lista VACÍA anunciando que no
   * está cargando. Quien mirara solo `loading` concluía que no hay permisos y denegaba el acceso.
   * `ready` distingue «no he preguntado» de «pregunté y no hay nada».</p>
   */
  ready: boolean;
}

export function useAccessibleModules(enabled = true): State {
  const [state, setState] = useState<State>({
    modules: [],
    loading: enabled,
    error: null,
    ready: false,
  });

  useEffect(() => {
    if (!enabled) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setState({ modules: [], loading: false, error: null, ready: false });
      return;
    }
    let cancelled = false;
    setState({ modules: [], loading: true, error: null, ready: false });
    apiFetch<AccessibleModule[]>("/api/v1/security/modules")
      .then((modules) => {
        if (!cancelled) setState({ modules, loading: false, error: null, ready: true });
      })
      .catch((err: unknown) => {
        if (!cancelled)
          setState({
            modules: [],
            loading: false,
            error: (err as Error).message ?? "Error",
            ready: true,
          });
      });
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return state;
}
