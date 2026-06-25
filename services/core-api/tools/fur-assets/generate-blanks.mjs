/**
 * ONE-TIME: genera fur-formulario-p1-blank.pdf y fur-instrucciones-p2-blank.pdf
 * desde plantillas BackCrud. No forma parte del runtime de core-api (solo assets).
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import handlebars from 'handlebars';
import handlebarsHelpers from 'handlebars-helpers';
import puppeteer from 'puppeteer-core';

handlebarsHelpers({ handlebars });

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const BACKCRUD = process.env.BACKCRUD_ROOT ?? 'D:\\FLIT\\BackCrudTransfer_master';
const OUT = path.resolve(
  __dirname,
  '../../src/Flit.Infrastructure/Documents/Fur/Templates',
);

const BLANK = {
  now: '',
  traffic_secretary_name: '',
  traffic_secretary_city: '',
  traffic_secretary_code: '',
  processing_day: '',
  processing_month: '',
  processing_year: '',
  plate_letter: '',
  plate_number: '',
  vehicle_owner_signature: '',
  vehicle_buyer_signature: '',
  vehicleOwnerDocumentType: '',
};

const PDF_OPTS = {
  format: 'Legal',
  landscape: true,
  printBackground: true,
  scale: 0.7,
  margin: { top: '1cm', bottom: '1cm', left: '2cm', right: '2cm' },
  preferCSSPageSize: true,
};

function chrome() {
  for (const p of [
    process.env.CHROME_EXECUTABLE,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ].filter(Boolean)) {
    if (fs.existsSync(p)) return p;
  }
  throw new Error('Chrome/Edge no encontrado para generación de assets');
}

async function render(templateRel, pageRanges) {
  const templatePath = path.join(BACKCRUD, templateRel);
  const source = fs.readFileSync(templatePath, 'utf8');
  const html = handlebars.compile(source)(BLANK);
  const browser = await puppeteer.launch({
    executablePath: chrome(),
    headless: true,
    args: ['--no-sandbox'],
  });
  try {
    const page = await browser.newPage();
    await page.setContent(html, { waitUntil: 'networkidle0', timeout: 120_000 });
    return await page.pdf({ ...PDF_OPTS, pageRanges: pageRanges ?? '' });
  } finally {
    await browser.close();
  }
}

async function main() {
  fs.mkdirSync(OUT, { recursive: true });
  const matriculaTpl =
    'src/assets/templates/pdf/vehicle-registration-fur/registration.AUTOMOTOR.hbs';

  const full = await render(matriculaTpl);
  fs.writeFileSync(path.join(OUT, 'fur-matricula-full-blank.pdf'), full);

  const p1 = await render(matriculaTpl, '1');
  const p2 = await render(matriculaTpl, '2');
  fs.writeFileSync(path.join(OUT, 'fur-formulario-p1-blank.pdf'), p1);
  fs.writeFileSync(path.join(OUT, 'fur-instrucciones-p2-blank.pdf'), p2);

  console.log('Plantillas guardadas en', OUT);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
