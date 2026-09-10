"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import {
  decodeJwtPayload,
  isOtAdmin as checkOtAdmin,
  TOKEN_STORAGE_KEY,
} from "@/lib/auth/jwt";
import { getToken } from "@/lib/api/client";
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

function resolveIsOtAdmin(): boolean {
  const token =
    typeof window !== "undefined"
      ? getToken() ?? window.localStorage.getItem(TOKEN_STORAGE_KEY)
      : null;
  return checkOtAdmin(decodeJwtPayload(token));
}

function errorMessage(err: unknown): string {
  if (err instanceof Error && err.message) return err.message;
  return "Error de red o permisos. Intenta de nuevo.";
}

export function useDrFlitChat(
  displayName?: string | null,
  /** Cambia al navegar entre módulos; cierra el panel sin borrar la conversación. */
  routeScope?: string,
) {
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
        const role = { isOtAdmin: resolveIsOtAdmin() };

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

        const results = await searchTramites(
          searchIntent === "cliente" ? "cliente" : searchIntent,
          value,
          role,
        );
        if (gen !== searchGen.current) return;
        setState((prev) =>
          applyTramitesSuccess(
            prev,
            queryLabelForIntent(searchIntent),
            results,
          ),
        );
      } catch (err) {
        if (gen !== searchGen.current) return;
        setState((prev) => applySearchFailure(prev, errorMessage(err)));
      }
    })();
  }, [
    state.phase,
    state.pendingIntent,
    state.queryValue,
    state.pendingClientBranch,
  ]);

  const selectHelpOption = useCallback((optionId: DrFlitHelpOptionId) => {
    setState((prev) => {
      const next = applySelectHelpOption(prev, optionId);
      return next ?? prev;
    });
    queueMicrotask(() => inputRef.current?.focus());
  }, []);

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

  const sendText = useCallback((text: string) => {
    setState((prev) => applyUserText(prev, text));
  }, []);

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
