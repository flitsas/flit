# Flit 2.0

Bienvenido al repositorio principal de **Flit 2.0**, una plataforma integral enfocada en la gestión vehicular, tránsito y conductores. El proyecto implementa integraciones oficiales clave como **Verifik (RUNT)**.

## Arquitectura y Estructura del Proyecto

El proyecto está organizado como un **monorepo** gestionado con `pnpm` workspaces, compuesto por una arquitectura orientada a microservicios:

- **`/frontend`**: Aplicación web moderna construida con **Next.js** (App Router, React, TypeScript).
- **`/services/core-api`**: Backend principal en **.NET 10 / C#**, que incluye:
  - `Flit.Gateway`: API Gateway utilizando **YARP** (proxy, validación JWT, rate limiting).
  - `Flit.Api`: Host ASP.NET Core con la lógica central de negocio.
  - `Flit.Infrastructure`: Capa de persistencia utilizando Entity Framework Core con **PostgreSQL**.
- **`/services/python-ml`**: Microservicio en **Python 3.13** y **FastAPI**, enfocado en tareas de **Machine Learning y OCR** (Reconocimiento Óptico de Caracteres).

## Desarrollo y Herramientas

- **Contenedores:** Soporte nativo para Docker y `docker-compose` (`docker-compose.yml`, `docker-compose.prod.yml`).
- **CI/CD:** Pipelines automatizados mediante **GitHub Actions** para construcción, test, despliegue, revisión de contratos OpenAPI y escaneo de seguridad con SonarCloud (`sonar-project.properties`).
- **Asistencia IA Integrada:** El directorio `.cursor` contiene la configuración de agentes especializados y "skills" (ej: `bug-reporter`, `db-schema-validator`, `feature-creator`, etc.) que auditan y asisten activamente en el ciclo de vida del desarrollo de software.

## Convenciones (Innegociables)

Asegúrate de cumplir las directrices detalladas en `.cursor/rules/00-flit-conventions.mdc`, que incluyen:

1. **Ramas:** El formato debe ser `feature/AB-1234-descripcion`.
2. **Commits:** El formato debe ser `HU1234: descripción breve`.
3. **Pull Requests:** El target principal de los PRs es la rama `develop` (o `main` en su defecto). El PR no debe sobrepasar las 800 líneas de código modificado.
4. **Seguridad:** Está estrictamente prohibido subir secretos o credenciales al repositorio.

## Cómo empezar (Local)

1. **Configuración de Variables de Entorno:**
   Copia los archivos de ejemplo (ej. `.env.example`, `.env.verifik.example`) a `.env` y configura tus variables de acceso y tokens correspondientes.

2. **Instalación de Dependencias Central:**
   ```bash
   pnpm install
   ```

3. **Ejecutar los servicios (comandos desde la raíz):**
   - Frontend: `pnpm --filter frontend dev`
   - Core API: `pnpm run dev:core-api`
   - Gateway: `pnpm run dev:gateway`
   - Python ML: `pnpm run dev:python`

---
> Proyecto configurado siguiendo buenas prácticas de la industria con integración de herramientas modernas y soporte completo para flujos de trabajo asistidos por IA.
