# /// script
# requires-python = ">=3.11"
# ///
"""
Evalúa un prompt de OCR de comparendos contra un conjunto de referencia (HU #13469, #13470).

Llama a la Messages API de Anthropic igual que el backend (ProveedorOcrAnthropic: el PDF o la imagen como bloque
document/image, el prompt como texto, sin system ni streaming) y compara cada campo con la referencia escrita a mano.

El conjunto vive fuera del repositorio porque trae datos personales: una carpeta con
  conjunto.json            [{clave, archivo, paginas}]   (archivo relativo a --muestras)
  referencia/<clave>.json  {campos: {...}, origen: {...}} (ver INSTRUCCIONES-REFERENCIA.md)

Uso (la llave solo por variable de entorno, nunca en la línea de comandos):
  ANTHROPIC_API_KEY=... uv run evaluar.py --conjunto ../../../../context/gdc/ocr-eval \
      --muestras "<carpeta de la muestra>" --modelo claude-haiku-5-5 [--prompt prompt.txt] [--solo clave1,clave2]
  uv run evaluar.py --conjunto ... --recalificar <carpeta de una corrida>   (sin llamar al modelo)

Sin --prompt usa el de la semilla (PromptOcrSemilla.cs). Cada corrida queda en <conjunto>/corridas/<marca>/ con la
respuesta cruda de cada documento y reporte.md.
"""
from __future__ import annotations

import argparse
import base64
import concurrent.futures
import datetime as dt
import json
import os
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.request
from pathlib import Path

CAMPOS = [
    "numero_comparendo", "tipo_documento_infractor", "numero_documento_infractor", "nombre_infractor",
    "codigo_infraccion", "fecha_infraccion", "placa", "secretaria", "valor",
    "descripcion_infraccion", "fecha_notificacion", "hora_infraccion", "codigo_organismo", "es_fotomulta",
    "direccion", "tipo_vehiculo",
]
OBLIGATORIOS = CAMPOS[:9]  # CamposOcr.Obligatorios
TEXTO_LIBRE = {"nombre_infractor", "secretaria", "descripcion_infraccion", "direccion", "tipo_vehiculo"}
SEMILLA = Path(__file__).resolve().parents[2] / "src/Flit.Comparendos.Api/Semillas/PromptOcrSemilla.cs"


def prompt_semilla() -> str:
    s = SEMILLA.read_text(encoding="utf-8")
    m = re.search(r'Texto = """\n(.*?)\n\s*""";', s, re.S)
    if not m:
        sys.exit("No encontré el prompt en PromptOcrSemilla.cs")
    lineas = m.group(1).splitlines()
    sangria = min(len(l) - len(l.lstrip()) for l in lineas if l.strip())
    return "\n".join(l[sangria:] for l in lineas)


def sin_tildes(s: str) -> str:
    s = unicodedata.normalize("NFD", s)
    return "".join(c for c in s if unicodedata.category(c) != "Mn")


def normalizar(campo: str, v) -> str | None:
    """La forma en que el backend usa cada campo (ValoresOcr), para no castigar diferencias que no importan."""
    if v is None:
        return None
    s = str(v).strip()
    if not s or s.lower() in ("null", "none"):
        return None
    if campo in ("numero_comparendo", "placa", "codigo_infraccion"):
        return re.sub(r"[\s\-.]", "", s).upper()
    if campo in ("numero_documento_infractor", "codigo_organismo"):
        return re.sub(r"\D", "", s) or None
    if campo == "valor":
        t = s.replace("$", "").replace(" ", "")
        t = re.sub(r",\d{1,2}$", "", t)          # 675.311,00
        t = re.sub(r"\.\d{1,2}$", "", t) if re.search(r"\.\d{3}\.", t) is None and re.search(r"^\d+\.\d{1,2}$", t) else t
        return re.sub(r"\D", "", t) or None
    if campo in ("fecha_infraccion", "fecha_notificacion"):
        for f in ("%Y-%m-%d", "%d/%m/%Y", "%d-%m-%Y", "%Y/%m/%d"):
            try:
                return dt.datetime.strptime(s, f).date().isoformat()
            except ValueError:
                pass
        return s
    if campo == "hora_infraccion":
        m = re.match(r"(\d{1,2}):(\d{2})", s)
        return f"{int(m.group(1)):02d}:{m.group(2)}" if m else s
    if campo == "es_fotomulta":
        return {"true": "true", "si": "true", "sí": "true", "1": "true", "false": "false", "no": "false", "0": "false"}.get(s.lower(), s)
    if campo == "tipo_documento_infractor":
        return s.replace(".", "").upper()
    return re.sub(r"\s+", " ", re.sub(r"[^\w\s]", "", sin_tildes(s).upper())).strip()


