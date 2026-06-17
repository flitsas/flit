# Templates de evidencia QA — playwright-runner

Archivos TypeScript temporales generados por el skill `playwright-runner` durante la ejecución de TCs.
Todos usan el prefijo `_qa_evidence_` y se eliminan al finalizar (ver PASO 7 en SKILL.md).

---

## `e2e/_qa_evidence_capture.ts` — Helper de capturas (QaCapture)

```typescript
// e2e/_qa_evidence_capture.ts — TEMPORAL, generado por playwright-runner skill
import type { Page, TestInfo } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';

export class QaCapture {
  private idx = 0;
  readonly steps: Array<{ label: string; status: 'pass' | 'fail'; shot?: string }> = [];

  constructor(
    private page: Page,
    private testInfo: TestInfo,
    readonly tcId: string,
  ) {}

  async step(label: string, fn: () => Promise<void>) {
    let status: 'pass' | 'fail' = 'pass';
    try { await fn(); }
    catch (e) { status = 'fail'; throw e; }
    finally {
      const shot = await this._shot(`step${String(++this.idx).padStart(2,'0')}_${label.toLowerCase().replace(/[^a-z0-9]+/g,'-').slice(0,40)}`);
      this.steps.push({ label, status, shot });
    }
  }

  async shot(name: string) { return this._shot(name); }

  private async _shot(name: string): Promise<string | undefined> {
    try {
      const p = path.join(this.testInfo.outputDir, `${this.tcId}_${name}.png`);
      await this.page.screenshot({ path: p, fullPage: false });
      await this.testInfo.attach(`${this.tcId}_${name}`, { path: p, contentType: 'image/png' });
      return p;
    } catch { return undefined; }
  }
}
```

---

## `e2e/_qa_evidence_reporter.ts` — Reporter de evidencia (QaEvidenceReporter)

