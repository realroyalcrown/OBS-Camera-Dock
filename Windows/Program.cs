using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CameraDock {
    static class Program {
        [STAThread]
        static int Main(string[] args) {
            try {
                string preferred = Option(args, "--camera") ?? "Kiyo";
                string data = Option(args, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBS Camera Dock");
                string diagnose = Option(args, "--diagnose");
                if (diagnose != null) {
                    using (var camera = new Camera(preferred)) {
                        File.WriteAllText(diagnose, new JavaScriptSerializer().Serialize(new {
                            connected = camera.Connected, cameraName = camera.Name, deviceId = camera.DeviceId,
                            cameras = camera.Devices, message = camera.Error, controls = camera.Describe()
                        }), Encoding.UTF8);
                    }
                    return 0;
                }
                if (args.Contains("--self-test")) { SelfTest.Run(data); return 0; }
                int port = Option(args,"--port")==null ? 24680 : Int32.Parse(Option(args,"--port"));
                using (var server = new Server(preferred, data, port)) {
                    server.Start();
                    if (args.Contains("--headless")) { server.Join(); return 0; }
                    Application.EnableVisualStyles();
                    using (var iconStream=typeof(Program).Assembly.GetManifestResourceStream("camera.ico"))
                    using (var icon=new Icon(iconStream))
                    using (var tray = new NotifyIcon()) using (var menu = new ContextMenuStrip()) {
                        menu.Items.Add("Otevřít ovládání kamery", null, delegate { Process.Start(server.Url); });
                        menu.Items.Add("Kopírovat URL", null, delegate { Clipboard.SetText(server.Url); });
                        menu.Items.Add("Znovu vyhledat kamery", null, delegate { server.RequestRescan(); });
                        menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add("realroyalcrown.eu", null, delegate { Process.Start("https://realroyalcrown.eu"); });
                        menu.Items.Add(Updates.Label).Enabled=false;
                        var updateItem=menu.Items.Add("Zkontrolovat aktualizace…");
                        updateItem.Click += delegate { Updates.Check(updateItem); };
                        menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add("Ukončit", null, delegate { Application.Exit(); });
                        tray.Icon = icon; tray.Text = "OBS Camera Dock "+Updates.Current.ToString(3);
                        tray.ContextMenuStrip = menu; tray.Visible = true;
                        tray.DoubleClick += delegate { Process.Start(server.Url); };
                        Application.Run(); tray.Visible = false;
                    }
                }
                return 0;
            } catch (Exception ex) {
                if (args.Contains("--headless") || args.Contains("--self-test") || args.Contains("--diagnose")) {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "OBS-Camera-Dock-error.txt"), ex.ToString());
                } else MessageBox.Show(ex.Message, "OBS Camera Dock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        static string Option(string[] args, string name) {
            int i = Array.IndexOf(args, name);
            if (i < 0) return null;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException("Chybí hodnota " + name);
            return args[i + 1];
        }
    }
    sealed class Server : IDisposable {
        public readonly string Url;
        readonly TcpListener listener;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
        readonly string preferred, root;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly HashSet<string> startupApplied = new HashSet<string>();
        Thread worker;
        volatile bool stopping, rescan;
        Exception startupError;
        Camera camera;
        Presets presets;
        FaceEngine face;
        string notice;
        public Server(string cameraName, string dataRoot, int port) {
            if(port<1||port>65535) throw new ArgumentException("Neplatný HTTP port.");
            preferred=cameraName; root=dataRoot; Url="http://127.0.0.1:"+port+"/"; listener=new TcpListener(IPAddress.Loopback,port);
        }
        public void Start() {
            listener.Start();
            worker = new Thread(Run) { IsBackground = true, Name = "Camera controls and HTTP" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            ready.WaitOne(); if (startupError != null) throw startupError;
        }
        public void Join() { worker.Join(); }
        public void RequestRescan() { rescan = true; }
        public void Dispose() { stopping = true; listener.Stop(); if (worker != null) worker.Join(6000); ready.Dispose(); }
        void ConfigurePresets() {
            // Device-specific files: DirectShow ranges and preset values differ between camera models.
            string key = "disconnected";
            if (camera.DeviceId != null) using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(camera.DeviceId))).Replace("-", "").Substring(0, 24);
            presets = new Presets(Path.Combine(root, key)); notice = null;
            if (camera.Connected && !startupApplied.Contains(key)) {
                try { presets.Startup(camera); startupApplied.Add(key); }
                catch (Exception ex) { notice = "Výchozí preset nebyl aplikován: " + ex.Message; }
            }
        }
        void Run() {
            try {
                string selection = Path.Combine(root, "selected-camera.txt");
                camera = new Camera(File.Exists(selection) && preferred == "Kiyo" ? File.ReadAllText(selection) : preferred);
                ConfigurePresets();
                face=new FaceEngine(root);
            }
            catch (Exception ex) { startupError = ex; }
            finally { ready.Set(); }
            if (startupError != null) { if (camera != null) camera.Dispose(); return; }
            try {
                while (!stopping) {
                    if (rescan) {
                        rescan = false;
                        try { face.Stop(camera); camera.Rescan(); ConfigurePresets(); } catch (Exception ex) { notice = ex.Message; }
                    }
                    face.Tick(camera);
                    if (!listener.Pending()) { Thread.Sleep(40); continue; }
                    using (var client = listener.AcceptTcpClient()) {
                        client.ReceiveTimeout = 3000; client.SendTimeout = 3000;
                        try { Handle(client.GetStream()); }
                        catch (IOException) { /* Disconnected browser or incomplete request. */ }
                        catch (SocketException) { }
                    }
                }
            } finally {
                try { face.Stop(camera); } catch { /* OBS recovery file remains available after an interrupted connection. */ }
                face.Dispose(); camera.Dispose();
            }
        }
        object State() {
            List<Dictionary<string, object>> controls;
            bool connected = camera.Connected;
            string message = notice ?? camera.Error ?? "Připojeno";
            try { controls = camera.Describe(); }
            catch (Exception ex) { controls = new List<Dictionary<string, object>>(); connected = false; message = "Kamera neodpovídá. Zkuste znovu vyhledat. " + ex.Message; }
            return new { connected = connected, cameraName = camera.Name, deviceId = camera.DeviceId,
                cameras = camera.Devices, message = message, controls = controls, presets = presets.Data, faceSupported = true };
        }
        static string Text(Dictionary<string, object> body, string key) {
            object value; return body.TryGetValue(key, out value) && value is string ? (string)value : null;
        }
        void Handle(NetworkStream stream) {
            try {
                // Bounded HTTP/1.1 framing, byte-based Content-Length, one request per connection.
                var header = new List<byte>(); int b;
                while ((b = stream.ReadByte()) >= 0) {
                    header.Add((byte)b);
                    if (header.Count > 16384) throw new ArgumentException("Hlavičky jsou příliš velké.");
                    int n = header.Count;
                    if (n >= 4 && header[n-4] == 13 && header[n-3] == 10 && header[n-2] == 13 && header[n-1] == 10) break;
                }
                if (b < 0) return;
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] {"\r\n"}, StringSplitOptions.None);
                var first = lines[0].Split(' ');
                if (first.Length != 3) throw new ArgumentException("Neplatný požadavek.");
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1).Where(x => x.Length > 0)) {
                    int colon = line.IndexOf(':');
                    if (colon < 1) throw new ArgumentException("Neplatná hlavička.");
                    headers.Add(line.Substring(0, colon), line.Substring(colon + 1).Trim());
                }
                string host;
                bool validHost = headers.TryGetValue("Host", out host) && host == new Uri(Url).Authority;
                string origin;
                bool validOrigin = !headers.TryGetValue("Origin", out origin) || origin == Url.TrimEnd('/');
                if (headers.ContainsKey("Transfer-Encoding")) throw new ArgumentException("Transfer-Encoding není podporováno.");
                int length = 0; string len;
                if (headers.TryGetValue("Content-Length", out len) && (!Int32.TryParse(len, out length) || length < 0 || length > 65536))
                    throw new ArgumentException("Neplatná délka těla.");
                byte[] bytes = new byte[length]; int read = 0;
                while (read < length) { int count = stream.Read(bytes, read, length - read); if (count == 0) return; read += count; }
                if (!validHost || !validOrigin) { Reply(stream, 403, new { error = "Nepovolený Host nebo původ požadavku." }); return; }
                string method = first[0], path = first[1].Split('?')[0];
                if (method == "GET" && (path == "/" || path == "/face")) {
                    using (var resource = typeof(Program).Assembly.GetManifestResourceStream(path == "/" ? "index.html" : "face.html"))
                    using (var memory = new MemoryStream()) { resource.CopyTo(memory); Send(stream, 200, "text/html; charset=utf-8", memory.ToArray()); }
                    return;
                }
                if (method == "GET" && path == "/api/state") { Reply(stream, 200, State()); return; }
                if (method == "GET" && path == "/api/face/state") { Reply(stream,200,face.State()); return; }
                if (method == "GET" && path == "/api/face/preview") {
                    if(face.Preview==null) Reply(stream,404,new {error="Náhled ještě není k dispozici."});
                    else Send(stream,200,"image/jpeg",face.Preview);
                    return;
                }
                if (method != "POST") { Reply(stream, 404, new { error = "Nenalezeno." }); return; }
                string type;
                if (!headers.TryGetValue("Content-Type", out type) || !type.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase)) {
                    Reply(stream, 415, new { error = "Požadováno application/json." }); return;
                }
                var body = length == 0 ? new Dictionary<string, object>() : json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
                if (body == null) throw new ArgumentException("Neplatné JSON tělo.");
                string expectedDevice = Text(body, "cameraDeviceId");
                if (expectedDevice != null && expectedDevice != camera.DeviceId)
                    throw new ArgumentException("Vybraná kamera se změnila. Obnovte stav doku.");
                switch (path) {
                    case "/api/face/connect": face.Connect(J.Int(body,"port"),J.String(body,"password"),camera); Reply(stream,200,face.State()); return;
                    case "/api/face/sources": face.ListSources(J.String(body,"scene")); Reply(stream,200,face.State()); return;
                    case "/api/face/start":
                        face.Start(json.Deserialize<FaceOptions>(json.Serialize(body)),camera); Reply(stream,200,face.State()); return;
                    case "/api/face/stop": face.Stop(camera); Reply(stream,200,face.State()); return;
                    case "/api/face/restore":
                        if(face.Running) throw new ArgumentException("Nejdřív zastavte analýzu.");
                        face.Restore(true); Reply(stream,200,face.State()); return;
                    case "/api/control":
                        object v, e; int? value = null; bool? enabled = null;
                        if (body.TryGetValue("value", out v)) { if (!(v is int)) throw new ArgumentException("value musí být celé číslo."); value = (int)v; }
                        if (body.TryGetValue("enabled", out e)) { if (!(e is bool)) throw new ArgumentException("enabled musí být boolean."); enabled = (bool)e; }
                        face.Stop(camera); camera.Apply(Text(body, "id"), value, enabled); break;
                    case "/api/rescan": face.Stop(camera); camera.Rescan(); ConfigurePresets(); break;
                    case "/api/camera":
                        string id = Text(body, "id");
                        if (id == null || !camera.Devices.Any(x => x["id"] == id)) throw new ArgumentException("Kamera není v seznamu.");
                        face.Stop(camera); camera.Select(id); ConfigurePresets();
                        File.WriteAllText(Path.Combine(root, "selected-camera.txt"), id); break;
                    case "/api/reset": face.Stop(camera); camera.Reset(); break;
                    case "/api/presets/create": case "/api/presets/save": case "/api/presets/load": case "/api/presets/delete":
                        if(path.EndsWith("/load")) face.Stop(camera);
                        presets.Action(path.Substring(path.LastIndexOf('/') + 1), Text(body, "panel"), Text(body, "id"), Text(body, "name"), camera); break;
                    default: Reply(stream, 404, new { error = "Nenalezeno." }); return;
                }
                Reply(stream, 200, State());
            } catch (Exception ex) { Reply(stream, 400, new { error = ex.Message }); }
        }
        void Reply(NetworkStream stream, int code, object value) { Send(stream, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json.Serialize(value))); }
        static void Send(NetworkStream stream, int code, string type, byte[] bytes) {
            string reason = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : code == 415 ? "Unsupported Media Type" : "Bad Request";
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
            stream.Write(header, 0, header.Length); stream.Write(bytes, 0, bytes.Length);
        }
    }
}
