# Flit 2.0

Bienvenido al repositorio principal de **Flit 2.0**, una plataforma integral enfocada en la gestión vehicular, tránsito y conductores, con integraciones oficiales clave como **Verifik (RUNT)**.

## Arquitectura y Estructura del Proyecto

El proyecto está organizado como un **monorepo** gestionado con \pnpm\ workspaces, compuesto por una arquitectura orientada a microservicios:

- **\/frontend\**: Aplicación web moderna construida con **Next.js** (App Router, React, TypeScript).
- **\/services/core-api\**: Backend principal en **.NET 10 / C#**, que incluye:
  - \Flit.Gateway\: API Gateway utilizando **YARP** (proxy, validación JWT, rate limiting).
  - \Flit.Api\: Host ASP.NET Core con la lógica central de negocio.
  - \Flit.Infrastructure\: Capa de persistencia utilizando Entity Framework Core con **PostgreSQL**.
- **\/services/python-ml\**: Microservicio en **Python 3.13** y **FastAPI**, enfocado en tareas de **Machine Learning y OCR** (Reconocimiento Óptico de Caracteres).

## Desarrollo y Herramientas

- **Contenedores:** Soporte nativo para Docker y \docker-compose\ (\docker-compose.yml\, \docker-compose.prod.yml\).
- **CI/CD:** Pipelines automatizados mediante **GitHub Actions** para construcción, test, despliegue, revisión de contratos OpenAPI y escaneo de seguridad (SonarCloud).
- **Asistencia IA Integrada:** El directorio \.cursor\ contiene la configuración de agentes especializados y "skills" que auditan y asisten en el ciclo de vida del desarrollo.

## Convenciones (Innegociables)

Por favor, asegúrate de cumplir las directrices detalladas en \.cursor/rules/00-flit-conventions.mdc\, que incluyen (entre otras):

1. **Ramas:** El formato debe ser \eature/AB-1234-descripcion\.
2. **Commits:** El formato debe ser \HU1234: descripción breve\.
3. **Pull Requests:** El target principal de los PRs es la rama \develop\. El PR no debe sobrepasar las 800 líneas.
4. **Seguridad:** Cero secretos subidos al repositorio.

## Cómo empezar (Local)

1. **Configuración de Variables de Entorno:**
   Copia el archivo \.env.example\ a \.env\ y configura tus variables de acceso y tokens según las instrucciones contenidas en el mismo archivo.

2. **Instalación de Dependencias Central:**
   \\\ash
   pnpm install
   \\\

3. **Ejecutar los servicios (comandos desde la raíz):**
   - Frontend: \pnpm --filter frontend dev\
   - Core API: \pnpm run dev:core-api\
   - Gateway: \pnpm run dev:gateway\
   - Python ML: \pnpm run dev:python\

---
> Documentación actualizada de manera autónoma para reflejar el estado general del ecosistema Flit 2.0.
