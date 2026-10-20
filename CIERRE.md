# Cierre y limpieza — 18/10/2026

Se cierra el alcance por decisión del usuario, que acepta el estado jugable y solicita los últimos builds y retirar contenido obsoleto.

## Retirado del proyecto

- Respaldos históricos, Game.zip antiguo, copia accidental, informes y herramientas de sesiones anteriores.
- Escenas Piso1/Piso2/Piso3 y sus recursos exclusivos; solo Menu y Casa forman parte del juego.
- El Torcido, La Presencia y el sistema antiguo de pisos y escaleras.
- Recogidas de pilas y botellas, lámparas oscilantes y bucles ambientales antiguos sin instancias ni referencias en el contenido vigente.
- Sistema antiguo de notas y grabaciones sin instancias en el juego, sus paneles e integración de pistas.
- Generación del monstruo provisional y texturas procedurales antiguas; se conserva el grano del HUD que sí se usa.
- Animaciones provisionales, materiales y texturas sin dependencias en las escenas finales ni en Resources.
- Importadores, migraciones, colocadores y pruebas de etapas anteriores; teletransportes, visor de animación y atajos para forzar la partida.

La retirada se basó en dependencias de AssetDatabase, búsquedas de GUID en assets vigentes y referencias de código. Se conservan fuentes del juego, iluminación, animaciones definitivas, audio, créditos y dependencias de Unity. La auditoría posterior a la retirada de assets devolvió 0 candidatos sin referencias (conservando Resources y scripts como raíces).

## Comprobaciones

Validación de Casa tras limpieza: 0 errores, 0 scripts perdidos, 7 habitaciones, 8 conexiones de puertas, 1 escondite y 15/15 anomalías válidas. Hay 3 avisos de diseño/arte; el baño mantiene su bloqueo físico intencionado.

Los resultados exactos de los nuevos builds están en `Capturas/build_release.txt` y `build_development.txt`. Se generaron inicialmente sobre carpetas vacías. Development conserva PerfRecorder y registros útiles para diagnóstico; release excluye las funciones de desarrollo.

Última compilación: release 22:09:34 y development 22:09:53 (hora de Bolivia), ambos Succeeded, 0 errores y 1 aviso del shader de ProBuilder. Se retiró también un bucle redundante del HUD que generaba el aviso de código inalcanzable, conservando su comportamiento. Development abre su menú correctamente; registro en `Capturas/development_arranque_Player.log`. El aviso al liberar una RenderTexture durante el cierre de development se conserva en ese registro; no es una excepción de referencias del juego.

El progreso original del ejecutable se restauró antes de la limpieza (vuelta 1, sin marca de victoria). Las preferencias de audio se conservaron.

Comprobación manual de arranque del release limpio: menú, opciones, ambiente al 100 %, cambio inmediato 1080 → 720 → 1080 (confirmado en Player.log), carga de Casa, inicio de vuelta 1, cámara, foto Nítida, carrete y pausa con todas las opciones y botones visibles. Cierre desde Salir. El registro de esta prueba no contiene excepciones ni referencias ausentes. Evidencia: `Capturas/release_arranque_Player.log`.

Alcance: las pruebas de integración anteriores no equivalen a una partida completa manual de release. La última prueba automática exhaustiva de rutas anterior al cierre registró un atasco Sala → Dormitorio; no se afirma haberlo corregido en esta limpieza. No se acredita aquí una nueva sesión de rendimiento de 120 segundos ni una partida completa de cuatro vueltas.

## Recuperación externa

El contenido retirado queda fuera de la carpeta del juego, con el manifiesto `ARCHIVOS_RETIRADOS.txt`, en:

`C:\Users\franz\.codex\visualizations\2026\10\18\01a1506a-39b4-7100-a268-37a10ab46524\Recuperacion_Anomaly_20261018`

Esta carpeta externa no forma parte del proyecto ni de la entrega jugable.

Unity se cerró antes de retirar Library/Temp y otros archivos regenerables: 3,14 GB de cachés eliminadas y 8 directorios vacíos retirados. Logs, configuración personal del editor, herramientas temporales de esta limpieza y símbolos Burst marcados DoNotShip se retiraron de la entrega y se conservaron en la recuperación externa. La carpeta raíz queda con Assets, Builds, Capturas, Packages, ProjectSettings, .gitignore, LEEME.md y este informe. Al abrir de nuevo el proyecto, Unity regenerará su caché.