def parecido(a: str, b: str) -> float:
    """Proporción de palabras en común (texto libre: nombre, dirección, descripción)."""
    pa, pb = set(re.findall(r"\w+", a)), set(re.findall(r"\w+", b))
    return len(pa & pb) / max(len(pa | pb), 1)


def calificar(campo: str, esperado, obtenido) -> str:
    e, o = normalizar(campo, esperado), normalizar(campo, obtenido)
    if e is None and o is None:
        return "ok_vacio"
    if e is None:
        return "inventado"      # el documento no lo trae y el modelo dio un valor
    if o is None:
        return "faltante"
    if e == o:
        return "ok"
    if campo in TEXTO_LIBRE and parecido(e, o) >= 0.7:
        return "ok"
    return "distinto"


def interpretar(texto: str) -> dict:
    """Igual que RespuestaOcr.Interpretar: el primer { al último }, y cada campo como valor o {valor, confianza}."""
    i, f = texto.find("{"), texto.rfind("}")
    if i < 0 or f <= i:
        return {}
    try:
        raiz = json.loads(texto[i:f + 1])
    except json.JSONDecodeError:
        return {}
    raiz = raiz.get("campos", raiz) if isinstance(raiz, dict) else {}
    salida = {}
    for k, v in raiz.items():
        k = k.strip().lower()
        if k in CAMPOS:
            salida[k] = (v.get("valor"), v.get("confianza")) if isinstance(v, dict) else (v, None)
    return salida


