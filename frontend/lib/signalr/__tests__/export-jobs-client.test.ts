import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const mocks = vi.hoisted(() => {
  const HubConnectionState = {
    Disconnected: "Disconnected",
    Connecting: "Connecting",
    Connected: "Connected",
    Disconnecting: "Disconnecting",
    Reconnecting: "Reconnecting",
  } as const;

  return {
    HubConnectionState,
    startMock: vi.fn(),
    invokeMock: vi.fn(),
    onMock: vi.fn(),
    stopMock: vi.fn(),
    onreconnectedMock: vi.fn(),
    oncloseMock: vi.fn(),
    getExportMock: vi.fn(),
  };
});

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        state: mocks.HubConnectionState.Disconnected as string,
        start: mocks.startMock,
        invoke: mocks.invokeMock,
        on: mocks.onMock,
        stop: mocks.stopMock,
        onreconnected: mocks.onreconnectedMock,
        onclose: mocks.oncloseMock,
      };
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: mocks.HubConnectionState,
    LogLevel: { Warning: 2 },
  };
});

vi.mock("@/lib/api/reporting-v2", () => ({
  getExport: (...args: unknown[]) => mocks.getExportMock(...args),
}));

vi.mock("@/lib/api/client", () => ({
  API_BASE_URL: "https://api.test",
  getToken: () => "jwt-token",
}));

import { watchExportJob } from "../export-jobs-client";

describe("watchExportJob", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    mocks.startMock.mockImplementation(async function (this: { state: string }) {
      void this;
    });
    mocks.invokeMock.mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("usa fallback polling cada 5s cuando el hub no conecta (AC3)", async () => {
    mocks.getExportMock
      .mockResolvedValueOnce({
        id: "job-1",
        status: "processing",
        reportType: "procedures",
        format: "csv",
        progressPct: 40,
        createdAt: "2026-07-30T00:00:00Z",
      })
      .mockResolvedValueOnce({
        id: "job-1",
        status: "completed",
        reportType: "procedures",
        format: "csv",
        progressPct: 100,
        createdAt: "2026-07-30T00:00:00Z",
        completedAt: "2026-07-30T00:01:00Z",
      });

    const onProgress = vi.fn();
    const onCompleted = vi.fn();
    const dispose = await watchExportJob(
      "job-1",
      { onProgress, onCompleted },
      { pollIntervalMs: 5000 },
    );

    await vi.advanceTimersByTimeAsync(5000);
    expect(mocks.getExportMock).toHaveBeenCalledWith("job-1");
    expect(onProgress).toHaveBeenCalledWith(
      expect.objectContaining({ jobId: "job-1", progressPct: 40 }),
    );

    await vi.advanceTimersByTimeAsync(5000);
    expect(onCompleted).toHaveBeenCalledWith(
      expect.objectContaining({ jobId: "job-1", status: "completed", progressPct: 100 }),
    );

    dispose();
  });

  it("invoca Subscribe y reenvía ExportProgress cuando el hub conecta (AC2)", async () => {
    mocks.startMock.mockImplementation(async function (this: { state: string }) {
      this.state = mocks.HubConnectionState.Connected;
    });

    const handlers = new Map<string, (payload: unknown) => void>();
    mocks.onMock.mockImplementation((event: string, cb: (payload: unknown) => void) => {
      handlers.set(event, cb);
    });

    const onProgress = vi.fn();
    const dispose = await watchExportJob("job-2", { onProgress });

    expect(mocks.invokeMock).toHaveBeenCalledWith("Subscribe", "job-2");
    handlers.get("ExportProgress")?.({ jobId: "job-2", status: "processing", progressPct: 60 });
    expect(onProgress).toHaveBeenCalledWith({
      jobId: "job-2",
      status: "processing",
      progressPct: 60,
    });

    dispose();
  });

  it("trata ExportFailed como evento terminal (HU #11107 AC4)", async () => {
    mocks.startMock.mockImplementation(async function (this: { state: string }) {
      this.state = mocks.HubConnectionState.Connected;
    });

    const handlers = new Map<string, (payload: unknown) => void>();
    mocks.onMock.mockImplementation((event: string, cb: (payload: unknown) => void) => {
      handlers.set(event, cb);
    });

    const onFailed = vi.fn();
    const dispose = await watchExportJob("job-fail", { onFailed });

    handlers.get("ExportFailed")?.({ jobId: "job-fail", status: "failed", progressPct: 40 });
    expect(onFailed).toHaveBeenCalledWith(
      expect.objectContaining({ jobId: "job-fail", status: "failed" }),
    );

    dispose();
  });
});
