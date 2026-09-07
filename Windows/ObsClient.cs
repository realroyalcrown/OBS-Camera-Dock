using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CameraDock {
    interface IObsClient : IDisposable {
        bool Connected { get; }
        Dictionary<string, object> Call(string type, object data);
    }
    sealed class ObsClient : IObsClient {
        readonly ClientWebSocket socket = new ClientWebSocket();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public bool Connected { get { return socket.State == WebSocketState.Open; } }
        public ObsClient(int port, string password) {
            if (port < 1 || port > 65535) throw new ArgumentException("Neplatný port OBS.");
            try {
                using (var timeout = new CancellationTokenSource(3000))
                    socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), timeout.Token).GetAwaiter().GetResult();
                var hello = Receive();
                if (J.Int(hello, "op") != 0) throw new IOException("OBS neposlal uvítání WebSocket 5.x.");
                var data = J.Map(hello, "d");
                var identify = new Dictionary<string, object> { {"rpcVersion",1}, {"eventSubscriptions",0} };
                if (data.ContainsKey("authentication")) {
                    var auth = J.Map(data, "authentication");
                    identify["authentication"] = Authentication(password ?? "", J.String(auth, "salt"), J.String(auth, "challenge"));
                }
                Send(new { op = 1, d = identify });
                if (J.Int(Receive(), "op") != 2) throw new IOException("OBS nepotvrdil připojení.");
            } catch { socket.Dispose(); throw; }
        }
        internal static string Authentication(string password, string salt, string challenge) {
            using (var sha = SHA256.Create()) {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }
        void Send(object data) {
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(data));
            using (var timeout = new CancellationTokenSource(3000))
                socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
        }
        Dictionary<string, object> Receive() {
            using (var timeout = new CancellationTokenSource(3000))
            using (var memory = new MemoryStream()) {
                byte[] buffer = new byte[16384];
                WebSocketReceiveResult result;
                do {
                    result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new IOException("OBS ukončilo spojení. Ověřte heslo a zapnutý WebSocket server.");
                    if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Neočekávaný formát zprávy OBS.");
                    memory.Write(buffer, 0, result.Count);
                    if (memory.Length > 8 * 1024 * 1024) throw new IOException("Zpráva OBS je příliš velká.");
                } while (!result.EndOfMessage);
                return json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(memory.ToArray()));
            }
        }
        public Dictionary<string, object> Call(string type, object data) {
            string id = Guid.NewGuid().ToString("N");
            Send(new { op = 6, d = new { requestType = type, requestId = id, requestData = data ?? new object() } });
            var reply = Receive();
            if (J.Int(reply, "op") != 7) throw new IOException("Neočekávaná odpověď OBS.");
            var payload = J.Map(reply, "d");
            if (J.String(payload, "requestId") != id) throw new IOException("Nesouhlasí ID odpovědi OBS.");
            var status = J.Map(payload, "requestStatus");
            if (!J.Bool(status, "result")) throw new InvalidOperationException(type + ": " + J.String(status, "comment"));
            return payload.ContainsKey("responseData") ? J.Map(payload, "responseData") : new Dictionary<string, object>();
        }
        public void Dispose() { socket.Dispose(); }
    }
    static class J {
        public static Dictionary<string, object> Map(Dictionary<string, object> d, string key) { return (Dictionary<string, object>)d[key]; }
        public static string String(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? Convert.ToString(v) : ""; }
        public static double Number(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? Convert.ToDouble(v) : 0; }
        public static int Int(Dictionary<string, object> d, string key) { return checked((int)Number(d,key)); }
        public static bool Bool(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key,out v) && v is bool && (bool)v; }
        public static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> d, string key) {
            foreach (object item in (IEnumerable)d[key]) yield return (Dictionary<string, object>)item;
        }
    }
}
