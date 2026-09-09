// HU-01 (Feature #12201) — CF-21: cuatro estados internos, TRES etiquetas de usuario.
// Uso de ejemplo: standaloneDocumentStatusLabel("processing") → "En proceso".
import { describe, expect, it } from "vitest";
import {
  STANDALONE_DOCUMENT_STATUSES,
  STANDALONE_DOCUMENT_STATUS_OPTIONS,
  isStandaloneDocumentStatus,
  standaloneDocumentStatusLabel,
  standaloneDocumentStatusQuery,
  standaloneDocumentStatusView,
} from "../status-labels";

describe("status-labels — mapa de estados (CF-21)", () => {
  it("traduce 'generated' a «Generado» y 'error' a «Error»", () => {
    expect(standaloneDocumentStatusLabel("generated")).toBe("Generado");
    expect(standaloneDocumentStatusLabel("error")).toBe("Error");
  });

  it("colapsa 'pending' y 'processing' en la MISMA etiqueta «En proceso»", () => {
    expect(standaloneDocumentStatusLabel("pending")).toBe("En proceso");
    expect(standaloneDocumentStatusLabel("processing")).toBe("En proceso");
    expect(standaloneDocumentStatusLabel("pending")).toBe(standaloneDocumentStatusLabel("processing"));
  });

  it("cubre los cuatro estados internos y produce exactamente tres etiquetas distintas", () => {
    expect(STANDALONE_DOCUMENT_STATUSES).toHaveLength(4);
    const labels = new Set(STANDALONE_DOCUMENT_STATUSES.map(standaloneDocumentStatusLabel));
    expect(labels).toEqual(new Set(["Generado", "Error", "En proceso"]));
  });

  it("el filtro de estado ofrece exactamente tres opciones (CF-18)", () => {
    expect(STANDALONE_DOCUMENT_STATUS_OPTIONS.map((o) => o.label)).toEqual([
      "Generado",
      "Error",
      "En proceso",
    ]);
  });

  it("la opción «En proceso» consulta los dos estados internos", () => {
    expect(standaloneDocumentStatusQuery("en_proceso")).toEqual(["pending", "processing"]);
    expect(standaloneDocumentStatusQuery("generated")).toEqual(["generated"]);
    expect(standaloneDocumentStatusQuery("")).toBeUndefined();
    expect(standaloneDocumentStatusQuery(undefined)).toBeUndefined();
  });

  it("cada estado trae un texto visible además del tono: el color nunca va solo (CF-22)", () => {
    for (const status of STANDALONE_DOCUMENT_STATUSES) {
      const view = standaloneDocumentStatusView(status);
      expect(view.label.trim().length).toBeGreaterThan(0);
      expect(view.tone).toBeTruthy();
    }
  });

  it("un estado desconocido degrada a «En proceso» neutro, sin inventar etiqueta", () => {
    expect(isStandaloneDocumentStatus("queued")).toBe(false);
    expect(standaloneDocumentStatusView("queued")).toEqual({
      label: "En proceso",
      tone: "neutral",
      filter: "en_proceso",
    });
  });

  it("contrato: la vista declara label, tone y filter para todo estado interno", () => {
    for (const status of STANDALONE_DOCUMENT_STATUSES) {
      const view = standaloneDocumentStatusView(status);
      expect(view).toHaveProperty("label");
      expect(view).toHaveProperty("tone");
      expect(view).toHaveProperty("filter");
    }
  });
});

describe("status-labels — fuente única del módulo (CF-21)", () => {
  it("ningún otro archivo del módulo declara las etiquetas de estado", async () => {
    const { readdirSync, readFileSync, statSync } = await import("node:fs");
    const { join } = await import("node:path");

    const moduleDir = join(process.cwd(), "components", "admin", "generacion-documental");
    const pageDir = join(process.cwd(), "app", "admin", "generacion-documental");
    const apiFiles = [
      join(process.cwd(), "lib", "api", "admin-generacion-documental.ts"),
      join(process.cwd(), "lib", "api", "types-generacion-documental.ts"),
    ];

    const walk = (dir: string): string[] =>
      readdirSync(dir).flatMap((name) => {
        const full = join(dir, name);
        if (statSync(full).isDirectory()) {
          return name === "__tests__" ? [] : walk(full);
        }
        return /\.(ts|tsx)$/.test(name) ? [full] : [];
      });

    const files = [...walk(moduleDir), ...walk(pageDir), ...apiFiles].filter(
      (f) => !f.endsWith("status-labels.ts"),
    );

    const culprits = files.filter((f) => {
      const content = readFileSync(f, "utf8");
      // Se buscan las etiquetas como literales de cadena; los comentarios que las citan
      // (sin comillas) no cuentan.
      return /["'`](Generado|En proceso)["'`]/.test(content);
    });

    expect(culprits).toEqual([]);
  });
});
