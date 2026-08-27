import { describe, expect, it, beforeEach } from "vitest";

import {
  applyBackToSearch,
  applyClientBranch,
  applySelectHelpOption,
  applySelectIntent,
  applyTramitesSuccess,
  applyUserText,
  applyValidacionesSuccess,
  createInitialState,
  resetMessageIdSeq,
} from "../dr-flit-conversation";

import {
  buildGreeting,
  buildHelpValuePrompt,
  buildValuePrompt,
  DR_FLIT_FREE_TEXT_HINT,
  DR_FLIT_GESTION_INTENTS,
  DR_FLIT_SUPPORT_CASE_URL,
  getIntentById,
} from "../dr-flit-intents";

describe("dr-flit-intents", () => {
  it("expone 4 intents de gestión", () => {
    expect(DR_FLIT_GESTION_INTENTS.map((i) => i.id)).toEqual([
      "placa",
      "vin",
      "tramite",
      "cliente",
    ]);
  });

  it("arma saludo", () => {
    expect(buildGreeting("Juan")).toContain("Hola Juan");
  });
});

describe("dr-flit-conversation", () => {
  beforeEach(() => {
    resetMessageIdSeq();
  });

  it("inicia con menú de sesiones Gestión y Ayuda", () => {
    const state = createInitialState("Ana");
    expect(state.messages[0].text).toBe(buildGreeting("Ana"));
    expect(state.phase).toBe("idle");
    expect(state.showSessionMenu).toBe(true);
  });

  it("al elegir intent pide valor", () => {
    const next = applySelectIntent(createInitialState(), "placa")!.next;
    expect(next.phase).toBe("awaiting_value");
    expect(next.messages.at(-1)?.text).toBe(
      buildValuePrompt(getIntentById("placa")!),
    );
    expect(next.showSessionMenu).toBe(false);
  });

  it("placa/VIN/trámite pasan a loading al enviar valor", () => {
    const awaiting = applySelectIntent(createInitialState(), "vin")!.next;
    const next = applyUserText(awaiting, "1HGCM82633A004352");
    expect(next.phase).toBe("loading");
    expect(next.isTyping).toBe(true);
    expect(next.queryValue).toBe("1HGCM82633A004352");
    expect(next.pendingIntent).toBe("vin");
  });

  it("cliente pregunta rama sin llamar API aún", () => {
    const awaiting = applySelectIntent(createInitialState(), "cliente")!.next;
    const next = applyUserText(awaiting, "900123456");
    expect(next.phase).toBe("awaiting_client_branch");
    expect(next.showClientBranch).toBe(true);
  });

  it("rama trámites entra en loading", () => {
    const awaiting = applySelectIntent(createInitialState(), "cliente")!.next;
    const branched = applyUserText(awaiting, "CLIENTE-1");
    const next = applyClientBranch(branched, "tramites");
    expect(next.phase).toBe("loading");
    expect(next.pendingClientBranch).toBe("tramites");
  });

  it("éxito de trámites muestra resultados", () => {
    const awaiting = applySelectIntent(createInitialState(), "placa")!.next;
    const loading = applyUserText(awaiting, "ABC123");
    const next = applyTramitesSuccess(loading, "placa", [
      {
        id: "11111111-1111-4111-a111-111111111111",
        fecha: "2026-01-01",
        estado: "borrador",
        placa: "ABC123",
        vin: "X",
        tipoTramite: "Traspaso",
        href: "/tramites/11111111-1111-4111-a111-111111111111",
      },
    ]);
    expect(next.phase).toBe("showing_tramites");
    expect(next.tramiteResults).toHaveLength(1);
    expect(next.showBackToSearch).toBe(true);
    expect(next.isTyping).toBe(false);
  });

  it("éxito de validaciones", () => {
    const awaiting = applySelectIntent(createInitialState(), "cliente")!.next;
    const branched = applyUserText(awaiting, "900");
    const loading = applyClientBranch(branched, "validaciones");
    const next = applyValidacionesSuccess(loading, [
      {
        id: "v1",
        name: "Ana",
        documentType: "CC",
        documentNumber: "900",
        status: "aprobado",
        createdAt: "2026-01-01",
        instanceId: null,
        href: "/?m=validaciones&q=900",
        tramiteHref: null,
      },
    ]);
    expect(next.phase).toBe("showing_validaciones");
    expect(next.validacionResults).toHaveLength(1);
  });

  it("regresar restaura menú completo de sesiones", () => {
    const awaiting = applySelectIntent(createInitialState(), "placa")!.next;
    const loading = applyUserText(awaiting, "ABC123");
    const shown = applyTramitesSuccess(loading, "placa", []);
    const next = applyBackToSearch(shown);
    expect(next.phase).toBe("idle");
    expect(next.showSessionMenu).toBe(true);
  });

  it("texto libre sin intent muestra hint unificado", () => {
    const next = applyUserText(createInitialState(), "hola");
    expect(next.messages.at(-1)?.text).toBe(DR_FLIT_FREE_TEXT_HINT);
    expect(next.showSessionMenu).toBe(true);
  });

  it("Necesito ayuda muestra artículos del manual", () => {
    const awaiting = applySelectHelpOption(createInitialState(), "necesito-ayuda")!;
    expect(awaiting.phase).toBe("awaiting_help_query");
    expect(awaiting.messages.at(-1)?.text).toBe(buildHelpValuePrompt());
    const next = applyUserText(awaiting, "como creo un tramite");
    expect(next.phase).toBe("showing_help");
    expect(next.helpResults?.length).toBeGreaterThan(0);
    expect(next.helpResults?.[0]?.href).toMatch(/^\/manual\//);
    expect(next.showBackToSearch).toBe(true);
  });

  it("Necesito ayuda sin match ofrece home del manual", () => {
    const awaiting = applySelectHelpOption(createInitialState(), "necesito-ayuda")!;
    const next = applyUserText(awaiting, "xyzzy-no-existe-12345");
    expect(next.phase).toBe("showing_help");
    expect(next.helpResults).toEqual([]);
    expect(next.manualHomeHref).toBe("/manual");
  });

  it("Soporte muestra panel de canales", () => {
    const next = applySelectHelpOption(createInitialState(), "soporte")!;
    expect(next.phase).toBe("showing_support");
    expect(next.showSupportInfo).toBe(true);
    expect(next.showBackToSearch).toBe(true);
  });

  it("regresar desde soporte restaura menú completo", () => {
    const support = applySelectHelpOption(createInitialState(), "soporte")!;
    const next = applyBackToSearch(support);
    expect(next.showSessionMenu).toBe(true);
    expect(next.showSupportInfo).toBe(false);
  });
});

describe("dr-flit-support", () => {
  it("URL de caso de soporte oficial", () => {
    expect(DR_FLIT_SUPPORT_CASE_URL).toBe("https://flitsas.com.co/SOPORTE/");
  });
});
