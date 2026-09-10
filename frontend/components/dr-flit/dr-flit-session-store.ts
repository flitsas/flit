import {
  syncMessageIdSeqFromState,
  type DrFlitChatState,
} from "./dr-flit-conversation";

/** Persiste open + conversación entre remounts de Shell al cambiar de módulo. */
export const DR_FLIT_SESSION_STORAGE_KEY = "flit.dr-flit.session.v1";

export type DrFlitPersistedSession = {
  open: boolean;
  state: DrFlitChatState;
};

function canUseSessionStorage(): boolean {
  return typeof window !== "undefined" && typeof window.sessionStorage !== "undefined";
}

export function loadDrFlitSession(): DrFlitPersistedSession | null {
  if (!canUseSessionStorage()) return null;
  try {
    const raw = window.sessionStorage.getItem(DR_FLIT_SESSION_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as DrFlitPersistedSession;
    if (
      typeof parsed?.open !== "boolean" ||
      !parsed.state ||
      !Array.isArray(parsed.state.messages)
    ) {
      return null;
    }
    syncMessageIdSeqFromState(parsed.state);
    return parsed;
  } catch {
    return null;
  }
}

export function saveDrFlitSession(session: DrFlitPersistedSession): void {
  if (!canUseSessionStorage()) return;
  try {
    window.sessionStorage.setItem(
      DR_FLIT_SESSION_STORAGE_KEY,
      JSON.stringify(session),
    );
  } catch {
    // Quota / modo privado: no bloquear el chat.
  }
}

export function clearDrFlitSession(): void {
  if (!canUseSessionStorage()) return;
  try {
    window.sessionStorage.removeItem(DR_FLIT_SESSION_STORAGE_KEY);
  } catch {
    // noop
  }
}
