# Mesa del Caos

Juego basado en Magic: The Gathering, en tres modos (ver el plan del proyecto):

1. **Party game de Commander** (este repo, primera etapa): una app de escritorio donde configurás la partida y exportás un HTML para compartir con la mesa.
2. Motor de reglas simplificado y puzzles "ganá este turno".
3. Roguelike de draft contra jefes Commander, con los puzzles como salas de desafío.

## Party game

La app de escritorio (C#, .NET 8, Avalonia) permite:

- Poner nombre a la partida, vidas iniciales (40 por defecto) y de 2 a 6 jugadores con su color.
- Activar, editar, borrar o agregar **desafíos** (con duración: ahora, una vez, esta ronda o toda la partida), **misiones secretas** con recompensa y **planos** con efecto y caos.
- Guardar y abrir partidas como archivos `.mtgparty`.
- **Exportar HTML**: un único archivo que se manda al grupo y se abre en el navegador del celular, sin instalar nada. Lleva el contador de vidas, el turno y la ronda, los desafíos, las misiones secretas (se reparten al azar al abrirlo) y el dado planar.

Todo el contenido es original. Es un proyecto de fans gratuito y no comercial, dentro de la Fan Content Policy de Wizards of the Coast.

## Puzzles "ganá este turno" (etapa 2)

En la pestaña **Puzzles** de la app hay 10 puzzles de dificultad creciente. Es tu fase principal y tenés que ganar antes de que termine el turno. Tocás cartas de la mano para jugarlas (las tierras se giran solas al lanzar), elegís objetivos, declarás atacantes y pasás la prioridad. Se puede deshacer, reiniciar y pedir una pista.

- El rival no tiene cartas en la mano y bloquea de la mejor forma según lo que ve en la mesa: no anticipa los trucos que lances después de los bloqueos.
- Cada puzzle se verifica con un solucionador por fuerza bruta (`PuzzleSolver`), que también da las pistas. Los tests comprueban que todos tienen solución y que varios dejan de tenerla con una vida más para el rival.
- Los puzzles están en `src/MtgEngine/Resources/puzzles.json`. El progreso se guarda en la carpeta de datos del usuario.

### Editor de puzzles

En la pestaña **Editor de puzzles** armás tu propio puzzle: título, dificultad, textos, vidas y qué cartas hay en tu mano, tu campo y el campo del rival (giradas o con mareo). **Verificar solución** corre el solucionador y te muestra una línea ganadora, y te avisa si con una vida más para el rival también se gana. **Probar** lo agrega a la lista de puzzles para jugarlo. Se guarda como archivo `.mtgpuzzle` para compartirlo, y se abre con **Abrir puzzle…** en la pestaña Puzzles.

## Roguelike (etapa 3)

La pestaña **Roguelike** es el modo principal. Elegís uno o dos colores, hacés un draft de 12 cartas (las tierras se agregan solas según los símbolos de maná) y recorrés tres actos. Cada acto es un mapa de salas que se sube de abajo hacia arriba:

- **Duelo**: una partida completa contra un rival con IA. Si ganás, elegís una carta para tu mazo.
- **Élite**: un rival más fuerte; da una carta mejor y una reliquia.
- **Desafío**: uno de los puzzles "ganá este turno". Si lo resolvés, ganás una reliquia.
- **Descanso**: te curás o sacás una carta del mazo.
- **Tesoro**: una reliquia.
- **Jefe**: al final de cada acto, un Commander con su comandante en la zona de mando (vuelve ahí si muere, y cada vez cuesta {2} más).

Tus vidas se mantienen entre duelos y si llegás a 0 se termina la run. Las reliquias dan ventajas para toda la run (una tierra extra al empezar, una carta más en la mano, rivales con menos vidas, etc.). La run se guarda sola y se puede seguir otro día.

La IA del rival (`src/MtgEngine/Roguelike/DuelAI.cs`) decide cada jugada simulando cómo queda el tablero después de resolver la pila y el combate. No ve tu mano ni anticipa tus trucos. Los rivales, el pool del draft y las reliquias están en `src/MtgEngine/Resources/roguelike.json`.

## Motor de reglas

`src/MtgEngine` es una librería con reglas simplificadas de Magic para dos jugadores, base de los puzzles y del roguelike:

- Maná por color con reserva que se vacía entre pasos, tierras (una por turno) y costes como `{2}{R}{R}`.
- Pila con prioridad: los hechizos se resuelven cuando ambos jugadores pasan, se puede responder con instantáneos y un hechizo cuyo objetivo desaparece se neutraliza.
- Pasos del turno completos: enderezar, mantenimiento, robar, fases principales, combate y limpieza.
- Combate con atacantes y bloqueadores, mareo de invocación y las habilidades volar, alcance, prisa, vigilancia, arrollar, toque mortal, vínculo vital y defensor.
- Acciones de estado: criaturas que mueren, jugadores sin vidas o sin cartas para robar.
- Zona de mando para los comandantes de los jefes, con el impuesto de {2} por cada vez que se lanzaron.
- `Game.Clone()` copia la partida completa, para deshacer jugadas y para que la IA pruebe líneas.

Las cartas son originales y están en `src/MtgEngine/Resources/cards.json`.

## Cómo correrlo

Necesitás el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet run --project src/MtgParty.Desktop
```

Tests:

```bash
dotnet test
```

Cada push a `main` corre los tests en GitHub Actions y publica una versión nueva. El último ejecutable para Windows siempre está en [MesaDelCaos.exe](https://github.com/fedeArrieta/mtg-game/releases/latest/download/MesaDelCaos.exe).

## Estructura

| Carpeta | Qué hay |
| --- | --- |
| `src/MtgParty.Core` | Modelos, contenido por defecto (`Resources/default-cards.json`), plantilla HTML y exportador |
| `src/MtgParty.Desktop` | App de escritorio Avalonia (configurador) |
| `tests/MtgParty.Core.Tests` | Tests del exportador y del formato `.mtgparty` |
| `src/MtgEngine` | Motor de reglas y catálogo de cartas originales |
| `src/MtgEngine/Roguelike` | IA rival, duelos completos y la run (draft, mapa, reliquias) |
| `tests/MtgEngine.Tests` | Tests de maná, pila, combate, turnos, puzzles y roguelike |
