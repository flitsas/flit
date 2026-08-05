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
  applySelectIntent,
  applyTramitesSuccess,
  applyUserText,
  applyValidacionesSuccess,
  createInitialState,
  queryLabelForIntent,
  type DrFlitChatState,
} from "./dr-flit-conversation";
import type { DrFlitClientBranch, DrFlitIntentId } from "./dr-flit-intents";
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

export function useDrFlitChat(displayName?: string | null) {
  const [open, setOpen] = useState(false);
  const [state, setState] = useState<DrFlitChatState>(() =>
    createInitialState(displayName),
  );
  const panelId = useId();
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const fabRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchGen = useRef(0);

  const openPanel = useCallback(() => {
    searchGen.current += 1;
    setState(createInitialState(displayName));
    setOpen(true);
  }, [displayName]);

  const closePanel = useCallback(() => {
    searchGen.current += 1;
    setOpen(false);
    queueMicrotask(() => fabRef.current?.focus());
  }, []);

  const togglePanel = useCallback(() => {
    setOpen((v) => {
      if (v) {
        searchGen.current += 1;
        queueMicrotask(() => fabRef.current?.focus());
        return false;
      }
      return true;
    });
  }, []);

  useEffect(() => {
    if (!open) return;

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        closePanel();
      }
    };
    window.addEventListener("keydown", onKey);

    const t = window.setTimeout(() => {
      closeButtonRef.current?.focus();
    }, 0);

    return () => {
      window.removeEventListener("keydown", onKey);
      window.clearTimeout(t);
    };
  }, [open, closePanel]);

  useEffect(() => {
    if (!open) return;
    const root = panelRef.current;
    if (!root) return;

    const onTab = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      const focusables = root.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      if (focusables.length === 0) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };

    root.addEventListener("keydown", onTab);
    return () => root.removeEventListener("keydown", onTab);
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
    setState(createInitialState(displayName));
  }, [displayName]);

  const navigate = useCallback((href: string) => {
    setOpen(false);
    window.location.assign(href);
  }, []);

  return {
    open,
    openPanel,
    closePanel,
    togglePanel,
    state,
    selectIntent,
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
