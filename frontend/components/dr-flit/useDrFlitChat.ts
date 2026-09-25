"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { resolveContextArticle, visibleAudiences } from "@/lib/manual/catalog";
import {
  acceptDrFlitConsent,
  createSupportCase,
  getDrFlitConsent,
  isConsentRequiredError,
  postDrFlitChat,
  uploadSupportAttachment,
} from "@/lib/api/dr-flit-client";
import { ApiError } from "@/lib/api/types";
import type { DrFlitConsentStatus, DrFlitSupportCaseDraft } from "./dr-flit-chat-types";
import { createSupportDraft, resolveAffectedModule, toSupportCaseRequest } from "./dr-flit-support-case";
import { readJwtPayload, resolveDrFlitContext } from "./dr-flit-context";
import { buildHistorialPlacaHref, DR_FLIT_CHAT_ENABLED } from "./dr-flit-intents";
import {
  applyBackToSearch,
  applyCancelSupportCase,
  applyChatDegraded,
  applyContinueSupportCase,
  applyConsentAccepted,
  applyConsentDeclined,
  applyConsentError,
  applyConsentStatus,
  applyRequestConsent,
  hasConsent,
  applyEditSupportCase,
  applyOpenSupportCase,
  applySubmitSupportCase,
  applySupportCaseCreated,
  applySupportCaseError,
  applyUpdateSupportDraft,
  applyChatRateLimited,
  applyChatSuccess,
  buildChatHistory,
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

/** HU #12931 — estado del consentimiento a partir de un 428 (trae la versión vigente) o del previo. */
function consentFromError(err: unknown, prev: DrFlitChatState): DrFlitConsentStatus {
  const version = err instanceof ApiError ? (err.body as { version?: string } | null)?.version : undefined;
  return { version: version ?? prev.consent?.version ?? "", accepted: false };
}

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
  /**
   * HU #12929 — nombre y correo del usuario (del `currentUser` del Shell) para prellenar el caso de
   * soporte. La compañía sale del `tenant_name` del JWT.
   */
  supportContact?: { name?: string | null; email?: string | null };
}

