# AI Engineering Platform — Project Guidelines

## 1. Contexto del Proyecto
Este repositorio es la plataforma base para **AI-Native Software Engineering**, orientada a equipos de ingeniería con desarrollo asistido por agentes.

## 2. Pautas de Trabajo y Comandos
- **Entorno:** Linux (WSL2 Ubuntu 24.04).
- **Stack Principal:** .NET 10, TypeScript, React, Angular, Docker.
- **Ruta de Trabajo:** Ubicarse siempre en `/home/aaicardi/workspace/ai-engineering-platform`.

## 3. Modelo de Autonomía y Gobernanza (Level 4 / D)
- **Acciones Automáticas:** Lectura de código, análisis de arquitectura, ejecución de tests locales, creación de especificaciones OpenSpec.
- **Requiere Aprobación Humana Explícita:**
  - `git push` a ramas remotas.
  - Pull Request merge.
  - Modificación o aplicación de migraciones de base de datos.
  - Creación/modificación de secretos o variables de entorno sensibles.
  - Despliegues en entornos.

## 4. Agentes Definidos
1. **Architect:** Requerimientos, diseño técnico, diagramas y decisiones.
2. **Developer:** Implementación de código siguiendo estándares.
3. **Tester:** Diseño y ejecución de pruebas unitarias/integración.
4. **Code-Reviewer:** Calidad, mantenibilidad y patrones de diseño.
5. **Security-Reviewer:** Análisis de vulnerabilidades y seguridad local.
6. **DevOps:** Contenedores (Docker), CI/CD y despliegue.
7. **Documentation:** README, OpenSpec, wikis y documentación técnica.

## 5. Convenciones de Commits y Código
- Seguir **Conventional Commits**: `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`.
- Todas las funcionalidades deben contar con su especificación correspondiente en `openspec/specs/`.
