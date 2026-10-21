"""HU #13734 (F00.1-H8): despliegue, puertos y cliente de servicio de Diagnóstico.

Comprueba la configuración versionada (compose, CD, CI, puertos y cliente svc-diagnostico) sin desplegar nada.
Lo corre el workflow «Configuración de despliegue»; en local:

    python -m unittest discover -s .github/scripts -p "test_despliegue_*.py" -v

Las comprobaciones de puertos son generales: fallan si un número queda asignado a dos servicios.
"""

from __future__ import annotations

import json
import re
import subprocess
import unittest
from pathlib import Path

import yaml

RAIZ = Path(__file__).resolve().parents[2]
COMPOSE = RAIZ / "docker-compose.prod.yml"
CD = RAIZ / ".github" / "workflows" / "cd.yml"
CI_PLATAFORMA = RAIZ / ".github" / "workflows" / "servicios-plataforma.yml"
PUERTOS = RAIZ / "docs" / "despliegue-y-puertos.md"
ENV_EJEMPLO = RAIZ / ".env.prod.example"
APPSETTINGS_API = RAIZ / "services" / "core-api" / "src" / "Flit.Api" / "appsettings.json"
SETTINGS_SERVICIO = RAIZ / "services" / "core-diagnostico" / "src" / "Flit.Diagnostico.Api" / "ServicioSettings.cs"
DOCKERFILE_API = RAIZ / "services" / "core-diagnostico" / "Dockerfile"
DOCKERFILE_WEB = RAIZ / "frontend-diagnostico" / "Dockerfile"

API_DEV, API_QA, API_PDN = "4029", "5029", "6029"
WEB_DEV, WEB_QA, WEB_PDN = "4024", "5024", "6024"
GRPC = "8087"
INTERPOLACION_VACIA = re.compile(r"^\$\{[A-Z0-9_]+:-\}$")


def _texto(ruta: Path) -> str:
    return ruta.read_text(encoding="utf-8")


def _compose() -> dict:
    return yaml.safe_load(_texto(COMPOSE))


def _servicio(nombre: str) -> dict:
    servicios = _compose()["services"]
    if nombre not in servicios:
        raise AssertionError(f"docker-compose.prod.yml no tiene el servicio {nombre}")
    return servicios[nombre]


def _cd() -> dict:
    return yaml.safe_load(_texto(CD))


def _seccion_2d() -> str:
    texto = _texto(PUERTOS)
    inicio = texto.index("### d) Puertos reservados")
    fin = texto.index("\n## 3.", inicio)
    return texto[inicio:fin]


def _filas(seccion: str) -> list[tuple[str, list[str]]]:
    filas = []
    for linea in seccion.splitlines():
        if not linea.startswith("|") or set(linea.replace("|", "").strip()) <= {"-"}:
            continue
        celdas = [c.strip() for c in linea.strip("|").split("|")]
        numeros = re.findall(r"`(\d{4})`", linea)
        if numeros:
            filas.append((celdas[0], numeros))
    return filas


