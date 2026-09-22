"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { resolveContextArticle, visibleAudiences } from "@/lib/manual/catalog";
import { readJwtPayload, resolveDrFlitContext } from "./dr-flit-context";
import { buildHistorialPlacaHref } from "./dr-flit-intents";
import {
  applyBackToSearch,
  applyClientBranch,
  applySearchFailure,
  applySelectHelpOption,
  applySelectIntent,
  applyTramitesSuccess,
  applyUserText,
  applyValidacionesSuccess,
  createInitialState,
  queryLabelForIntent,
  type DrFlitChatState,
} from "./dr-flit-conversation";
import type {
  DrFlitClientBranch,
  DrFlitHelpOptionId,
  DrFlitIntentId,
} from "./dr-flit-intents";
import {
  clearDrFlitSession,
  loadDrFlitSession,
  saveDrFlitSession,
} from "./dr-flit-session-store";
import { searchTramites, searchValidaciones } from "./dr-flit-search";

function errorMessage(err: unknown): string {
  if (err instanceof Error && err.message) return err.message;
  return "Error de red o permisos. Intenta de nuevo.";
}

export interface UseDrFlitChatOptions {
  /**
   * HU-C — el usuario tiene el módulo «Historial por placa» (RBAC `visibleModuleCodes`). Sin él no
   * se ofrece el atajo: llevaría a un módulo que el dock no muestra. Por defecto `true` (sin filtro
   * RBAC, como el propio dock).
   */
  historialPlacaEnabled?: boolean;
}

