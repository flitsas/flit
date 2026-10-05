// Capturas del cliente: viven SOLO en memoria del flujo hasta el envío final (HU #13294/#13295).
import type { ManualSubmitFiles } from "./types";

export type CaptureKey = keyof ManualSubmitFiles;
export type Captures = Partial<ManualSubmitFiles>;
