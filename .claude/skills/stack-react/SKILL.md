---
name: stack-react
description: Convenciones de componentes, hooks, estado, accesibilidad, testing con Testing Library y checklist de revisión para frontends React con TypeScript. Úsala cuando package.json dependa de react; complementa a stack-typescript-node.
---

# Skill: React

Complementa a `stack-typescript-node` (comandos, TypeScript). Las convenciones del repositorio prevalecen.

## Convenciones
- Componentes de función con props tipadas; un componente por archivo, con el nombre del archivo igual al del componente.
- **Hooks:** solo en el nivel superior; dependencias completas en `useEffect`/`useMemo`/`useCallback` (respeta
  `react-hooks/exhaustive-deps`); lógica reutilizable en hooks `useX`.
- **Efectos:** solo para sincronizar con sistemas externos; no para derivar estado (calcúlalo en el render).
  Limpia suscripciones y cancela peticiones (`AbortController`) en el *cleanup*.
- **Estado:** lo más local posible; estado del servidor con la librería del repositorio (TanStack Query, RTK Query...),
  no duplicado en estado local.
- **Listas:** `key` estable (id), nunca el índice si la lista cambia.
- **Formularios:** validación en cliente para la UX, pero la validación que cuenta es la del servidor.
- **Accesibilidad:** HTML semántico (`button`, `label`, `nav`), textos alternativos, foco gestionado en diálogos, contraste.
- **Seguridad:** sin `dangerouslySetInnerHTML` con contenido no saneado; sin tokens en `localStorage` si el repositorio usa cookies `httpOnly`.

## Tests (React Testing Library + Vitest/Jest)
- Prueba lo que ve y hace el usuario: `getByRole`, `getByLabelText`; evita `getByTestId` salvo que no haya alternativa.
- Interacciones con `@testing-library/user-event`.
- Red simulada con MSW o el mecanismo del repositorio.
- Espera con `findBy*`/`waitFor`, no con temporizadores.
- Cubre estados de carga, error y vacío, no solo el caso feliz.

## Revisión
- [ ] Dependencias de hooks completas; sin efectos que derivan estado.
- [ ] Sin re-renders costosos evidentes (objetos o funciones nuevas en props de listas grandes sin memoización).
- [ ] Estados de carga, error y vacío manejados.
- [ ] Accesible por teclado y con roles correctos.