export function useDrFlitChat(
  displayName?: string | null,
  /** Cambia al navegar entre módulos; cierra el panel sin borrar la conversación. */
  routeScope?: string,
  options: UseDrFlitChatOptions = {},
) {
  const historialPlacaEnabled = options.historialPlacaEnabled ?? true;
  const hydrated = useRef(loadDrFlitSession());
  // Tras remount (p. ej. layout de otro módulo) el panel arranca cerrado;
  // la conversación sí se restaura hasta “Terminar chat”.
  const [open, setOpen] = useState(false);
  const [state, setState] = useState<DrFlitChatState>(() =>
    hydrated.current?.state ?? createInitialState(displayName),
  );
  const panelId = useId();
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const fabRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchGen = useRef(0);
  // HU #12363 — DR-FLIT busca donde el usuario está mirando: el mismo alcance (propio / red / un
  // hijo) que eligió para la tabla de trámites. Para quien no es cabeza el hook no hace llamadas.
  const { networkActive, scope: networkScope } = useNetworkScope();
  /** Solo enfocar al abrir por gesto del usuario, no al remount por navegación. */
  const shouldFocusOnOpen = useRef(false);
  const prevRouteScope = useRef(routeScope);

  useEffect(() => {
    saveDrFlitSession({ open, state });
  }, [open, state]);

  const openPanel = useCallback(() => {
    shouldFocusOnOpen.current = true;
    setOpen(true);
  }, []);

  const closePanel = useCallback(() => {
    setOpen(false);
    queueMicrotask(() => fabRef.current?.focus());
  }, []);

  /** Al cambiar de módulo/ruta: ocultar panel, conservar conversación. */
  useEffect(() => {
    if (routeScope == null) return;
    if (prevRouteScope.current === routeScope) return;
    prevRouteScope.current = routeScope;
    setOpen(false);
  }, [routeScope]);

  const endChat = useCallback(() => {
    searchGen.current += 1;
    clearDrFlitSession();
    setState(createInitialState(displayName));
    queueMicrotask(() => closeButtonRef.current?.focus());
  }, [displayName]);

  const togglePanel = useCallback(() => {
    setOpen((v) => {
      if (v) {
        queueMicrotask(() => fabRef.current?.focus());
        return false;
      }
      shouldFocusOnOpen.current = true;
      return true;
    });
  }, []);

  useEffect(() => {
    if (!open || !shouldFocusOnOpen.current) return;
    shouldFocusOnOpen.current = false;
    const t = window.setTimeout(() => {
      closeButtonRef.current?.focus();
    }, 0);
    return () => window.clearTimeout(t);
  }, [open]);

  /** Ejecuta búsqueda cuando el estado entra en loading. */
  useEffect(() => {
    if (state.phase !== "loading") return;
    const gen = ++searchGen.current;
    const intent = state.pendingIntent;
    const value = state.queryValue;
    const branch = state.pendingClientBranch;

    void (async () => {
      try {
        const ctx = currentContext();

        if (branch === "validaciones" && value) {
          const results = await searchValidaciones(value);
          if (gen !== searchGen.current) return;
          setState((prev) => applyValidacionesSuccess(prev, results));
          return;
        }

        const searchIntent: DrFlitIntentId | null =
          branch === "tramites" ? "cliente" : intent;
        if (!searchIntent || !value) {
          if (gen !== searchGen.current) return;
          setState((prev) =>
            applySearchFailure(prev, "Falta el criterio de búsqueda."),
          );
          return;
        }

        const results = await searchTramites(searchIntent, value, ctx);
        if (gen !== searchGen.current) return;
        // El OT no tiene la SPA «Historial por placa» (su universo es la bandeja del organismo).
        const historialHref =
          searchIntent === "placa" && historialPlacaEnabled && ctx.role !== "ot_admin"
            ? buildHistorialPlacaHref(value)
            : null;
        setState((prev) =>
          applyTramitesSuccess(
            prev,
            queryLabelForIntent(searchIntent),
            results.items,
            results.total,
            historialHref,
          ),
        );
      } catch (err) {
        if (gen !== searchGen.current) return;
        setState((prev) => applySearchFailure(prev, errorMessage(err)));
      }
    })();
    // Ni `currentContext` (alcance de red) ni `historialPlacaEnabled` son dependencias: un cambio a
    // mitad de búsqueda no la relanza; la siguiente ya lo toma. Relanzarla duplicaría resultados.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    state.phase,
    state.pendingIntent,
    state.queryValue,
    state.pendingClientBranch,
  ]);

  /** Contexto de rol/red vigente; se resuelve al momento (el JWT o el alcance pueden cambiar). */
  const currentContext = useCallback(
    () => resolveDrFlitContext(readJwtPayload(), { networkActive, scope: networkScope }),
    [networkActive, networkScope],
  );

  const selectHelpOption = useCallback(
    (optionId: DrFlitHelpOptionId) => {
      // HU-G — artículo del módulo actual, solo si aplica al perfil (HU-F).
      const audiences = visibleAudiences(currentContext().role);
      const contextArticle = resolveContextArticle(routeScope, audiences);
      setState((prev) => {
        const next = applySelectHelpOption(prev, optionId, { contextArticle });
        return next ?? prev;
      });
      queueMicrotask(() => inputRef.current?.focus());
    },
    [routeScope, currentContext],
  );

  const selectIntent = useCallback((intentId: DrFlitIntentId) => {
    setState((prev) => {
      const result = applySelectIntent(prev, intentId);
      return result?.next ?? prev;
    });
    queueMicrotask(() => inputRef.current?.focus());
  }, []);

  const selectClientBranch = useCallback((branch: DrFlitClientBranch) => {
    setState((prev) => applyClientBranch(prev, branch));
  }, []);

  const backToSearch = useCallback(() => {
    searchGen.current += 1;
    setState((prev) => applyBackToSearch(prev));
  }, []);

  const sendText = useCallback(
    (text: string) => {
      // HU-F — la búsqueda del manual solo devuelve artículos del perfil de quien pregunta.
      const helpAudiences = visibleAudiences(currentContext().role);
      setState((prev) => applyUserText(prev, text, { helpAudiences }));
    },
    [currentContext],
  );

  const resetConversation = useCallback(() => {
    searchGen.current += 1;
    clearDrFlitSession();
    setState(createInitialState(displayName));
  }, [displayName]);

  const navigate = useCallback((href: string) => {
    window.open(href, "_blank", "noopener,noreferrer");
  }, []);

  return {
    open,
    openPanel,
    closePanel,
    endChat,
    togglePanel,
    state,
    selectIntent,
    selectHelpOption,
    selectClientBranch,
    backToSearch,
    sendText,
    resetConversation,
    navigate,
    panelId,
    closeButtonRef,
    fabRef,
    panelRef,
    inputRef,
  };
}