```typescript
// e2e/_qa_evidence_reporter.ts — TEMPORAL, generado por playwright-runner skill
import type { Reporter, Suite, TestCase, TestResult, FullResult } from '@playwright/test/reporter';
import * as fs from 'fs';
import * as path from 'path';

interface TC {
  tcId: string; title: string; status: string; duration: number;
  attachments: Array<{name:string;path?:string;contentType:string}>;
  errors: string[];
}

export default class QaEvidenceReporter implements Reporter {
  private tcs: TC[] = [];
  private start = new Date();
  private get huId()    { return process.env.QA_HU_ID ?? 'local'; }
  private get build()   { return process.env.QA_BUILD ?? 'local'; }
  private get ambiente(){ return process.env.QA_AMBIENTE ?? 'DEV'; }
  private get orgUrl()  { return process.env.AZURE_ORG_URL ?? ''; }
  private get project() { return process.env.AZURE_PROJECT_NAME ?? ''; }
  private get pat()     { return process.env.AZURE_PAT ?? ''; }

  onBegin() { this.start = new Date(); console.log(`\n[qa-evidence] HU #${this.huId} | ${this.ambiente}\n`); }

  onTestEnd(test: TestCase, result: TestResult) {
    const m = test.title.match(/QA_TC\d+/i);
    this.tcs.push({
      tcId: m?.[0].toUpperCase() ?? 'TC_UNKNOWN',
      title: test.title, status: result.status, duration: result.duration,
      attachments: result.attachments.map(a => ({ name: a.name, path: a.path, contentType: a.contentType })),
      errors: result.errors.map(e => e.message ?? ''),
    });
  }

  async onEnd(_r: FullResult) {
    const pass = this.tcs.filter(t => t.status === 'passed').length;
    const fail = this.tcs.filter(t => t.status === 'failed').length;
    const reportDir = this._buildDocs(pass, fail);
    if (this.huId !== 'local' && this.orgUrl && this.pat) await this._publishAdo(pass, fail, reportDir);
    else console.log(`\n[qa-evidence] Reporte: ${reportDir}/INDEX.md (sin ADO — faltan credenciales)\n`);
  }

  private _buildDocs(pass: number, fail: number): string {
    const date = this.start.toISOString().split('T')[0];
    const dir = path.resolve(process.cwd(), `qa-evidence/${date}/HU-${this.huId}`);
    fs.mkdirSync(dir, { recursive: true });

    for (const tc of this.tcs) {
      for (const a of tc.attachments) {
        if (a.path && fs.existsSync(a.path)) {
          const ext = a.contentType === 'video/webm' ? 'webm' : 'png';
          const dest = path.join(dir, `${tc.tcId}_${a.name}.${ext}`);
          try { fs.copyFileSync(a.path, dest); } catch {}
        }
      }
    }

    const tag = fail === 0 ? 'QA_PDN' : 'QA_NOVEDAD';
    const rows = this.tcs.map(tc => {
      const icon = tc.status === 'passed' ? '✅' : tc.status === 'failed' ? '❌' : '⏭️';
      const shots = tc.attachments.filter(a => a.contentType === 'image/png').length;
      const hasVid = tc.attachments.some(a => a.contentType === 'video/webm');
      return `| [${tc.tcId}](./${tc.tcId}.md) | ${tc.title} | ${icon} ${tc.status} | ${(tc.duration/1000).toFixed(1)}s | ${shots} | ${hasVid ? 'Sí' : '—'} |`;
    }).join('\n');

    fs.writeFileSync(path.join(dir, 'INDEX.md'), [
      `# Evidencia QA — HU #${this.huId}`,
      `| Campo | Valor |`,`|---|---|`,
      `| **Resultado** | ${tag} |`,`| **Ambiente** | ${this.ambiente} |`,
      `| **Build** | ${this.build} |`,
      `| **Inicio** | ${this.start.toLocaleString('es-CO')} |`,
      `| **TCs** | ${pass}/${this.tcs.length} pasan |`,
      ``,`## Resultados`,``,
      `| TC | Título | Resultado | Duración | Screenshots | Video |`,
      `|----|--------|-----------|----------|-------------|-------|`,
      rows, ``, `---`,
      `*qa-evidence-reporter · FLIT · ${new Date().toISOString()}*`,
    ].join('\n'), 'utf-8');

    for (const tc of this.tcs) {
      const icon = tc.status === 'passed' ? '✅' : '❌';
      const shots = tc.attachments.filter(a => a.contentType === 'image/png')
        .map(a => `### ${a.name}\n![${a.name}](./${tc.tcId}_${a.name}.png)`).join('\n\n');
      const video = tc.attachments.find(a => a.contentType === 'video/webm');
      const errors = tc.errors.length ? `## Errores\n\`\`\`\n${tc.errors[0]?.slice(0,800)}\n\`\`\`` : '';
      fs.writeFileSync(path.join(dir, `${tc.tcId}.md`), [
        `# ${tc.tcId} — Evidencia`,``,
        `**${icon} ${tc.status}** | ${this.ambiente} | Build: ${this.build} | ${(tc.duration/1000).toFixed(1)}s`,``,
        shots ? `## Screenshots\n\n${shots}` : '',
        video ? `## Video\n\n[Ver video](./${tc.tcId}_video.webm)` : '',
        errors, `---`, `*${new Date().toISOString()}*`,
      ].filter(Boolean).join('\n\n'), 'utf-8');
    }

    console.log(`\n[qa-evidence] Reporte: ${dir}/INDEX.md\n`);
    return dir;
  }

  private async _publishAdo(pass: number, fail: number, reportDir: string) {
    const auth = Buffer.from(`:${this.pat}`).toString('base64');
    const proj = encodeURIComponent(this.project);
    const tag = fail === 0 ? '✅ QA_PDN' : '❌ QA_NOVEDAD';

    const indexPath = path.join(reportDir, 'INDEX.md');
    const indexAttUrl = fs.existsSync(indexPath)
      ? await this._uploadAttachment(indexPath, `QA_Evidencia_HU${this.huId}_INDEX.md`, auth, proj)
      : null;

    const rows = this.tcs.map(tc => {
      const icon = tc.status === 'passed' ? '✅' : tc.status === 'failed' ? '❌' : '⏭️';
      return `<tr><td><b>${tc.tcId}</b></td><td>${tc.title}</td><td>${icon} ${tc.status}</td><td>${(tc.duration/1000).toFixed(1)}s</td></tr>`;
    }).join('');
    const indexLink = indexAttUrl
      ? `<div><a href="${indexAttUrl}">Reporte completo de evidencia (INDEX.md)</a></div>` : '';
    const summaryHtml = [
      `<div><b>[playwright-runner]</b> HU #${this.huId} | <b>${tag}</b> — ${pass}/${this.tcs.length} TCs pasan | ${this.ambiente} | Build: ${this.build}</div>`,
      `<table><thead><tr><th>TC</th><th>Título</th><th>Resultado</th><th>Duración</th></tr></thead><tbody>${rows}</tbody></table>`,
      indexLink,
      `<div><small>${new Date().toISOString()}</small></div>`,
    ].join('');
    await this._postComment(this.huId, summaryHtml, auth, proj);

    const tasks = await this._fetchChildTasks(auth, proj);
    for (const tc of this.tcs) {
      const task = tasks.find(t => t.title.includes(tc.tcId));
      if (!task) { console.log(`  Sin Task ADO para ${tc.tcId}`); continue; }

      const icon = tc.status === 'passed' ? '✅' : tc.status === 'failed' ? '❌' : '⏭️';
      const err = tc.errors.length
        ? `<div><b>Error:</b> <pre style="font-size:11px">${tc.errors[0]?.slice(0, 600)}</pre></div>` : '';

      const imgBlocks: string[] = [];
      const shotAtts = tc.attachments.filter(a => a.contentType === 'image/png' && a.path && fs.existsSync(a.path));
      for (const att of shotAtts) {
        const adoUrl = await this._uploadAttachment(att.path!, `${tc.tcId}_${att.name}.png`, auth, proj);
        if (adoUrl) imgBlocks.push(`<div><b>${att.name}</b><br/><img src="${adoUrl}" style="max-width:900px;border:1px solid #ddd;margin:4px 0"/></div>`);
      }

      const videoAtt = tc.attachments.find(a => a.contentType === 'video/webm' && a.path && fs.existsSync(a.path));
      const videoLink = videoAtt
        ? await this._uploadAttachment(videoAtt.path!, `${tc.tcId}_video.webm`, auth, proj) : null;
      const videoBlock = videoLink ? `<div><a href="${videoLink}">Descargar video del flujo (${tc.tcId})</a></div>` : '';

      const tcHtml = [
        `<div><b>[playwright-runner]</b> ${tc.tcId} | ${icon} <b>${tc.status}</b></div>`,
        `<div>${this.ambiente} | Build: ${this.build} | ${(tc.duration/1000).toFixed(1)}s</div>`,
        err,
        imgBlocks.length ? `<div><b>Screenshots (${imgBlocks.length}):</b></div>${imgBlocks.join('')}` : '<div>Sin screenshots</div>',
        videoBlock,
        `<div><small>${new Date().toISOString()}</small></div>`,
      ].filter(Boolean).join('');

      await this._postComment(String(task.id), tcHtml, auth, proj);
      console.log(`  ${tc.tcId} → Task #${task.id} (${imgBlocks.length} screenshots)`);
    }
  }

  /**
   * Sube un archivo a ADO Attachments API y retorna la URL pública del adjunto.
   * IMPORTANTE: ADO requiere Content-Type: application/octet-stream para todos los binarios.
   */
  private async _uploadAttachment(filePath: string, fileName: string, auth: string, proj: string): Promise<string | null> {
    const https = await import('https'); const http = await import('http');
    try {
      if (!fs.existsSync(filePath)) { console.warn(`Archivo no encontrado: ${filePath}`); return null; }
      const fileBuffer = fs.readFileSync(filePath);
      const url = new URL(`${this.orgUrl}/${proj}/_apis/wit/attachments?fileName=${encodeURIComponent(fileName)}&api-version=7.1`);
      const t = url.protocol === 'https:' ? https.default : http.default;
      const raw = await new Promise<string>((res, rej) => {
        const req = t.request(
          { hostname: url.hostname, path: url.pathname + url.search, method: 'POST',
            headers: { Authorization: `Basic ${auth}`, 'Content-Type': 'application/octet-stream', 'Content-Length': fileBuffer.length } },
          (r) => { let d = ''; r.on('data', c => d += c); r.on('end', () => res(d)); },
        );
        req.on('error', rej); req.write(fileBuffer); req.end();
      });
      const resp = JSON.parse(raw);
      return resp.url ?? null;
    } catch (e) { console.warn(`No se pudo subir ${fileName}: ${e}`); return null; }
  }

  private async _postComment(wiId: string, html: string, auth: string, proj: string): Promise<void> {
    const https = await import('https'); const http = await import('http');
    const body = JSON.stringify({ text: html });
    const url = new URL(`${this.orgUrl}/${proj}/_apis/wit/workItems/${wiId}/comments?api-version=7.1-preview.3`);
    const t = url.protocol === 'https:' ? https.default : http.default;
    await new Promise<void>((res, rej) => {
      const req = t.request(
        { hostname: url.hostname, path: url.pathname+url.search, method: 'POST',
          headers: { Authorization: `Basic ${auth}`, 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } },
        r => { r.resume(); (r.statusCode??0)>=400 ? rej(new Error(`ADO ${r.statusCode}`)) : res(); },
      );
      req.on('error', rej); req.write(body); req.end();
    }).catch(e => console.warn(`ADO comment #${wiId}: ${e}`));
  }

  private async _fetchChildTasks(auth: string, proj: string): Promise<Array<{id:number;title:string}>> {
    const https = await import('https'); const http = await import('http');
    try {
      const url = new URL(`${this.orgUrl}/${proj}/_apis/wit/workItems/${this.huId}?$expand=relations&api-version=7.1`);
      const t = url.protocol === 'https:' ? https.default : http.default;
      const raw = await new Promise<string>((res,rej) => {
        const req = t.request({hostname:url.hostname,path:url.pathname+url.search,method:'GET',headers:{Authorization:`Basic ${auth}`}},
          r => { let d=''; r.on('data',c=>d+=c); r.on('end',()=>res(d)); });
        req.on('error',rej); req.end();
      });
      const wi = JSON.parse(raw);
      const childUrls: string[] = (wi.relations??[])
        .filter((r:{rel:string})=>r.rel==='System.LinkTypes.Hierarchy-Forward')
        .map((r:{url:string})=>r.url);
      const tasks: Array<{id:number;title:string}> = [];
      for (const cu of childUrls) {
        const curl = new URL(`${cu}?api-version=7.1`);
        const ct = curl.protocol==='https:' ? https.default : http.default;
        const cr = await new Promise<string>((res,rej) => {
          const req = ct.request({hostname:curl.hostname,path:curl.pathname+curl.search,method:'GET',headers:{Authorization:`Basic ${auth}`}},
            r=>{let d='';r.on('data',c=>d+=c);r.on('end',()=>res(d));});
          req.on('error',rej); req.end();
        });
        const c = JSON.parse(cr);
        if (c.fields?.['System.WorkItemType']==='Task') tasks.push({id:c.id, title:c.fields['System.Title']??''});
      }
      return tasks;
    } catch { return []; }
  }
}
```

---

## Plantilla de spec con QaCapture

Todo spec generado por el qa-agent debe seguir esta estructura. El import apunta al helper temporal creado en el PASO 2 del skill.

```typescript
/**
 * QA Spec — HU #{hu_id}
 * {titulo_hu}
 *
 * TCs cubiertos:
 *   TC01 — {titulo_tc01}
 *   TC02 — {titulo_tc02}
 */
