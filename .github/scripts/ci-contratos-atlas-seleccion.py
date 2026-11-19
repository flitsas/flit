#!/usr/bin/env python3
"""Elige qué contratos de .github/ci/contratos-atlas.yml (pasada a JSON con yq) corren en atlas-publicar-contratos.yml.

Cada contrato es un job aparte y GitHub cobra cada job redondeado al minuto: con ~30 contratos, un PR que toca
uno solo gastaba ~30 minutos. Reglas:
  - push a staging o release, y workflow_dispatch: todos (publican los canales qa/pdn; mejor completo).
  - pull_request y push a develop: solo los contratos cuyo archivo cambió. Si cambia este script, la lista o el
    workflow, todos. Si cambia otro archivo dentro de contracts/<tipo>/ (un $ref común, un import), todos los de
    ese tipo.
  - Sin historia para comparar (rama nueva, push forzado): todos.

Entrada: CONTRATOS (ruta del JSON), EVENTO, RAMA, BASE (PR), ANTES y SHA (push). Escribe en GITHUB_OUTPUT
matriz={"include":[...]} y hay=true|false.
"""
import json
import os
import subprocess
import sys

PROPIOS = {
    ".github/workflows/atlas-publicar-contratos.yml",
    ".github/ci/contratos-atlas.yml",
    ".github/scripts/ci-contratos-atlas-seleccion.py",
}
CARPETA_TIPO = {"contracts/openapi/": "openapi", "contracts/asyncapi/": "asyncapi", "contracts/proto/": "proto"}


def git_diff(*rango):
    r = subprocess.run(["git", "diff", "--name-only", *rango], capture_output=True, text=True)
    return None if r.returncode else [l for l in r.stdout.splitlines() if l]


def archivos_cambiados():
    evento, rama = os.environ.get("EVENTO", ""), os.environ.get("RAMA", "")
    if evento == "pull_request":
        return git_diff(f"origin/{os.environ['BASE']}...HEAD")
    if evento == "push" and rama == "develop":
        antes = os.environ.get("ANTES", "")
        if not antes or set(antes) == {"0"}:
            return None
        return git_diff(antes, os.environ["SHA"])
    return None  # staging, release, workflow_dispatch: todos


def seleccionar(contratos, cambiados):
    if cambiados is None or PROPIOS & set(cambiados):
        return contratos, "todos"
    rutas = {c["ruta"] for c in contratos}
    tipos = {t for f in cambiados if f not in rutas for p, t in CARPETA_TIPO.items() if f.startswith(p)}
    elegidos = [c for c in contratos if c["ruta"] in cambiados or c["tipo"] in tipos]
    motivo = f"{len(elegidos)} de {len(contratos)}" + (f" (todos los {', '.join(sorted(tipos))})" if tipos else "")
    return elegidos, motivo


def main():
    with open(os.environ["CONTRATOS"], encoding="utf-8") as f:
        contratos = json.load(f)
    elegidos, motivo = seleccionar(contratos, archivos_cambiados())
    print(f"Contratos a validar/publicar: {motivo}")
    for c in elegidos:
        print(f"  - {c['contrato']} ({c['tipo']})")
    salida = os.environ.get("GITHUB_OUTPUT")
    lineas = f"matriz={json.dumps({'include': elegidos}, separators=(',', ':'))}\nhay={'true' if elegidos else 'false'}\n"
    if salida:
        with open(salida, "a", encoding="utf-8") as f:
            f.write(lineas)
    else:
        sys.stdout.write(lineas)


if __name__ == "__main__":
    main()
