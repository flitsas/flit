// HU #12415 — pruebas de la regla de lint acotada `no-literal-brand-color`.
//
// Uso de ejemplo:
//   const rule = createRule(new Set(["components/atom/Login.tsx:122:#00dbd5"]));
//   ruleTester.run("no-literal-brand-color", rule, { valid: [...], invalid: [...] });
//
// Nota: ESLint's RuleTester detecta el test runner activo (vitest expone
// `describe`/`it` globales) y crea sus propios bloques `describe`/`it` al
// llamar `.run(...)`. Por eso cada caso se invoca a nivel de módulo (dentro de
// un `describe` propio), nunca anidado dentro de un `it()` — anidar
// describe/it desde dentro de un `it()` revienta con
// "Calling the suite function inside test function is not allowed".
import { describe } from "vitest";
import { RuleTester } from "eslint";
import { createRule } from "../no-literal-brand-color.mjs";

const ruleTester = new RuleTester({
  languageOptions: {
    ecmaVersion: 2022,
    sourceType: "module",
    parserOptions: { ecmaFeatures: { jsx: true } },
  },
});

describe("no-literal-brand-color — contrato del inventario congelado", () => {
  ruleTester.run("hex documentado en el baseline (archivo:línea:hex) no se reporta", createRule(new Set(["test.tsx:1:#557eff"])), {
    valid: [
      {
        filename: "test.tsx",
        code: 'const style = { color: "#557eff" };',
      },
    ],
    invalid: [],
  });

  ruleTester.run("hex de marca ausente del baseline (color nuevo) se reporta", createRule(new Set()), {
    valid: [],
    invalid: [
      {
        filename: "test.tsx",
        code: 'const style = { color: "#557eff" };',
        errors: [{ messageId: "literalBrandColor" }],
      },
    ],
  });

  ruleTester.run("mismo hex en línea distinta a la documentada se reporta", createRule(new Set(["test.tsx:1:#ff4e00"])), {
    valid: [],
    invalid: [
      {
        filename: "test.tsx",
        // El baseline documenta la línea 1; aquí el hex cae en la línea 2
        // (tras el salto de línea inicial) — no matchea la clave archivo:línea:hex.
        code: '\nconst style = { color: "#ff4e00" };',
        errors: [{ messageId: "literalBrandColor" }],
      },
    ],
  });

  ruleTester.run("hex fuera de los 6 de marca (neutro/estado) no se reporta", createRule(new Set()), {
    valid: [
      { filename: "test.tsx", code: 'const style = { color: "#ffffff" };' },
      { filename: "test.tsx", code: 'const style = { color: "#4F74C9" };' },
    ],
    invalid: [],
  });

  ruleTester.run("detecta hex de marca dentro de un template literal (linear-gradient)", createRule(new Set()), {
    valid: [],
    invalid: [
      {
        filename: "test.tsx",
        code: "const bg = `linear-gradient(135deg,#557EFF,#00DBD5)`;",
        errors: [{ messageId: "literalBrandColor" }, { messageId: "literalBrandColor" }],
      },
    ],
  });
});
