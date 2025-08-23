// File: OpenGLTerminalRenderer.cs
using System;
using System.Collections.Generic;
using ConsoleGame.RayTracing;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ConsoleGame.Renderer
{
    public sealed class OpenGLTerminalRenderer : IDisposable
    {
        private readonly List<Framebuffer> frameBuffers;
        public int consoleWidth;
        public int consoleHeight;
        private readonly Action<int, int> onResize;
        private readonly string windowTitle;

        private GameWindow window;
        private bool initialized = false;
        private bool disposed = false;

        private int texId = 0;
        private int vao = 0;
        private int vbo = 0;
        private int program = 0;

        private int uploadedWidth = 0;
        private int uploadedHeight = 0;

        private byte[] composeBuffer = null;

        private int scale;
        private readonly int minScale = 1;
        private readonly int maxScale = 64;
        private bool resizingInternally = false;

        public OpenGLTerminalRenderer(Action<int, int> onResize, int initialCellsW = 120, int initialCellsH = 40, int initialScale = 8, string title = "OpenGL Terminal Renderer")
        {
            this.onResize = onResize;
            this.windowTitle = string.IsNullOrWhiteSpace(title) ? "OpenGL Terminal Renderer" : title;
            frameBuffers = new List<Framebuffer>();
            consoleWidth = Math.Max(1, initialCellsW);
            consoleHeight = Math.Max(1, initialCellsH);
            scale = Math.Clamp(initialScale, minScale, maxScale);
            CreateWindowOnThisThread(consoleWidth, consoleHeight, scale, this.windowTitle);
            InitGLResources();
            initialized = true;
        }

        public void AddFrameBuffer(Framebuffer fb)
        {
            if (fb == null) return;
            frameBuffers.Add(fb);
        }

        public void RemoveFrameBuffer(Framebuffer fb)
        {
            if (fb == null) return;
            frameBuffers.Remove(fb);
        }

        public void SetGridSize(int cellsW, int cellsH)
        {
            int w = Math.Max(1, cellsW);
            int h = Math.Max(1, cellsH);
            if (w == consoleWidth && h == consoleHeight)
            {
                return;
            }
            consoleWidth = w;
            consoleHeight = h;
            UpdateWindowSizeToAspect();
            onResize?.Invoke(consoleWidth, consoleHeight);
        }

        public void Render()
        {
            if (!initialized || window == null || window.IsExiting) return;

            try
            {
                Console.SetCursorPosition(0, 0);
            }
            catch { }

            window.MakeCurrent();
            window.IsEventDriven = false;
            window.ProcessEvents(0.001);

            int pxW = consoleWidth;
            int pxH = consoleHeight * 2;
            int totalBytes = pxW * pxH * 4;
            if (composeBuffer == null || composeBuffer.Length != totalBytes)
            {
                composeBuffer = new byte[totalBytes];
            }

            for (int y = 0; y < pxH; y++)
            {
                int cellY = y >> 1;
                bool top = (y & 1) == 0;
                for (int x = 0; x < pxW; x++)
                {
                    Chexel c = GetChexelForPoint(x, cellY);
                    ChexelColor cc = top ? c.ForegroundColor : c.BackgroundColor;
                    byte r = LinearToSrgb8(cc.color_f32.X);
                    byte g = LinearToSrgb8(cc.color_f32.Y);
                    byte b = LinearToSrgb8(cc.color_f32.Z);
                    int idx = (y * pxW + x) * 4;
                    composeBuffer[idx + 0] = r;
                    composeBuffer[idx + 1] = g;
                    composeBuffer[idx + 2] = b;
                    composeBuffer[idx + 3] = 255;
                }
            }

            UploadTexture(pxW, pxH, composeBuffer);

            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.UseProgram(program);
            GL.BindVertexArray(vao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texId);
            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
            window.SwapBuffers();

            SnapViewportIfWindowWasResized();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (window != null)
            {
                if (!window.IsExiting) window.Close();
                window.MakeCurrent();
                if (program != 0) { GL.DeleteProgram(program); program = 0; }
                if (vbo != 0) { GL.DeleteBuffer(vbo); vbo = 0; }
                if (vao != 0) { GL.DeleteVertexArray(vao); vao = 0; }
                if (texId != 0) { GL.DeleteTexture(texId); texId = 0; }
                window.Context?.MakeNoneCurrent();
                window.Dispose();
                window = null;
            }
        }

        private void CreateWindowOnThisThread(int cellsW, int cellsH, int scaleInit, string title)
        {
            GameWindowSettings gws = GameWindowSettings.Default;
            gws.UpdateFrequency = 0.0;

            int winW = Math.Max(1, cellsW) * scaleInit;
            int winH = Math.Max(1, cellsH * 2) * scaleInit;

            NativeWindowSettings nws = new NativeWindowSettings();
            nws.Size = new Vector2i(winW, winH);
            nws.Title = title;
            nws.API = ContextAPI.OpenGL;
            nws.APIVersion = new Version(3, 3);
            nws.Profile = ContextProfile.Core;
            nws.Flags = ContextFlags.ForwardCompatible;
            nws.StartVisible = true;
            nws.StartFocused = true;

            window = new GameWindow(gws, nws);
            window.IsEventDriven = false;
            window.VSync = VSyncMode.Off;
            window.Resize += OnResizeEvent;
            window.Closing += OnClosingEvent;
            window.MouseWheel += OnMouseWheelEvent;

            window.MakeCurrent();
            GL.Viewport(0, 0, winW, winH);
            GL.ClearColor(0f, 0f, 0f, 1f);
        }

        private void OnMouseWheelEvent(MouseWheelEventArgs e)
        {
            int step = e.OffsetY > 0 ? 1 : (e.OffsetY < 0 ? -1 : 0);
            if (step == 0) return;
            int newScale = Math.Clamp(scale + step, minScale, maxScale);
            if (newScale == scale) return;
            scale = newScale;
            UpdateWindowSizeToAspect();
        }

        private void OnResizeEvent(ResizeEventArgs e)
        {
            if (resizingInternally)
            {
                GL.Viewport(0, 0, e.Width, e.Height);
                return;
            }

            int targetW = consoleWidth * scale;
            int targetH = consoleHeight * 2 * scale;

            int sx = Math.Max(1, e.Width / Math.Max(1, consoleWidth));
            int sy = Math.Max(1, e.Height / Math.Max(1, consoleHeight * 2));
            int snapped = Math.Clamp(Math.Min(sx, sy), minScale, maxScale);

            if (snapped != scale || e.Width != targetW || e.Height != targetH)
            {
                scale = snapped;
                UpdateWindowSizeToAspect();
            }
            else
            {
                GL.Viewport(0, 0, e.Width, e.Height);
            }
        }

        private void OnClosingEvent(System.ComponentModel.CancelEventArgs e)
        {
        }

        private void InitGLResources()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            float[] verts = new float[]
            {
                -1f, -1f,  0f, 1f,
                 1f, -1f,  1f, 1f,
                 1f,  1f,  1f, 0f,
                -1f,  1f,  0f, 0f
            };

            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            int vs = GL.CreateShader(ShaderType.VertexShader);
            int fs = GL.CreateShader(ShaderType.FragmentShader);

            string vsrc = "#version 330 core\nlayout(location=0) in vec2 aPos;\nlayout(location=1) in vec2 aUV;\nout vec2 vUV;\nvoid main()\n{\n    vUV = aUV;\n    gl_Position = vec4(aPos, 0.0, 1.0);\n}\n";
            string fsrc = "#version 330 core\nin vec2 vUV;\nout vec4 FragColor;\nuniform sampler2D uTex;\nvoid main()\n{\n    FragColor = texture(uTex, vUV);\n}\n";

            GL.ShaderSource(vs, vsrc);
            GL.CompileShader(vs);
            CheckShader(vs, "vertex");

            GL.ShaderSource(fs, fsrc);
            GL.CompileShader(fs);
            CheckShader(fs, "fragment");

            program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            CheckProgram(program);
            GL.DetachShader(program, vs);
            GL.DetachShader(program, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            GL.UseProgram(program);
            int loc = GL.GetUniformLocation(program, "uTex");
            if (loc >= 0) GL.Uniform1(loc, 0);
            GL.UseProgram(0);

            texId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private void UploadTexture(int w, int h, byte[] rgba)
        {
            GL.BindTexture(TextureTarget.Texture2D, texId);
            if (w != uploadedWidth || h != uploadedHeight)
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
                uploadedWidth = w;
                uploadedHeight = h;
            }
            else
            {
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            }
        }

        private Chexel GetChexelForPoint(int screenX, int screenY)
        {
            for (int i = frameBuffers.Count - 1; i >= 0; i--)
            {
                Framebuffer fb = frameBuffers[i];
                int fbX = screenX - fb.ViewportX;
                int fbY = screenY - fb.ViewportY;
                if (fbX >= 0 && fbX < fb.Width && fbY >= 0 && fbY < fb.Height)
                {
                    Chexel chexel = fb.GetChexel(fbX, fbY);
                    return chexel;
                }
            }
            return new Chexel(' ', ConsoleColor.White, ConsoleColor.Black);
        }

        private void UpdateWindowSizeToAspect()
        {
            if (window == null || window.IsExiting) return;
            int targetW = consoleWidth * scale;
            int targetH = consoleHeight * 2 * scale;
            resizingInternally = true;
            window.Size = new Vector2i(targetW, targetH);
            GL.Viewport(0, 0, targetW, targetH);
            resizingInternally = false;
        }

        private void SnapViewportIfWindowWasResized()
        {
            if (window == null || window.IsExiting) return;
            int targetW = consoleWidth * scale;
            int targetH = consoleHeight * 2 * scale;
            if (window.Size.X != targetW || window.Size.Y != targetH)
            {
                UpdateWindowSizeToAspect();
            }
        }

        private static byte LinearToSrgb8(double c)
        {
            double v = c;
            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;
            double s = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
            int i = (int)Math.Round(s * 255.0);
            if (i < 0) i = 0;
            if (i > 255) i = 255;
            return (byte)i;
        }

        private static void CheckShader(int shader, string label)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == (int)All.True) return;
            string log = GL.GetShaderInfoLog(shader);
            throw new InvalidOperationException("GL " + label + " shader compile failed: " + log);
        }

        private static void CheckProgram(int prog)
        {
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == (int)All.True) return;
            string log = GL.GetProgramInfoLog(prog);
            throw new InvalidOperationException("GL program link failed: " + log);
        }
    }
}
