import { describe, expect, it, beforeEach } from "vitest";

import { createInitialState, applySelectIntent } from "../dr-flit-conversation";
import {
  clearDrFlitSession,
  DR_FLIT_SESSION_STORAGE_KEY,
  loadDrFlitSession,
  saveDrFlitSession,
} from "../dr-flit-session-store";

describe("dr-flit-session-store", () => {
  beforeEach(() => {
    clearDrFlitSession();
  });

  it("guarda y recupera open + state", () => {
    const state = applySelectIntent(createInitialState("Ana"), "placa")!.next;
    saveDrFlitSession({ open: true, state });

    const loaded = loadDrFlitSession();
    expect(loaded?.open).toBe(true);
    expect(loaded?.state.phase).toBe("awaiting_value");
    expect(loaded?.state.messages.some((m) => m.text.includes("placa"))).toBe(
      true,
    );
  });

  it("clear elimina la clave", () => {
    saveDrFlitSession({ open: false, state: createInitialState() });
    clearDrFlitSession();
    expect(window.sessionStorage.getItem(DR_FLIT_SESSION_STORAGE_KEY)).toBeNull();
    expect(loadDrFlitSession()).toBeNull();
  });
});
