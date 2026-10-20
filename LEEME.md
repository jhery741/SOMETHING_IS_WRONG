# ANOMALY: DESCENT

Unity 6000.5.5f1 · Built-in Render Pipeline · Windows 64 bits.

## Ejecutar

Abrir `Builds/Windows_Release/AnomalyDescent.exe`. Conservar toda su carpeta: `AnomalyDescent_Data`, `MonoBleedingEdge`, DLL y demás dependencias de Unity son necesarias.

`Builds/Windows_Dev` contiene la versión de diagnóstico y rendimiento. Para jugar, usar release.

## Juego

Una casa y un monstruo: El Ciego. Las cuatro vueltas piden 1, 2, 3 y 3 anomalías. Observar, fotografiar el cambio con calidad Nítida y reportar su habitación y categoría desde el carrete. La radio del refugio cierra cada vuelta y guarda el progreso. Tras la cuarta, salir por la puerta principal del recibidor.

El refugio protege al jugador. El baño conserva la bañera junto a la entrada como segundo refugio físico. El Ciego escucha pasos, puertas, flash, voz y botellas. Las voces definitivas son susurros sintetizados con subtítulos; las animaciones son los siete clips de Meshy aportados por el usuario.

## Controles

| Acción | Control |
|---|---|
| Caminar / mirar | WASD / ratón; también J/L e I/K |
| Correr / agacharse | Shift / C o Ctrl |
| Interactuar | E |
| Linterna | F |
| Cámara | Clic derecho mantenido o Q |
| Zoom / foto | Rueda o Z/X / clic izquierdo o Espacio |
| Carrete / cambiar foto | Tab / A-D |
| Reportar | Enter, habitación y categoría |
| Responder | 1, 2 o 3 |
| Lanzar botella | G |
| Pausa y opciones | Esc |

## Opciones

Volúmenes general, ambiente, efectos y voces; sensibilidad, inversión vertical, balanceo, subtítulos, FOV, grano y viñeta. Resolución Nativa / 1440 / 1080 / 720: altura máxima, proporción conservada y sin ampliar sobre la resolución inicial. Se aplica al momento y se guarda con las opciones. El ambiente está restaurado al 100 %. El progreso previo del usuario se ha conservado.

## Proyecto

Abrir esta carpeta en Unity 6000.5.5f1. Las únicas escenas finales son `Assets/AnomalyDescent/Scenes/Menu.unity` y `Casa.unity`. El escenario, mobiliario y monstruo están guardados en la escena. Los archivos `.meta`, `Packages` y `ProjectSettings` son necesarios. Unity regenera sus cachés al abrir.

Menú `ANOMALY DESCENT > Dev`: compilar release, development o ambas. `Validar escena abierta` comprueba referencias y configuración. Development mantiene registro de partida, contadores F9 y medición de rendimiento; se retiraron los atajos que completaban vueltas o forzaban anomalías.

Las licencias y atribuciones están en los créditos, `Assets/AnomalyDescent/Art/LICENCIAS.md` y las licencias originales conservadas con los recursos. Los resultados y alcance de las comprobaciones finales están en `Capturas` y `CIERRE.md`.
