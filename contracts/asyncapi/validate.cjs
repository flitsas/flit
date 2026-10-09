// Valida los contratos AsyncAPI de esta carpeta con @asyncapi/parser (versión fija en CI, contracts.yml).
// Sustituye a `@asyncapi/cli validate`, roto desde 2026-08 (docs/ci/asyncapi-cli-broken-generator-hooks.md)
// y que hoy además exige Node 24. Falla si algún archivo tiene diagnósticos de severidad «error».
//
// Uso: ASYNCAPI_PARSER_DIR=<carpeta con node_modules/@asyncapi/parser> node contracts/asyncapi/validate.cjs
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');

const parserDir = process.env.ASYNCAPI_PARSER_DIR;
if (!parserDir) {
  console.error('Falta ASYNCAPI_PARSER_DIR (carpeta donde se instaló @asyncapi/parser).');
  process.exit(2);
}
const { Parser, fromFile } = createRequire(path.join(path.resolve(parserDir), 'noop.js'))('@asyncapi/parser');

const ERROR = 0; // DiagnosticSeverity.Error
const dir = __dirname;
const files = fs.readdirSync(dir).filter((f) => /\.ya?ml$/.test(f)).sort();

(async () => {
  if (files.length === 0) {
    console.error(`No hay contratos AsyncAPI en ${dir}.`);
    process.exit(1);
  }
  const parser = new Parser();
  let failed = false;
  for (const file of files) {
    const { diagnostics } = await fromFile(parser, path.join(dir, file)).parse();
    const errors = diagnostics.filter((d) => d.severity === ERROR);
    if (errors.length > 0) {
      failed = true;
      console.error(`✗ ${file}`);
      for (const e of errors) console.error(`  ${e.path.join('.') || '(raíz)'}: ${e.message}`);
    } else {
      console.log(`✓ ${file}`);
    }
  }
  process.exit(failed ? 1 : 0);
})().catch((err) => {
  console.error(err);
  process.exit(1);
});