class PerfilDeCompose(unittest.TestCase):
    """Escenario 1: el perfil diagnostico resuelve la API, la web y sus variables."""

    def test_api_en_el_perfil_con_sus_puertos(self):
        api = _servicio("core-diagnostico")
        self.assertEqual(api.get("profiles"), ["diagnostico"])
        self.assertEqual(api["image"], "ghcr.io/flitsas/flitdev/core-diagnostico:${CORE_DIAGNOSTICO_TAG:-latest}")
        env = api["environment"]
        self.assertEqual(env["ASPNETCORE_URLS"], "http://+:${CORE_DIAGNOSTICO_PORT:-4029}")
        self.assertEqual(env["Servicio__GrpcPort"], "${CORE_DIAGNOSTICO_GRPC_PORT:-8087}")
        self.assertNotIn("ports", api, "la API no se publica: vive detrás del gateway")
        self.assertIn("/health/ready", " ".join(api["healthcheck"]["test"]))
        self.assertIn("${CORE_DIAGNOSTICO_PORT:-4029}", " ".join(api["healthcheck"]["test"]))

    def test_api_trae_toda_la_configuracion_que_exige_al_arrancar(self):
        env = _servicio("core-diagnostico")["environment"]
        requeridas = re.findall(r'\("([A-Za-z:0-9]+)",\s*"[^"]*"\)', _texto(SETTINGS_SERVICIO))
        self.assertTrue(requeridas, "no se pudieron leer las claves de ServicioSettings.cs")
        for clave in requeridas:
            variable = clave.replace(":", "__")
            self.assertIn(variable, env, f"ServicioSettings exige {clave} y el compose no la pasa")
            self.assertTrue(str(env[variable]).strip(), f"{variable} vacía en el compose")

    def test_api_trae_gateway_e_identidad_sin_simular_suscripcion(self):
        env = _servicio("core-diagnostico")["environment"]
        self.assertEqual(env["Diagnostico__Plataforma__GatewayUrl"], "http://gateway:${GATEWAY_PORT:-4002}")
        self.assertEqual(env["Diagnostico__Identidad__Address"], "${DIAGNOSTICO_IDENTIDAD_ADDRESS:-}")
        self.assertEqual(env["Platform__ServiceClient__ClientSecret"], "${SVC_DIAGNOSTICO_CLIENT_SECRET:-}")
        for ruta in (COMPOSE, ENV_EJEMPLO):
            self.assertNotIn("SimularSuscripcionActiva", _texto(ruta), f"{ruta.name}: la suscripción simulada no va en ningún ambiente")
            self.assertNotIn("SIMULAR_SUSCRIPCION", _texto(ruta), ruta.name)

    def test_web_en_el_perfil_publicada_solo_en_loopback(self):
        web = _servicio("frontend-diagnostico")
        self.assertEqual(web.get("profiles"), ["diagnostico"])
        self.assertEqual(web["image"], "ghcr.io/flitsas/flitdev/frontend-diagnostico:${FRONTEND_DIAGNOSTICO_TAG:-latest}")
        env = web["environment"]
        self.assertEqual(env["PORT"], "${DIAGNOSTICO_PORT:-4024}")
        self.assertEqual(env["CORE_API_ORIGIN"], "http://gateway:${GATEWAY_PORT:-4002}")
        self.assertEqual(env["FLIT_SESSION_SECRET"], "${DIAGNOSTICO_SESSION_SECRET:-}")
        self.assertEqual(web["ports"], ["127.0.0.1:${DIAGNOSTICO_PORT:-4024}:${DIAGNOSTICO_PORT:-4024}"])
        self.assertIn("/healthz", " ".join(web["healthcheck"]["test"]))

    def test_gateway_enruta_a_la_api_y_core_api_registra_el_cliente(self):
        gateway = _servicio("gateway")["environment"]
        self.assertEqual(
            gateway.get("ReverseProxy__Clusters__core-diagnostico-cluster__Destinations__core-diagnostico-1__Address"),
            "http://core-diagnostico:${CORE_DIAGNOSTICO_PORT:-4029}/",
        )
        for nombre in ("core-api", "core-identity"):
            env = _servicio(nombre)["environment"]
            self.assertEqual(
                env.get("Suite__Oidc__ServiceClients__svc-diagnostico__Secret"), "${SVC_DIAGNOSTICO_CLIENT_SECRET:-}", nombre
            )


