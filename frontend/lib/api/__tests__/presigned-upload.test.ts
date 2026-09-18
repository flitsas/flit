import { afterEach, describe, expect, it, vi } from "vitest";
import { uploadFileToPresignedUrl } from "../presigned-upload";

describe("uploadFileToPresignedUrl", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("usa PUT con bytes crudos cuando method=PUT (Contabo)", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, text: async () => "" });
    vi.stubGlobal("fetch", fetchMock);

    const file = new File([new Uint8Array([1, 2, 3])], "doc.pdf", { type: "application/pdf" });
    await uploadFileToPresignedUrl(
      { url: "https://storage.example/obj?sig=1", method: "PUT", fields: {} },
      file,
    );

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain("storage.example");
    expect(init.method).toBe("PUT");
    expect(init.body).toBe(file);
    expect((init.headers as Record<string, string>)["Content-Type"]).toBe(
      "application/octet-stream",
    );
  });

  it("usa POST multipart cuando method ausente (MinIO/S3)", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, text: async () => "" });
    vi.stubGlobal("fetch", fetchMock);

    const file = new File([new Uint8Array([9])], "a.pdf", { type: "application/pdf" });
    await uploadFileToPresignedUrl(
      { url: "https://minio.example/bucket", fields: { key: "k", policy: "p" } },
      file,
    );

    expect(fetchMock).toHaveBeenCalledOnce();
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
    const form = init.body as FormData;
    expect(form.get("key")).toBe("k");
    expect(form.get("policy")).toBe("p");
    expect(form.get("file")).toBeInstanceOf(File);
  });
});