def llamar(llave: str, modelo: str, prompt: str, ruta: Path, max_tokens: int) -> str:
    datos = ruta.read_bytes()
    tipo = "application/pdf" if ruta.suffix.lower() == ".pdf" else ("image/png" if ruta.suffix.lower() == ".png" else "image/jpeg")
    bloque = "document" if tipo == "application/pdf" else "image"
    cuerpo = {
        "model": modelo,
        "max_tokens": max_tokens,
        "messages": [{"role": "user", "content": [
            {"type": bloque, "source": {"type": "base64", "media_type": tipo, "data": base64.b64encode(datos).decode()}},
            {"type": "text", "text": prompt},
        ]}],
    }
    for intento in range(5):
        req = urllib.request.Request("https://api.anthropic.com/v1/messages", data=json.dumps(cuerpo).encode(),
                                     headers={"x-api-key": llave, "anthropic-version": "2023-06-01", "content-type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=180) as r:
                j = json.load(r)
            return "".join(b.get("text", "") for b in j.get("content", []) if b.get("type") == "text")
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 529):
                time.sleep(float(e.headers.get("retry-after") or 2 ** intento * 5))
                continue
            return f"__ERROR__ HTTP {e.code}: {e.read()[:300]!r}"
        except (urllib.error.URLError, TimeoutError) as e:
            time.sleep(2 ** intento * 5)
            ultimo = str(e)
    return f"__ERROR__ {ultimo if 'ultimo' in dir() else 'reintentos agotados'}"


def reporte(conjunto: Path, corrida: Path, referencias: dict[str, dict]) -> str:
    por_campo = {c: {"ok": 0, "ok_vacio": 0, "faltante": 0, "distinto": 0, "inventado": 0} for c in CAMPOS}
    filas, obligatorios_ok, errores_conf, aciertos_conf = [], 0, [], []
    for clave, ref in sorted(referencias.items()):
        salida_f = corrida / f"{clave}.txt"
        if not salida_f.exists():
            continue
        obtenido = interpretar(salida_f.read_text(encoding="utf-8"))
        malos = []
        for c in CAMPOS:
            esperado = ref["campos"].get(c)
            valor, conf = obtenido.get(c, (None, None))
            r = calificar(c, esperado, valor)
            por_campo[c][r] += 1
            if r in ("faltante", "distinto", "inventado"):
                malos.append(f"{c} ({r})")
                if isinstance(conf, (int, float)) and r != "faltante":
                    errores_conf.append(conf)
            elif r == "ok" and isinstance(conf, (int, float)):
                aciertos_conf.append(conf)
        if all(calificar(c, ref["campos"].get(c), obtenido.get(c, (None, None))[0]) in ("ok", "ok_vacio") for c in OBLIGATORIOS):
            obligatorios_ok += 1
        filas.append(f"| {clave} | {ref.get('organismo_formato', '')} | {', '.join(malos) or '—'} |")
    n = len(filas)
    lineas = [f"# Corrida {corrida.name}", "", f"Documentos: {n}. Con todos los obligatorios bien: {obligatorios_ok} ({obligatorios_ok * 100 // max(n, 1)}%).", "",
              "| Campo | Acierto | Faltante | Distinto | Inventado |", "|---|---|---|---|---|"]
    for c in CAMPOS:
        p = por_campo[c]
        total = sum(p.values())
        lineas.append(f"| {c}{' *' if c in OBLIGATORIOS else ''} | {(p['ok'] + p['ok_vacio']) * 100 // max(total, 1)}% | {p['faltante']} | {p['distinto']} | {p['inventado']} |")
    media = lambda xs: f"{sum(xs) / len(xs):.2f}" if xs else "—"
    lineas += ["", f"Confianza media en aciertos: {media(aciertos_conf)}; en errores: {media(errores_conf)} (si se parecen, la confianza no sirve para avisar).",
               "", "| Documento | Formato | Campos con error |", "|---|---|---|", *filas, ""]
    texto = "\n".join(lineas)
    (corrida / "reporte.md").write_text(texto, encoding="utf-8")
    return texto


def main() -> None:
    a = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    a.add_argument("--conjunto", required=True, type=Path)
    a.add_argument("--muestras", type=Path)
    a.add_argument("--modelo", default="claude-haiku-5-5")
    a.add_argument("--prompt", type=Path, help="archivo con el prompt; sin él, el de la semilla")
    a.add_argument("--solo", help="claves separadas por coma")
    a.add_argument("--paralelo", type=int, default=3)
    a.add_argument("--max-tokens", type=int, default=2000)
    a.add_argument("--recalificar", type=Path, help="carpeta de una corrida: solo vuelve a calificar")
    args = a.parse_args()

    referencias = {p.stem: json.loads(p.read_text(encoding="utf-8")) for p in (args.conjunto / "referencia").glob("*.json")}
    if args.solo:
        quiero = set(args.solo.split(","))
        referencias = {k: v for k, v in referencias.items() if k in quiero}
    if args.recalificar:
        print(reporte(args.conjunto, args.recalificar, referencias))
        return

    llave = os.environ.get("ANTHROPIC_API_KEY")
    if not llave or not args.muestras:
        sys.exit("Falta ANTHROPIC_API_KEY (variable de entorno) o --muestras.")
    prompt = args.prompt.read_text(encoding="utf-8") if args.prompt else prompt_semilla()
    indice = {d["clave"]: d for d in json.loads((args.conjunto / "conjunto.json").read_text(encoding="utf-8"))}
    marca = f"{dt.datetime.now():%Y%m%d-%H%M}-{args.modelo}-{(args.prompt.stem if args.prompt else 'semilla')}"
    corrida = args.conjunto / "corridas" / marca
    corrida.mkdir(parents=True, exist_ok=True)
    (corrida / "prompt.txt").write_text(prompt, encoding="utf-8")

    def uno(clave: str) -> None:
        inicio = time.time()
        texto = llamar(llave, args.modelo, prompt, args.muestras / indice[clave]["archivo"], args.max_tokens)
        (corrida / f"{clave}.txt").write_text(texto, encoding="utf-8")
        print(f"{clave}: {time.time() - inicio:.1f} s{' ERROR' if texto.startswith('__ERROR__') else ''}", flush=True)

    with concurrent.futures.ThreadPoolExecutor(args.paralelo) as ex:
        list(ex.map(uno, [k for k in referencias if k in indice]))
    print(reporte(args.conjunto, corrida, referencias))


if __name__ == "__main__":
    main()
