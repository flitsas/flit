import { describe, expect, it, beforeEach } from "vitest";

import { getArticleBySlug } from "@/lib/manual/catalog";

import {
  applyBackToSearch,
  applyClientBranch,
  applySelectHelpOption,
  applySelectIntent,
  applyTramitesSuccess,
  applyUserText,
  applyValidacionesSuccess,
  createInitialState,
  hasActiveConversation,
  isComposerEnabled,
  queryLabelForIntent,
  resetMessageIdSeq,
} from "../dr-flit-conversation";

import {
  buildGreeting,
  buildHelpValuePrompt,
  buildHistorialPlacaHref,
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

  it("HU-C — buildHistorialPlacaHref normaliza la placa", () => {
    expect(buildHistorialPlacaHref(" abc 123 ")).toBe("/?m=historial-placa&placa=ABC123");
  });

  it("HU-C — el atajo al historial solo se conserva con resultados y se limpia al volver", () => {
    const awaiting = applySelectIntent(createInitialState(), "placa")!.next;
    const loading = applyUserText(awaiting, "ABC123");
    const row = {
      id: "11111111-1111-4111-a111-111111111111",
      radicado: "R-1",
      fecha: "01/01/2026 08:00",
      estado: "borrador",
      placa: "ABC123",
      vin: "X",
      tipoTramite: "Traspaso",
      compania: null,
      href: "/tramites/11111111-1111-4111-a111-111111111111",
    };
    const href = buildHistorialPlacaHref("ABC123");

    const conResultados = applyTramitesSuccess(loading, "placa", [row], 1, href);
    expect(conResultados.historialPlacaHref).toBe(href);
    expect(hasActiveConversation(conResultados)).toBe(true);

    const sinResultados = applyTramitesSuccess(loading, "placa", [], 0, href);
    expect(sinResultados.historialPlacaHref).toBeNull();

    const back = applyBackToSearch(conResultados);
    expect(back.historialPlacaHref).toBeNull();
  });

  // HU12851 (Feature #12846) — "preasignacion de placas" dejó de ser un artículo propio del OT (el
  // módulo se retiró del manual); se prueba con "validar impronta", exclusivo del Organismo de
  // Tránsito, igual que antes lo era la preasignación.
  it("HU-F — applyUserText en ayuda respeta las audiencias", () => {
    const help = applySelectHelpOption(createInitialState(), "necesito-ayuda")!;
    const gestor = applyUserText(help, "validar impronta", {
      helpAudiences: ["Todos", "Gestor"],
    });
    expect(gestor.phase).toBe("showing_help");
    expect((gestor.helpResults ?? []).some((h) => h.audience === "Organismo de Tránsito")).toBe(false);

    const ot = applyUserText(help, "validar impronta", {
      helpAudiences: ["Todos", "Organismo de Tránsito"],
    });
    expect((ot.helpResults ?? []).some((h) => h.slug === "2-ot/11-validar-impronta")).toBe(true);
  });

  it("HU-G — applySelectHelpOption con artículo de contexto lo ofrece como primer chip", () => {
    const article = getArticleBySlug("1-gestor/2-crear-tramite")!;
    const next = applySelectHelpOption(createInitialState(), "necesito-ayuda", {
      contextArticle: article,
    })!;
    expect(next.phase).toBe("awaiting_help_query");
    expect(next.helpResults).toHaveLength(1);
    expect(next.helpResults![0]!.href).toBe("/manual/1-gestor/2-crear-tramite");
    expect(next.messages[next.messages.length - 1]!.text).toContain(article.title);
    expect(isComposerEnabled(next)).toBe(true);

    // Al escribir, la búsqueda reemplaza la sugerencia.
    const searched = applyUserText(next, "documentos de matricula");
    expect(searched.phase).toBe("showing_help");
    expect(searched.helpResults!.some((h) => h.slug === "1-gestor/3-documentos-tramite")).toBe(true);
  });

  it("Normativa — la opción ofrece la resolución como fuente principal con su PDF", () => {
    const next = applySelectHelpOption(createInitialState(), "normativa")!;
    expect(next.phase).toBe("showing_help");
    expect(next.session).toBe("ayuda");
    expect(next.showBackToSearch).toBe(true);
    expect(next.helpResults).toHaveLength(1);
    const r = next.helpResults![0]!;
    expect(r.slug).toBe("5-normativa/1-resolucion-20233040017145-2023");
    expect(r.primarySource).toBe(true);
    expect(r.sourceHref).toBe("/legal/resolucion-20233040017145-2023-mintransporte.pdf");
    expect(r.sourceLabel).toBe("Abrir la norma (PDF)");
    expect(next.messages[next.messages.length - 1]!.text).toContain("20233040017145");
    expect(hasActiveConversation(next)).toBe(true);
  });

  it("Normativa — una pregunta normativa en «Necesito ayuda» trae la norma con su PDF; una operativa no", () => {
    const help = applySelectHelpOption(createInitialState(), "necesito-ayuda")!;
    const norma = applyUserText(help, "qué dice la norma sobre la preasignación de placa", {
      helpAudiences: ["Todos", "Gestor"],
    });
    expect(norma.helpResults![0]!.sourceHref).toBe(
      "/legal/resolucion-20233040017145-2023-mintransporte.pdf",
    );
    const howto = applyUserText(help, "cómo creo un trámite", { helpAudiences: ["Todos", "Gestor"] });
    expect(howto.helpResults![0]!.slug).toBe("1-gestor/2-crear-tramite");
    expect(howto.helpResults![0]!.sourceHref).toBeUndefined();
  });

  it("HU-B — el intent trámite pide el radicado, no un GUID", () => {
    const next = applySelectIntent(createInitialState(), "tramite")!.next;
    const last = next.messages[next.messages.length - 1]!.text;
    expect(last).toMatch(/radicado/i);
    expect(last).not.toMatch(/GUID/);
    expect(queryLabelForIntent("tramite")).toBe("radicado");
  });

  it("éxito con total mayor al mostrado avisa cuántos se muestran (HU #12104)", () => {
    const awaiting = applySelectIntent(createInitialState(), "placa")!.next;
    const loading = applyUserText(awaiting, "ABC123");
    const row = {
      id: "11111111-1111-4111-a111-111111111111",
      radicado: "R-1",
      fecha: "01/01/2026 08:00",
      estado: "borrador",
      placa: "ABC123",
      vin: "X",
      tipoTramite: "Traspaso",
      compania: null,
      href: "/tramites/11111111-1111-4111-a111-111111111111",
    };
    const next = applyTramitesSuccess(loading, "placa", [row], 37);
    const last = next.messages[next.messages.length - 1]!.text;
    expect(last).toContain("37 trámites");
    expect(last).toContain("Te muestro los 1 más recientes");

    const exacto = applyTramitesSuccess(loading, "placa", [row], 1);
    expect(exacto.messages[exacto.messages.length - 1]!.text).not.toContain("Te muestro");
  });

  it("éxito de trámites muestra resultados", () => {
    const awaiting = applySelectIntent(createInitialState(), "placa")!.next;
    const loading = applyUserText(awaiting, "ABC123");
    const next = applyTramitesSuccess(loading, "placa", [
      {
        id: "11111111-1111-4111-a111-111111111111",
        radicado: "R-1",
        fecha: "01/01/2026 08:00",
        estado: "borrador",
        placa: "ABC123",
        vin: "X",
        tipoTramite: "Traspaso",
        compania: null,
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

  it("hasActiveConversation distingue menú inicial de consulta en curso", () => {
    const idle = createInitialState("Juan");
    expect(hasActiveConversation(idle)).toBe(false);
    const awaiting = applySelectIntent(idle, "placa")!.next;
    expect(hasActiveConversation(awaiting)).toBe(true);
    const back = applyBackToSearch(awaiting);
    expect(hasActiveConversation(back)).toBe(false);
  });
});

describe("dr-flit-support", () => {
  it("URL de caso de soporte oficial", () => {
    expect(DR_FLIT_SUPPORT_CASE_URL).toBe("https://flitsas.com.co/SOPORTE/");
  });
});
