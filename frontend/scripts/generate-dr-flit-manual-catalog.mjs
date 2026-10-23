#!/usr/bin/env node
// HU #12920 (Épica #12718, ADR-0060 §3) — regenera el artefacto JSON del manual que usa el backend de
// DR. FLIT como contexto del LLM. Escribe:
//   frontend/public/dr-flit/manual-catalog.generated.json
//   services/core-api/src/Flit.Api/Content/dr-flit/manual-catalog.generated.json
//
// Uso de ejemplo:
//   pnpm manual:export
//
// El manual está escrito en TypeScript con el alias "@/", que Node no resuelve por sí solo. En vez de
// sumar un cargador de TS al repo, se reutiliza vitest (que ya los resuelve): corre la guarda de
// frescura en modo escritura. Así el generador y la guarda comparten exactamente la misma
// serialización (lib/manual/dr-flit-catalog-export.ts) y no pueden divergir.

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const vitestBin = path.join(frontendDir, "node_modules", "vitest", "vitest.mjs");

const result = spawnSync(
  process.execPath,
  [vitestBin, "run", "lib/manual/__tests__/generated-catalog-freshness.test.ts"],
  {
    cwd: frontendDir,
    stdio: "inherit",
    env: { ...process.env, DR_FLIT_MANUAL_EXPORT: "write" },
  },
);

if (result.status !== 0) {
  console.error("manual:export: no se pudo regenerar el artefacto del manual.");
  process.exit(result.status ?? 1);
}

console.log("manual:export: artefacto del manual regenerado. Commitea los dos archivos .generated.json.");
