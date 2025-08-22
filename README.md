# Ray Tracing with Console.Write()

![screenshot](Assets/Screenshot%202025-08-17%20182206.png)

A real-time CPU ray tracer that renders to a text console.

I have written this code a few times, and this is probably the most complete it has ever gotten. 

On Behest of the reddit comments I implemented a renderer that uses PInvoke to directly write into the terminal memory buffer, and a renderer that uses ANSI color codes.

Example video of the ANSITerminalRenderer providing a MASSIVE speedup, the PInvoke is about the same speed but plays less nicely with the debug text printing.

Higher Quality version here: [Assets/RTConsole.mp4]


https://github.com/user-attachments/assets/d6c066e6-49c5-428d-923f-66186bd413fd

You need to change a line in Program.cs to activate it but there is a video reader mode which allows reading cameras and arbitrary video files

https://github.com/user-attachments/assets/71801318-bc0e-4d4c-9efb-66c455577134

---
High Res

<img width="2560" height="1393" alt="Screenshot 2025-08-22 024508" src="https://github.com/user-attachments/assets/3e7a0746-0a45-497f-98af-f3e620bd74c1" />

Low Res

<img width="2560" height="1393" alt="Screenshot 2025-08-22 024524" src="https://github.com/user-attachments/assets/cf4caf98-ffc1-4ceb-8118-faeaddb3f5bf" />

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
