import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  activateAiProvider,
  listAiAlerts,
  listAiTools,
  listAiTransitions,
  putAiToolSettings,
} from "../admin-plataforma-ai";

const apiFetch = vi.fn();

vi.mock("../client", () => ({
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));

describe("admin-plataforma-ai client (HU #13511 / #13512)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });

  it("GET tools", async () => {
    apiFetch.mockResolvedValueOnce({ tools: [] });
    await listAiTools();
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/admin/plataforma/ai/tools", expect.objectContaining({ method: "GET" }));
  });

  it("PUT settings", async () => {
    apiFetch.mockResolvedValueOnce({ toolCode: "ocr" });
    await putAiToolSettings("ocr", {
      autoReturnToPrimary: true,
      providers: [{ providerCode: "anthropic", enabled: true, priorityRank: 1 }],
    });
    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/admin/plataforma/ai/tools/ocr/settings",
      expect.objectContaining({ method: "PUT" }),
    );
  });

  it("POST activate", async () => {
    apiFetch.mockResolvedValueOnce({ toolCode: "ocr", activeProvider: "openai" });
    await activateAiProvider("ocr", { providerCode: "openai" });
    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/admin/plataforma/ai/tools/ocr/activate",
      expect.objectContaining({
        method: "POST",
        body: { providerCode: "openai" },
      }),
    );
  });

  it("GET transitions con pageSize 20", async () => {
    apiFetch.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await listAiTransitions("dr_flit", 2, 20);
    expect(String(apiFetch.mock.calls[0][0])).toContain(
      "/api/v1/admin/plataforma/ai/tools/dr_flit/transitions?",
    );
    expect(String(apiFetch.mock.calls[0][0])).toContain("page=2");
    expect(String(apiFetch.mock.calls[0][0])).toContain("pageSize=20");
  });

  it("GET alerts acepta { items } o array", async () => {
    apiFetch.mockResolvedValueOnce({ items: [{ id: "a1", acked: false }] });
    expect(await listAiAlerts()).toHaveLength(1);
    apiFetch.mockResolvedValueOnce([{ id: "a2", acked: true }]);
    expect(await listAiAlerts()).toHaveLength(1);
  });
});
