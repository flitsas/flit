// Declaración de tipos para no-literal-brand-color.mjs — HU #12415 (corrección de
// typecheck). El módulo fuente es JS plano (sin JSDoc de tipos); TypeScript
// infiere `meta.type: string` (ensanchado) para el objeto de regla, lo que no
// es asignable al `RuleType` ("problem" | "suggestion" | "layout") que exige
// `RuleTester.run()` en ESLint 9 (`@eslint/core`). Esta declaración fija el tipo
// real (`Rule.RuleModule`) sin recurrir a `any`/`@ts-ignore`.
//
// Uso de ejemplo:
//   import { createRule } from "./no-literal-brand-color.mjs"; // resuelve aquí
//   const rule = createRule(new Set(["archivo.tsx:1:#557eff"]));
import type { Rule } from "eslint";

export declare function createRule(baseline: Set<string>): Rule.RuleModule;

declare const rule: Rule.RuleModule;
export default rule;
