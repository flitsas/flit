import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api/types";
import {
  mapSendTestApiError,
  mapSendTestOutcome,
} from "@/lib/notificaciones/mensajes-envio-prueba";

// Uso de ejemplo:
// mapSendTestApiError(new ApiError(400, "x", { error: "buzon_no_configurado" }))
//   → { cause: "buzon_no_configurado", productMessage: "No hay un buzón..." }
// mapSendTestOutcome({ success: true, outcome: "Sent", isConsoleTransport: false, ... })
//   → { kind: "sent", productMessage: "...", disclaimer: "..." }

describe("mapSendTestApiError — AC4 (causa de lista cerrada)", () => {
  it("mapea buzon_no_configurado (AC5) a un mensaje de producto que pide configurar el buzón", () => {
    const result = mapSendTestApiError(
      new ApiError(400, "Error 400", { error: "buzon_no_configurado", message: "detalle interno" }),
    );
    expect(result.cause).toBe("buzon_no_configurado");
    expect(result.productMessage).toMatch(/configúralo/i);
    expect(result.productMessage).not.toContain("detalle interno");
  });

  it("mapea configuracion_incompleta (reemplazo real de 'canal sin adaptador')", () => {
    const result = mapSendTestApiError(
      new ApiError(400, "Error 400", { error: "configuracion_incompleta", message: "smtp host missing" }),
    );
    expect(result.cause).toBe("configuracion_incompleta");
    expect(result.productMessage).not.toContain("smtp host missing");
    expect(result.productMessage.length).toBeGreaterThan(0);
  });

  it("mapea canal_invalido", () => {
    const result = mapSendTestApiError(new ApiError(400, "Error 400", { error: "canal_invalido" }));
    expect(result.cause).toBe("canal_invalido");
  });

  it("mapea fallo_render (500)", () => {
    const result = mapSendTestApiError(
      new ApiError(500, "Error 500", { error: "fallo_render", message: "Liquid syntax error at line 12" }),
    );
    expect(result.cause).toBe("fallo_render");
    expect(result.productMessage).not.toContain("Liquid syntax error");
  });

  it("mapea limite_frecuencia (429) e incluye retryAfterSeconds en el mensaje", () => {
    const result = mapSendTestApiError(
      new ApiError(429, "Error 429", {
        error: "limite_frecuencia",
        message: "rate limited",
        retryAfterSeconds: 270,
      }),
    );
    expect(result.cause).toBe("limite_frecuencia");
    expect(result.retryAfterSeconds).toBe(270);
    expect(result.productMessage).toContain("270");
  });

  it("edge case — causa no reconocida por el frontend cae en 'desconocida' sin filtrar el mensaje crudo", () => {
    const result = mapSendTestApiError(
      new ApiError(500, "Error 500", { error: "algo_nuevo_del_backend", message: "detalle interno x" }),
    );
    expect(result.cause).toBe("desconocida");
    expect(result.productMessage).not.toContain("detalle interno x");
  });

  it("contrato — un error que no es ApiError (p. ej. de red) también resuelve a un mensaje seguro", () => {
    const result = mapSendTestApiError(new TypeError("Failed to fetch"));
    expect(result.cause).toBe("desconocida");
    expect(result.productMessage.length).toBeGreaterThan(0);
  });
});

describe("mapSendTestOutcome — AC3/AC4 (resultado 200: éxito o fallo de transporte)", () => {
  it("outcome Sent con éxito real: mensaje positivo + disclaimer de entrega no garantizada", () => {
    const outcome = mapSendTestOutcome({
      success: true,
      outcome: "Sent",
      message: "ok",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: "no-reply@flit.com.co",
      senderName: "FLIT",
      sentAt: "2026-08-10T12:00:00Z",
      isConsoleTransport: false,
    });
    expect(outcome.kind).toBe("sent");
    expect(outcome.disclaimer).toMatch(/no garantiza/i);
    expect(outcome.consoleTransportNotice).toBeUndefined();
  });

  it("outcome Sent con transporte de consola: declara honestamente que no hubo correo real (AC8)", () => {
    const outcome = mapSendTestOutcome({
      success: true,
      outcome: "Sent",
      message: "ok",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: null,
      senderName: null,
      sentAt: "2026-08-10T12:00:00Z",
      isConsoleTransport: true,
    });
    expect(outcome.kind).toBe("sent");
    expect(outcome.consoleTransportNotice).toMatch(/no se envió un correo real/i);
  });

  it("edge case — success:false con outcome TransportFailed (200) es un fallo de transporte, no un error HTTP", () => {
    const outcome = mapSendTestOutcome({
      success: false,
      outcome: "TransportFailed",
      message: "SMTP server rejected recipient",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: "no-reply@flit.com.co",
      senderName: "FLIT",
      sentAt: null,
      isConsoleTransport: false,
    });
    expect(outcome.kind).toBe("transport-failed");
    expect(outcome.productMessage).not.toContain("SMTP server rejected recipient");
    expect(outcome.disclaimer).toMatch(/no garantiza/i);
  });
});
