# Ray Tracing with Console.Write()

![screenshot](Assets/Screenshot%202025-08-17%20182206.png)

A real-time CPU ray tracer that renders to a text console.

I have written this code a few times, and this is probably the most complete it has ever gotten. 

On Behest of the reddit comments I implemented a renderer that uses PInvoke to directly write into the terminal memory buffer, and a renderer that uses ANSI color codes.

Example video of the ANSITerminalRenderer providing a MASSIVE speedup, the PInvoke is about the same speed but plays less nicely with the debug text printing.
<video src="Assets/RTConsole.mp4" controls width="720"></video>

---

## TL;DR

- Arrow keys: look
- WASD / Q / E: move (hold Shift to go faster)
- I / U: next / previous scene
- Esc: quit
---

## Scenes

Press I/U to cycle. The table is built in `RaytraceEntity.BuildSceneTable()` and includes:
- Test/Demo scenes
- Cornell Box
- Mirror spheres on a checker plane
- Cylinders, disks, triangles, and boxes showcase
- Volume grid test and “minecraft-like” voxel scene
- Mesh scenes: Bunny, Teapot, Cow, Dragon
Each scene provides default camera pose and FOV; switching resets TAA history and rebuilds the BVH.

---