class JobsDeCd(unittest.TestCase):
    """Escenario 2: cd.yml construye la API y la web de Diagnóstico en cada push."""

    def _job(self, nombre: str) -> dict:
        jobs = _cd()["jobs"]
        self.assertIn(nombre, jobs, f"cd.yml no tiene el job {nombre}")
        return jobs[nombre]

    @staticmethod
    def _paso(job: dict, uses_prefijo: str) -> dict:
        for paso in job["steps"]:
            if str(paso.get("uses", "")).startswith(uses_prefijo):
                return paso
        raise AssertionError(f"sin paso {uses_prefijo}")

    @staticmethod
    def _fuentes_dockerfile(dockerfile: Path, prefijo: str) -> list[str]:
        fuentes = []
        for linea in _texto(dockerfile).splitlines():
            partes = linea.split()
            if not partes or partes[0] != "COPY":
                continue
            if partes[1].startswith("--from="):
                if partes[1] == "--from=contracts":
                    fuentes.append("contracts/proto")
                continue
            for fuente in partes[1:-1]:
                fuentes.append((prefijo + fuente).rstrip("/").removesuffix("/."))
        return fuentes

    def _cubre(self, job: dict, dockerfile: Path, prefijo: str):
        rutas = [r.strip() for r in self._paso(job, "./.github/actions/reuse-image")["with"]["paths"].splitlines() if r.strip()]
        for fuente in self._fuentes_dockerfile(dockerfile, prefijo):
            self.assertTrue(
                any(fuente == r or fuente.startswith(r + "/") for r in rutas),
                f"{fuente} entra en la imagen y no está en las rutas de reuse-image: un cambio ahí no la reconstruiría",
            )

    def test_build_core_diagnostico(self):
        self.assertEqual(_cd()["env"].get("CORE_DIAGNOSTICO_IMAGE"), "ghcr.io/flitsas/flitdev/core-diagnostico")
        job = self._job("build-core-diagnostico")
        self.assertEqual(job["needs"], "setup")
        build = self._paso(job, "docker/build-push-action")["with"]
        self.assertEqual(build["context"], "./services")
        self.assertEqual(build["file"], "./services/core-diagnostico/Dockerfile")
        self.assertIn("contracts=./contracts/proto", build["build-contexts"])
        self._cubre(job, DOCKERFILE_API, "services/")

    def test_build_frontend_diagnostico(self):
        self.assertEqual(_cd()["env"].get("FRONTEND_DIAGNOSTICO_IMAGE"), "ghcr.io/flitsas/flitdev/frontend-diagnostico")
        job = self._job("build-frontend-diagnostico")
        self.assertEqual(job["needs"], "setup")
        build = self._paso(job, "docker/build-push-action")["with"]
        self.assertEqual(build["context"], ".")
        self.assertEqual(build["file"], "./frontend-diagnostico/Dockerfile")
        self._cubre(job, DOCKERFILE_WEB, "")

    def test_despliegue_por_vps_conoce_los_dos_servicios(self):
        job = self._job("deploy-to-vps")
        for build in ("build-core-diagnostico", "build-frontend-diagnostico"):
            self.assertIn(build, job["needs"])
        script = next(p["with"]["script"] for p in job["steps"] if "script" in p.get("with", {}))
        servicios = re.search(r'SERVICES="([^"]+)"', script).group(1).split()
        for svc, var in (("core-diagnostico", "CORE_DIAGNOSTICO"), ("frontend-diagnostico", "FRONTEND_DIAGNOSTICO")):
            self.assertIn(svc, servicios)
            self.assertIn(f"export {var}_TAG=", script)
            self.assertIn(f"{svc}) echo \"${{{{ needs.build-{svc}.result }}}}\"", script)
            self.assertIn(f"{svc}) echo \"${{{{ needs.build-{svc}.outputs.digest }}}}\"", script)
            self.assertIn(f"{svc}) echo {var}_TAG", script)
        self.assertIn("export DIAGNOSTICO_PORT=${{ needs.setup.outputs.diagnostico_port }}", script)
        self.assertIn("export CORE_DIAGNOSTICO_PORT=${{ needs.setup.outputs.core_diagnostico_port }}", script)

    def test_setup_exporta_los_puertos_por_ambiente(self):
        script = self._job("setup")["steps"][0]["run"]
        for web, api in ((WEB_PDN, API_PDN), (WEB_QA, API_QA), (WEB_DEV, API_DEV)):
            linea = f"diagnostico_port={web}; core_diagnostico_port={api}"
            self.assertTrue(linea in script, f"setup de cd.yml no exporta {linea}")
        outputs = self._job("setup")["outputs"]
        self.assertIn("diagnostico_port", outputs)
        self.assertIn("core_diagnostico_port", outputs)


class CiDePlataforma(unittest.TestCase):
    """Escenario 3: servicios-plataforma.yml descubre core-diagnostico y prueba con Postgres."""

    def _sh(self, *args: str) -> str:
        return subprocess.run(
            ["bash", ".github/scripts/servicios-plataforma.sh", *args], cwd=RAIZ, check=True, capture_output=True, text=True
        ).stdout

    def test_lo_descubre_y_tiene_contrato(self):
        self.assertIn('"core-diagnostico"', self._sh("listar"))
        self.assertIn("contracts/proto/flit/diagnostico/v1/", self._sh("contrato", "core-diagnostico"))
        self.assertEqual(self._sh("solucion", "core-diagnostico").strip(), "services/core-diagnostico/Flit.Diagnostico.slnx")

    def test_las_pruebas_corren_con_postgres(self):
        job = yaml.safe_load(_texto(CI_PLATAFORMA))["jobs"]["servicio"]
        self.assertIn("postgres", job["services"])
        paso = next(p for p in job["steps"] if p.get("name") == "Build y pruebas")
        self.assertIn("ConnectionStrings__Core", paso["env"])
        self.assertIn("dotnet test", paso["run"])


