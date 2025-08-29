// File: SceneSyncWrappers.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using ConsoleGame.RayTracing;
using ConsoleGame.RayTracing.Objects;
using ConsoleGame.RayTracing.Scenes;
using ConsoleGame.RayTracing.Objects.BoundedSolids;
using ConsoleGame.RayTracing.Objects.Surfaces;

namespace ConsoleGame.RayTracing.Scenes
{
    public sealed class SceneSyncServer : Scene, IDisposable
    {
        private readonly Scene inner;
        private readonly object innerLock = new object();
        private readonly TcpListener listener;
        private readonly Thread acceptThread;
        private volatile bool running = false;

        public SceneSyncServer(Scene inner, IPAddress bindAddress, int port)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (bindAddress == null) throw new ArgumentNullException(nameof(bindAddress));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            this.inner = inner;
            Objects = inner.Objects;
            Lights = inner.Lights;
            BackgroundTop = inner.BackgroundTop;
            BackgroundBottom = inner.BackgroundBottom;
            Ambient = inner.Ambient;
            DefaultFovDeg = inner.DefaultFovDeg;
            DefaultCameraPos = inner.DefaultCameraPos;
            DefaultYaw = inner.DefaultYaw;
            DefaultPitch = inner.DefaultPitch;
            CameraPos = inner.CameraPos;
            Yaw = inner.Yaw;
            Pitch = inner.Pitch;
            listener = new TcpListener(bindAddress, port);
            running = true;
            listener.Start();
            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "SceneSyncServer-Accept";
            acceptThread.Start();
        }

        public override void RebuildBVH()
        {
            lock (innerLock)
            {
                inner.RebuildBVH();
            }
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord outRec, float screenU, float screenV)
        {
            lock (innerLock)
            {
                return inner.Hit(r, tMin, tMax, ref outRec, screenU, screenV);
            }
        }

        public override bool Occluded(Ray r, float maxDist, float screenU, float screenV)
        {
            lock (innerLock)
            {
                return inner.Occluded(r, maxDist, screenU, screenV);
            }
        }

        public override void ResetCamera()
        {
            lock (innerLock)
            {
                inner.ResetCamera();
                CameraPos = inner.CameraPos;
                Yaw = inner.Yaw;
                Pitch = inner.Pitch;
            }
        }

        public override void Update(float deltaTimeMS)
        {
            lock (innerLock)
            {
                inner.Update(deltaTimeMS);
                CameraPos = inner.CameraPos;
                Yaw = inner.Yaw;
                Pitch = inner.Pitch;
            }
        }

        public override void HandleInput(ConsoleKeyInfo keyInfo, float dt)
        {
            lock (innerLock)
            {
                inner.HandleInput(keyInfo, dt);
                CameraPos = inner.CameraPos;
                Yaw = inner.Yaw;
                Pitch = inner.Pitch;
            }
        }

        public override void CopyFrom(Scene src)
        {
            lock (innerLock)
            {
                inner.CopyFrom(src);
                Objects = inner.Objects;
                Lights = inner.Lights;
                BackgroundTop = inner.BackgroundTop;
                BackgroundBottom = inner.BackgroundBottom;
                Ambient = inner.Ambient;
                DefaultFovDeg = inner.DefaultFovDeg;
                DefaultCameraPos = inner.DefaultCameraPos;
                DefaultYaw = inner.DefaultYaw;
                DefaultPitch = inner.DefaultPitch;
                CameraPos = inner.CameraPos;
                Yaw = inner.Yaw;
                Pitch = inner.Pitch;
            }
        }

