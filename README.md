# Mesa del Caos

Juego basado en Magic: The Gathering, en tres modos (ver el plan del proyecto):

1. **Party game de Commander** (este repo, primera etapa): una app de escritorio donde configurás la partida y exportás un HTML para compartir con la mesa.
2. Motor de reglas simplificado y puzzles "ganá este turno".
3. Roguelike de draft contra jefes Commander.

## Party game

La app de escritorio (C#, .NET 8, Avalonia) permite:

- Poner nombre a la partida, vidas iniciales (40 por defecto) y de 2 a 6 jugadores con su color.
- Activar, editar, borrar o agregar **desafíos** (con duración: ahora, una vez, esta ronda o toda la partida), **misiones secretas** con recompensa y **planos** con efecto y caos.
- Guardar y abrir partidas como archivos `.mtgparty`.
- **Exportar HTML**: un único archivo que se manda al grupo y se abre en el navegador del celular, sin instalar nada. Lleva el contador de vidas, el turno y la ronda, los desafíos, las misiones secretas (se reparten al azar al abrirlo) y el dado planar.

Todo el contenido es original. Es un proyecto de fans gratuito y no comercial, dentro de la Fan Content Policy de Wizards of the Coast.

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