import { expect, test } from '@playwright/test';
import { QaCapture } from './_qa_evidence_capture.js';

test.describe('{MODULO} — {descripcion_corta} (HU #{hu_id})', () => {

  test('{QA_TC01_MODULO_ALCANCE} — {escenario}', async ({ page }, testInfo) => {
    const ev = new QaCapture(page, testInfo, 'QA_TC01');

    await ev.step('Preparar datos / mock API', async () => {
      await page.route('**/api/**', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({/*...*/}) });
      });
    });

    await ev.step('Navegar a {ruta}', () => page.goto('{ruta}'));
    await ev.shot('{componente}-cargado');

    await ev.step('{descripcion del AC}', async () => {
      await expect(page.getByText('{texto_esperado}')).toBeVisible();
    });
    await ev.shot('{componente}-validado');
  });

  test('{QA_TC02_MODULO_ALCANCE} — {escenario}', async ({ page }, testInfo) => {
    const ev = new QaCapture(page, testInfo, 'QA_TC02');
    // ... misma estructura
  });

});
```

**Reglas del spec generado:**
- Cada test corresponde a exactamente un TC (`QA_TC{##}`)
- Cada acción relevante va dentro de `ev.step(...)` — nunca assertions sueltas sin paso
- `ev.shot(nombre)` se llama después de cada estado visual importante
- Nombres de shots descriptivos: `dashboard-vacio`, `formulario-con-error`, `modal-confirmacion`
- Si el AC involucra API: siempre mockear con `page.route(...)` dentro de un `ev.step`

---

## `playwright.evidence.config.ts` — Config de evidencia

> **Navegador visible vs headless**
> - **Local (QA humano supervisando):** previsualización con PASO 3b de la skill (`playwright.config` + `--headed`); corrida oficial con este config (`headless: false`) **y** `--headed` en CLI.
> - **CI sin display:** única excepción para `headless: true` en este archivo cuando `CI=true`; no aplicar headless en local sin pedido explícito del humano.

Config temporal que extiende el config del proyecto sin modificarlo:

```typescript
// playwright.evidence.config.ts — TEMPORAL, generado por playwright-runner skill
import { defineConfig } from '@playwright/test';
import baseConfig from './playwright.config.js';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? '{BASE_URL}';

export default defineConfig({
  ...baseConfig,
  reporter: [
    ['list'],
    ['html', { open: 'on-failure', outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'playwright-report/results.json' }],
    ['./e2e/_qa_evidence_reporter.ts'],
  ],
  use: {
    ...baseConfig.use,
    baseURL,
    headless: false,
    screenshot: 'on',
    video: 'on',
    trace: 'on',
    viewport: { width: 1280, height: 720 },
  },
  outputDir: 'test-results',
});
```

**CI sin entorno gráfico** — variante opcional del bloque `use` anterior (solo pipelines):

```typescript
headless: process.env.CI === 'true',
```

En máquina del desarrollador o QA, **no** usar esta variante; mantener `headless: false`.

Config mínimo desde cero (si el proyecto no tiene `playwright.config.ts`):

```typescript
// playwright.config.ts — generado por playwright-runner skill
import { defineConfig, devices } from '@playwright/test';
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['html', { open: 'on-failure' }], ['json', { outputFile: 'playwright-report/results.json' }]],
  timeout: 90_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: '{BASE_URL}',
    headless: false,
    screenshot: 'on',
    video: 'on',
    trace: 'on',
    viewport: { width: 1280, height: 720 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
```