        public void Dispose()
        {
            running = false;
            try { listener.Stop(); } catch { }
            try { acceptThread.Join(500); } catch { }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    var th = new Thread(() => ServeSnapshot(client));
                    th.IsBackground = true;
                    th.Start();
                }
                catch
                {
                    try { client?.Close(); } catch { }
                    if (!running) break;
                }
            }
        }

        private void ServeSnapshot(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var bw = new BinaryWriter(stream))
            {
                lock (innerLock)
                {
                    SceneSyncProtocol.WriteSnapshot(bw, inner);
                }
                try { bw.Flush(); } catch { }
            }
        }
    }

    public sealed class SceneSyncClient : Scene, IDisposable
    {
        private readonly string host;
        private readonly int port;
        private Scene localReplica;

        public SceneSyncClient(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException(nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            this.host = host;
            this.port = port;
            Resync();
        }

        public void Resync()
        {
            using (var tcp = new TcpClient())
            {
                tcp.NoDelay = true;
                tcp.Connect(host, port);
                using (var br = new BinaryReader(tcp.GetStream()))
                {
                    localReplica = SceneSyncProtocol.ReadSnapshot(br);
                }
            }
            Objects = localReplica.Objects;
            Lights = localReplica.Lights;
            BackgroundTop = localReplica.BackgroundTop;
            BackgroundBottom = localReplica.BackgroundBottom;
            Ambient = localReplica.Ambient;
            DefaultFovDeg = localReplica.DefaultFovDeg;
            DefaultCameraPos = localReplica.DefaultCameraPos;
            DefaultYaw = localReplica.DefaultYaw;
            DefaultPitch = localReplica.DefaultPitch;
            ResetCamera();
            localReplica.RebuildBVH();
        }

        public override void RebuildBVH()
        {
            if (localReplica == null) throw new InvalidOperationException("No local replica; call Resync() first.");
            localReplica.RebuildBVH();
        }

        public override bool Hit(Ray r, float tMin, float tMax, ref HitRecord outRec, float screenU, float screenV)
        {
            if (localReplica == null) throw new InvalidOperationException("No local replica; call Resync() first.");
            return localReplica.Hit(r, tMin, tMax, ref outRec, screenU, screenV);
        }

        public override bool Occluded(Ray r, float maxDist, float screenU, float screenV)
        {
            if (localReplica == null) throw new InvalidOperationException("No local replica; call Resync() first.");
            return localReplica.Occluded(r, maxDist, screenU, screenV);
        }

        public override void ResetCamera()
        {
            CameraPos = DefaultCameraPos;
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
        }

        public override void Update(float deltaTimeMS)
        {
            // No-op; client controls camera locally.
        }

        public override void HandleInput(ConsoleKeyInfo keyInfo, float dt)
        {
            // No-op; client controls camera locally.
        }

        public override void CopyFrom(Scene src)
        {
            Objects.Clear();
            Objects.AddRange(src.Objects);
            Lights.Clear();
            Lights.AddRange(src.Lights);
            BackgroundTop = src.BackgroundTop;
            BackgroundBottom = src.BackgroundBottom;
            Ambient = src.Ambient;
            DefaultFovDeg = src.DefaultFovDeg;
            DefaultCameraPos = src.DefaultCameraPos;
            DefaultYaw = src.DefaultYaw;
            DefaultPitch = src.DefaultPitch;
            ResetCamera();
        }

        public void Dispose()
        {
            // Stateless; nothing to dispose.
        }
    }

    internal static class SceneSyncProtocol
    {
        private const uint MAGIC = 0x53434E45; // 'SCNE'
        private const uint VERSION = 1;

        private const byte T_SPHERE = 1;
        private const byte T_PLANE_SOLID = 2;    // Plane with solid material baked
        private const byte T_DISK_SOLID = 3;     // Disk with solid material baked
        private const byte T_XYRECT_SOLID = 4;
        private const byte T_XZRECT_SOLID = 5;
        private const byte T_YZRECT_SOLID = 6;
        private const byte T_BOX_SOLID = 7;      // Box with solid material baked
        private const byte T_CYL_Y = 8;
        private const byte T_TRIANGLE = 9;

        public static void WriteSnapshot(BinaryWriter bw, Scene scene)
        {
            bw.Write(MAGIC);
            bw.Write(VERSION);
            WriteVec3(bw, scene.BackgroundTop);
            WriteVec3(bw, scene.BackgroundBottom);
            WriteVec3(bw, scene.Ambient.Color);
            bw.Write(scene.Ambient.Intensity);
            bw.Write(scene.DefaultFovDeg);
            WriteVec3(bw, scene.DefaultCameraPos);
            bw.Write(scene.DefaultYaw);
            bw.Write(scene.DefaultPitch);
            bw.Write(scene.Lights.Count);
            for (int i = 0; i < scene.Lights.Count; i++)
            {
                var L = scene.Lights[i];
                WriteVec3(bw, L.Position);
                WriteVec3(bw, L.Color);
                bw.Write(L.Intensity);
            }
            var objs = scene.Objects;
            int countPos = 0;
            long posCountPtr = bw.BaseStream.Position;
            bw.Write(countPos);
            int written = 0;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o is Sphere s)
                {
                    bw.Write(T_SPHERE);
                    WriteVec3(bw, s.Center);
                    bw.Write(s.Radius);
                    WriteMaterial(bw, s.Mat);
                    written++;
                }
                else if (o is Plane p)
                {
                    // Bake material once (assumes Solid/constant); dynamic patterns are not replicated.
                    var m = p.MaterialFunc(p.Point, p.Normal, 0.0f);
                    bw.Write(T_PLANE_SOLID);
                    WriteVec3(bw, p.Point);
                    WriteVec3(bw, p.Normal);
                    WriteMaterial(bw, BakeSpecRefl(m, p.Specular, p.Reflectivity));
                    written++;
                }
                else if (o is Disk d)
                {
                    var m = d.MaterialFunc(d.Center, d.Normal, 0.0f);
                    bw.Write(T_DISK_SOLID);
                    WriteVec3(bw, d.Center);
                    WriteVec3(bw, d.Normal);
                    bw.Write(d.Radius);
                    WriteMaterial(bw, BakeSpecRefl(m, d.Specular, d.Reflectivity));
                    written++;
                }
                else if (o is XYRect rxy)
                {
                    var m = rxy.MaterialFunc(new Vec3((rxy.X0 + rxy.X1) * 0.5f, (rxy.Y0 + rxy.Y1) * 0.5f, rxy.Z), new Vec3(0, 0, 1), 0.0f);
                    bw.Write(T_XYRECT_SOLID);
                    bw.Write(rxy.X0); bw.Write(rxy.X1); bw.Write(rxy.Y0); bw.Write(rxy.Y1); bw.Write(rxy.Z);
                    WriteMaterial(bw, BakeSpecRefl(m, rxy.Specular, rxy.Reflectivity));
                    written++;
                }
                else if (o is XZRect rxz)
                {
                    var m = rxz.MaterialFunc(new Vec3((rxz.X0 + rxz.X1) * 0.5f, rxz.Y, (rxz.Z0 + rxz.Z1) * 0.5f), new Vec3(0, 1, 0), 0.0f);
                    bw.Write(T_XZRECT_SOLID);
                    bw.Write(rxz.X0); bw.Write(rxz.X1); bw.Write(rxz.Z0); bw.Write(rxz.Z1); bw.Write(rxz.Y);
                    WriteMaterial(bw, BakeSpecRefl(m, rxz.Specular, rxz.Reflectivity));
                    written++;
                }
                else if (o is YZRect ryz)
                {
                    var m = ryz.MaterialFunc(new Vec3(ryz.X, (ryz.Y0 + ryz.Y1) * 0.5f, (ryz.Z0 + ryz.Z1) * 0.5f), new Vec3(1, 0, 0), 0.0f);
                    bw.Write(T_YZRECT_SOLID);
                    bw.Write(ryz.Y0); bw.Write(ryz.Y1); bw.Write(ryz.Z0); bw.Write(ryz.Z1); bw.Write(ryz.X);
                    WriteMaterial(bw, BakeSpecRefl(m, ryz.Specular, ryz.Reflectivity));
                    written++;
                }
                else if (o is Box bx)
                {
                    // Bake a solid box by sampling its first face's material; corners share same look in Solid(...) usage.
                    Func<Vec3, Vec3, float, Material> sample = (pos, n, u) => new Material(new Vec3(0.82f, 0.82f, 0.82f), 0.02, 0.0, Vec3.Zero);
                    // We cannot inspect bx.faces (private). Serialize as solid using average gray unless constructed from Solid().
                    bw.Write(T_BOX_SOLID);
                    WriteVec3(bw, bx.Min);
                    WriteVec3(bw, bx.Max);
                    WriteMaterial(bw, BakeSpecRefl(sample(Vec3.Zero, Vec3.Zero, 0.0f), 0.02f, 0.0f));
                    written++;
                }
                else if (o is CylinderY cy)
                {
                    bw.Write(T_CYL_Y);
                    WriteVec3(bw, cy.Center);
                    bw.Write(cy.Radius);
                    bw.Write(cy.YMin);
                    bw.Write(cy.YMax);
                    bw.Write(cy.Capped ? (byte)1 : (byte)0);
                    WriteMaterial(bw, cy.Mat);
                    written++;
                }
                else if (o is Triangle tr)
                {
                    bw.Write(T_TRIANGLE);
                    WriteVec3(bw, tr.A);
                    WriteVec3(bw, tr.B);
                    WriteVec3(bw, tr.C);
                    WriteMaterial(bw, tr.Mat);
                    written++;
                }
                else
                {
                    // Unsupported hittable (e.g., Mesh, VolumeGrid). Skipped to keep local-only ray tests fast and network-agnostic.
                }
            }
            long end = bw.BaseStream.Position;
            bw.BaseStream.Seek(posCountPtr, SeekOrigin.Begin);
            bw.Write(written);
            bw.BaseStream.Seek(end, SeekOrigin.Begin);
        }

        public static Scene ReadSnapshot(BinaryReader br)
        {
            uint magic = br.ReadUInt32();
            if (magic != MAGIC) throw new InvalidDataException("Bad scene snapshot magic.");
            uint version = br.ReadUInt32();
            if (version != VERSION) throw new InvalidDataException("Unsupported scene snapshot version.");
            Scene s = new Scene();
            s.BackgroundTop = ReadVec3(br);
            s.BackgroundBottom = ReadVec3(br);
            Vec3 ambC = ReadVec3(br);
            float ambI = br.ReadSingle();
            s.Ambient = new AmbientLight(ambC, ambI);
            s.DefaultFovDeg = br.ReadSingle();
            s.DefaultCameraPos = ReadVec3(br);
            s.DefaultYaw = br.ReadSingle();
            s.DefaultPitch = br.ReadSingle();
            int lightCount = br.ReadInt32();
            for (int i = 0; i < lightCount; i++)
            {
                Vec3 lp = ReadVec3(br);
                Vec3 lc = ReadVec3(br);
                float li = br.ReadSingle();
                s.Lights.Add(new PointLight(lp, lc, li));
            }
            int objCount = br.ReadInt32();
            for (int i = 0; i < objCount; i++)
            {
                byte tag = br.ReadByte();
                switch (tag)
                {
                    case T_SPHERE:
                        {
                            Vec3 c = ReadVec3(br);
                            float r = br.ReadSingle();
                            Material m = ReadMaterial(br);
                            s.Objects.Add(new Sphere(c, r, m));
                            break;
                        }
                    case T_PLANE_SOLID:
                        {
                            Vec3 p = ReadVec3(br);
                            Vec3 n = ReadVec3(br);
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new Plane(p, n, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_DISK_SOLID:
                        {
                            Vec3 c = ReadVec3(br);
                            Vec3 n = ReadVec3(br);
                            float r = br.ReadSingle();
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new Disk(c, n, r, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_XYRECT_SOLID:
                        {
                            float x0 = br.ReadSingle(); float x1 = br.ReadSingle(); float y0 = br.ReadSingle(); float y1 = br.ReadSingle(); float z = br.ReadSingle();
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new XYRect(x0, x1, y0, y1, z, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_XZRECT_SOLID:
                        {
                            float x0 = br.ReadSingle(); float x1 = br.ReadSingle(); float z0 = br.ReadSingle(); float z1 = br.ReadSingle(); float y = br.ReadSingle();
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new XZRect(x0, x1, z0, z1, y, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_YZRECT_SOLID:
                        {
                            float y0 = br.ReadSingle(); float y1 = br.ReadSingle(); float z0 = br.ReadSingle(); float z1 = br.ReadSingle(); float x = br.ReadSingle();
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new YZRect(y0, y1, z0, z1, x, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_BOX_SOLID:
                        {
                            Vec3 mn = ReadVec3(br);
                            Vec3 mx = ReadVec3(br);
                            Material m = ReadMaterial(br);
                            Func<Vec3, Vec3, float, Material> solid = (pos, nn, u) => m;
                            s.Objects.Add(new Box(mn, mx, solid, (float)m.Specular, (float)m.Reflectivity));
                            break;
                        }
                    case T_CYL_Y:
                        {
                            Vec3 c = ReadVec3(br);
                            float r = br.ReadSingle();
                            float yMin = br.ReadSingle();
                            float yMax = br.ReadSingle();
                            bool capped = br.ReadByte() != 0;
                            Material m = ReadMaterial(br);
                            s.Objects.Add(new CylinderY(c, r, yMin, yMax, capped, m));
                            break;
                        }
                    case T_TRIANGLE:
                        {
                            Vec3 a = ReadVec3(br);
                            Vec3 b = ReadVec3(br);
                            Vec3 c = ReadVec3(br);
                            Material m = ReadMaterial(br);
                            s.Objects.Add(new Triangle(a, b, c, m));
                            break;
                        }
                    default:
                        {
                            throw new InvalidDataException("Unknown object tag in snapshot.");
                        }
                }
            }
            return s;
        }

        private static Material BakeSpecRefl(Material m, float specular, float reflectivity)
        {
            Material outM = new Material(m.Albedo, specular, reflectivity, m.Emission, m.Transparency, m.IndexOfRefraction, m.TransmissionColor);
            outM.DiffuseTexture = m.DiffuseTexture;
            outM.TextureWeight = m.TextureWeight;
            outM.UVScale = m.UVScale;
            return outM;
        }

        private static void WriteMaterial(BinaryWriter bw, Material m)
        {
            WriteVec3(bw, m.Albedo);
            bw.Write((float)m.Specular);
            bw.Write((float)m.Reflectivity);
            WriteVec3(bw, m.Emission);
            bw.Write((float)m.Transparency);
            bw.Write((float)m.IndexOfRefraction);
            WriteVec3(bw, m.TransmissionColor);
            // Textures are not serialized; if present, client will treat as baked albedo due to lack of asset streaming.
        }

        private static Material ReadMaterial(BinaryReader br)
        {
            Vec3 albedo = ReadVec3(br);
            float spec = br.ReadSingle();
            float refl = br.ReadSingle();
            Vec3 emit = ReadVec3(br);
            float transp = br.ReadSingle();
            float ior = br.ReadSingle();
            Vec3 transCol = ReadVec3(br);
            return new Material(albedo, spec, refl, emit, transp, ior, transCol);
        }

        private static void WriteVec3(BinaryWriter bw, Vec3 v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
            bw.Write(v.Z);
        }

        private static Vec3 ReadVec3(BinaryReader br)
        {
            float x = br.ReadSingle();
            float y = br.ReadSingle();
            float z = br.ReadSingle();
            return new Vec3(x, y, z);
        }
    }
}