export function useDrFlitChat(
  displayName?: string | null,
  /** Cambia al navegar entre módulos; cierra el panel sin borrar la conversación. */
  routeScope?: string,
  options: UseDrFlitChatOptions = {},
) {
  const historialPlacaEnabled = options.historialPlacaEnabled ?? true;
  // Sin nombre en el perfil el campo queda vacío para que la persona lo escriba: el displayName del
  // Shell cae al correo cuando no hay nombre, y un correo no es un nombre.
  const supportName = options.supportContact ? (options.supportContact.name ?? null) : (displayName ?? null);
  const supportEmail = options.supportContact?.email ?? null;
  const hydrated = useRef(loadDrFlitSession());
  // Tras remount (p. ej. layout de otro módulo) el panel arranca cerrado;
  // la conversación sí se restaura hasta “Terminar chat”.
  const [open, setOpen] = useState(false);
  const [consentBusy, setConsentBusy] = useState(false);
  const [state, setState] = useState<DrFlitChatState>(() => {
    const restored = hydrated.current?.state;
    if (!restored) return createInitialState(displayName);
    // HU #12930 — una radicación interrumpida (recarga a mitad) no se relanza sola: vuelve al resumen
    // para que el usuario confirme de nuevo. Radicar exige siempre un clic.
    if (restored.phase === "submitting_support_case") {
      return { ...restored, phase: "confirming_support_case", isTyping: false };
    }
    return restored;
  });
  // El estado vigente para callbacks asíncronos (p. ej. la versión del consentimiento al aceptar).
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  /** HU #12931 — al abrir el panel se consulta si el usuario ya aceptó la versión vigente. */
  useEffect(() => {
    if (!open || state.consent) return;
    let cancelled = false;
    getDrFlitConsent()
      .then((status) => {
        if (!cancelled) setState((prev) => applyConsentStatus(prev, status));
      })
      // Sin respuesta se trata como no aceptado: se pedirá al usar la IA o el caso.
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [open, state.consent]);

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

  /**
   * HU #12926 — texto libre al chat con LLM. Cualquier respuesta que no sea `ok` (o un error HTTP o de
   * red) cae al buscador local del manual: el chat nunca se queda sin responder.
   */
  useEffect(() => {
    if (state.phase !== "chat_loading") return;
    const gen = ++searchGen.current;
    const message = state.queryValue ?? "";
    const history = buildChatHistory(state);
    const helpAudiences = visibleAudiences(currentContext().role);

    void (async () => {
      try {
        const response = await postDrFlitChat({ message, history, routeScope: routeScope ?? null });
        if (gen !== searchGen.current) return;
        setState((prev) => {
          if (response.status === "ok")
            return applyChatSuccess(prev, response, response.intent === "soporte" ? newSupportDraft() : null);
          if (response.status === "rate_limited") return applyChatRateLimited(prev, response);
          return applyChatDegraded(prev, { helpAudiences }, response.usage);
        });
      } catch (err) {
        if (gen !== searchGen.current) return;
        // HU #12931 — el backend exige el consentimiento (p. ej. cambió la versión del texto): se pide
        // y el mensaje se envía al aceptar. Cualquier otro error cae al buscador local.
        setState((prev) =>
          isConsentRequiredError(err)
            ? applyRequestConsent({ ...prev, consent: consentFromError(err, prev) }, { kind: "chat", text: message })
            : applyChatDegraded(prev, { helpAudiences }),
        );
      }
    })();
    // Solo la entrada a chat_loading dispara la llamada; el resto del estado se lee en ese momento.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.phase, state.queryValue]);

  /**
   * HU #12930 — la radicación ocurre SOLO al entrar en `submitting_support_case`, a la que se llega
   * únicamente por el clic en «Confirmar y radicar caso» o «Reintentar».
   */
  useEffect(() => {
    if (state.phase !== "submitting_support_case" || !state.supportDraft) return;
    const request = toSupportCaseRequest(state.supportDraft);
    let cancelled = false;

    void (async () => {
      try {
        const created = await createSupportCase(request);
        if (!cancelled) setState((prev) => applySupportCaseCreated(prev, created));
      } catch (err) {
        if (cancelled) return;
        setState((prev) =>
          isConsentRequiredError(err)
            ? applyRequestConsent({ ...prev, consent: consentFromError(err, prev) }, { kind: "submit" })
            : applySupportCaseError(prev, errorMessage(err)),
        );
      }
    })();
    return () => {
      cancelled = true;
    };
    // Solo la entrada a la fase dispara la llamada; el borrador se lee en ese momento.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.phase]);

  const submitSupportCase = useCallback(() => {
    setState((prev) =>
      hasConsent(prev) ? applySubmitSupportCase(prev) : applyRequestConsent(prev, { kind: "submit" }),
    );
  }, []);

  const editSupportCase = useCallback(() => {
    setState((prev) => applyEditSupportCase(prev));
  }, []);

  /** HU #12929 — borrador del caso prellenado con lo que la plataforma ya sabe del usuario. */
  const newSupportDraft = useCallback(
    () =>
      createSupportDraft(
        { name: supportName, email: supportEmail, company: readJwtPayload()?.tenant_name ?? null },
        resolveAffectedModule(routeScope),
      ),
    [supportName, supportEmail, routeScope],
  );

  const openSupportCase = useCallback(() => {
    // HU #12931 — el caso envía datos a soporte: sin consentimiento, primero se pide.
    setState((prev) =>
      hasConsent(prev) ? applyOpenSupportCase(prev, newSupportDraft()) : applyRequestConsent(prev, { kind: "support" }),
    );
  }, [newSupportDraft]);

  /** HU #12931 — registra la aceptación de la versión vigente y continúa lo pendiente. */
  const acceptConsent = useCallback(async () => {
    setConsentBusy(true);
    try {
      const version = stateRef.current.consent?.version || (await getDrFlitConsent()).version;
      const status = await acceptDrFlitConsent(version);
      setState((prev) => applyConsentAccepted(prev, status, newSupportDraft()));
    } catch (err) {
      const current = err instanceof ApiError && err.status === 409 ? (err.body as { version?: string } | null)?.version : null;
      setState((prev) =>
        current
          ? applyConsentError(applyConsentStatus(prev, { version: current, accepted: false }), "El texto se actualizó. Léelo de nuevo antes de aceptar.")
          : applyConsentError(prev, errorMessage(err)),
      );
    } finally {
      setConsentBusy(false);
    }
  }, [newSupportDraft]);

  const declineConsent = useCallback(() => {
    setState((prev) => applyConsentDeclined(prev));
  }, []);

  const updateSupportDraft = useCallback((draft: DrFlitSupportCaseDraft) => {
    setState((prev) => applyUpdateSupportDraft(prev, draft));
  }, []);

  const continueSupportCase = useCallback(() => {
    setState((prev) => applyContinueSupportCase(prev));
  }, []);

  const cancelSupportCase = useCallback(() => {
    setState((prev) => applyCancelSupportCase(prev));
  }, []);

  /** Sube un adjunto y guarda solo su id en el borrador. Devuelve el motivo si falló. */
  const attachSupportFile = useCallback(async (file: File): Promise<string | null> => {
    try {
      const uploaded = await uploadSupportAttachment(file);
      setState((prev) =>
        prev.supportDraft
          ? applyUpdateSupportDraft(prev, {
              ...prev.supportDraft,
              attachments: [...prev.supportDraft.attachments, uploaded],
            })
          : prev,
      );
      return null;
    } catch (err) {
      return errorMessage(err);
    }
  }, []);

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
      // HU #12931 AC2 — escribir cuenta como haber visto el aviso: no bloquea ni se repite.
      setState((prev) =>
        applyUserText(prev, text, { helpAudiences, chatEnabled: DR_FLIT_CHAT_ENABLED, requireConsent: !hasConsent(prev) }),
      );
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
    openSupportCase,
    updateSupportDraft,
    continueSupportCase,
    cancelSupportCase,
    attachSupportFile,
    submitSupportCase,
    editSupportCase,
    acceptConsent,
    declineConsent,
    consentBusy,
    panelId,
    closeButtonRef,
    fabRef,
    panelRef,
    inputRef,
  };
}