class PuertosRegistrados(unittest.TestCase):
    """Escenario 4: §2.d registra API 4029/5029/6029 y gRPC 8087 sin choques."""

    def test_diagnostico_registrado(self):
        filas = dict(_filas(_seccion_2d()))
        api = next((n for s, n in filas.items() if "core-diagnostico" in s and GRPC not in n), None)
        self.assertEqual(api, [API_DEV, API_QA, API_PDN], "falta la fila de la API de Diagnóstico en §2.d")
        web = next((n for s, n in filas.items() if "frontend-diagnostico" in s), None)
        self.assertEqual(web, [WEB_DEV, WEB_QA, WEB_PDN])
        grpc = next((n for s, n in filas.items() if "core-diagnostico" in s and GRPC in n), None)
        self.assertEqual(grpc, [GRPC], "falta core-diagnostico en «Puertos gRPC internos»")

    def test_ningun_puerto_de_la_seccion_se_repite(self):
        duenos: dict[str, str] = {}
        for servicio, numeros in _filas(_seccion_2d()):
            for numero in numeros:
                self.assertNotIn(numero, duenos, f"{numero} asignado a «{duenos.get(numero)}» y a «{servicio}»")
                duenos[numero] = servicio

    def test_ningun_puerto_por_defecto_del_compose_se_repite(self):
        duenos: dict[str, set[str]] = {}
        for variable, numero in re.findall(r"\$\{([A-Z0-9_]+_PORT):-(\d+)\}", _texto(COMPOSE)):
            duenos.setdefault(numero, set()).add(variable)
        repetidos = {n: v for n, v in duenos.items() if len(v) > 1}
        self.assertEqual(repetidos, {}, "un mismo puerto por defecto para dos variables del compose")
        self.assertEqual(duenos.get(API_DEV), {"CORE_DIAGNOSTICO_PORT"})
        self.assertEqual(duenos.get(GRPC), {"CORE_DIAGNOSTICO_GRPC_PORT"})
        self.assertEqual(duenos.get(WEB_DEV), {"DIAGNOSTICO_PORT"})


class ClienteDeServicio(unittest.TestCase):
    """Escenario 5: svc-diagnostico declarado con sus scopes y sin secretos en el repo."""

    def test_svc_diagnostico_con_los_scopes_que_usa(self):
        clientes = json.loads(_texto(APPSETTINGS_API))["Suite"]["Oidc"]["ServiceClients"]
        cliente = clientes["svc-diagnostico"]
        self.assertEqual(cliente["Secret"], "")
        self.assertEqual(
            sorted(cliente["Scopes"]), sorted(["platform.manifest", "platform.identidad.read", "platform.tramites.catalogos"])
        )

    def test_variables_de_diagnostico_sin_valores_en_el_ejemplo(self):
        lineas = [l for l in _texto(ENV_EJEMPLO).splitlines() if "DIAGNOSTICO" in l and "=" in l]
        nombres = {re.match(r"#?\s*([A-Z0-9_]+)=", l).group(1) for l in lineas if re.match(r"#?\s*[A-Z0-9_]+=", l)}
        for esperada in (
            "SVC_DIAGNOSTICO_CLIENT_SECRET",
            "DIAGNOSTICO_SESSION_SECRET",
            "CONNECTION_STRING_DIAGNOSTICO",
            "RABBITMQ_URL_DIAGNOSTICO",
            "DIAGNOSTICO_IDENTIDAD_ADDRESS",
            "DIAGNOSTICO_PORT",
            "CORE_DIAGNOSTICO_PORT",
            "CORE_DIAGNOSTICO_GRPC_PORT",
        ):
            self.assertIn(esperada, nombres, f".env.prod.example no documenta {esperada}")
        for linea in lineas:
            if re.match(r"#?\s*[A-Z0-9_]*(SECRET|PASSWORD|CLAVE|KEY)[A-Z0-9_]*=", linea):
                valor = linea.split("=", 1)[1].split("#")[0].strip()
                self.assertEqual(valor, "", f"valor de secreto en .env.prod.example: {linea.split('=')[0]}")

    def test_secretos_del_compose_solo_por_variable(self):
        bloques = [_servicio("core-diagnostico")["environment"], _servicio("frontend-diagnostico")["environment"]]
        bloques.append({k: v for k, v in _servicio("core-api")["environment"].items() if "diagnostico" in k})
        for env in bloques:
            for clave, valor in env.items():
                if re.search(r"Secret|SECRET|ConnectionString|ApiKey|Password", clave):
                    self.assertRegex(str(valor), INTERPOLACION_VACIA, f"{clave} debe salir del .env, sin valor por defecto")


if __name__ == "__main__":
    unittest.main()
