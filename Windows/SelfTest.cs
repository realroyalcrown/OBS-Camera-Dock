using System;
using System.Collections.Generic;
using System.IO;
namespace CameraDock {
    static class SelfTest {
        public static void Run(string root) {
            string release="[{\"draft\":false,\"assets\":[{\"name\":\"OBS-Camera-Dock-Windows-0.5.0-preview.zip\",\"browser_download_url\":\"https://github.com/realroyalcrown/OBS-Camera-Dock/releases/download/v0.5.0/file.zip\"}]}]";
            Check(Updates.Select(release,new Version(0,4,3,0)).Version==new Version(0,5,0,0),"new Windows preview release selected");
            Check(Updates.Select(release,new Version(0,5,0,0))==null,"same version is not an update");
            Check(Updates.Select(release.Replace("Windows-","macOS-"),new Version(0,4,3,0))==null,"macOS asset excluded");
            Check(Updates.Select(release.Replace("false","true"),new Version(0,4,3,0))==null,"draft excluded");
            Check(Updates.Select(release.Replace("https://github.com/","https://example.com/"),new Version(0,4,3,0))==null,"foreign download rejected");
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("camera.ico")) using(var icon=new System.Drawing.Icon(stream)) Check(icon.Width>0,"embedded tray icon loads");
            FaceTests.Run(root);
            var exposure = new Control { Min = -11, Max = -1, Step = 1 };
            Check(exposure.Snap(Math.Log(1.0 / 60, 2)) == -6, "1/60 rounds to supported 1/64");
            var offset = new Control { Min = 3, Max = 23, Step = 5 };
            Check(offset.Snap(10) == 8, "steps start at minimum");
            Check(offset.Snap(-100) == 3 && offset.Snap(100) == 23, "clamping");
            var camera = new FakeCamera();
            string directory = Path.Combine(root, "self-test-" + Guid.NewGuid().ToString("N"));
            var store = new Presets(directory);
            store.Action("create", "expo", null, "Test česky", camera);
            string id = store.Data.expo.selectedId;
            store = new Presets(directory);
            Check(store.Data.expo.items.Count == 1 && store.Data.expo.items[0].name == "Test česky", "preset persistence and Unicode");
            store.Action("load", "obraz", id, null, camera);
            Check(camera.Calls[0] == "exposureTime" && camera.Calls[1] == "exposureAuto", "AUTO must be restored after numeric writes");
            store.Action("save", "expo", id, null, camera);
            store.Action("delete", "expo", id, null, camera);
            Check(new Presets(directory).Data.expo.items.Count == 0, "preset deletion persistence");
            bool rejected = false;
            try { store.GetPanel("invalid"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "unknown panel validation");
            camera.Fail = true; rejected = false;
            try { Presets.Apply(camera, new Dictionary<string, PresetValue> { {"exposureTime", new PresetValue {value = -6}} }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "hardware failures must reach API caller");
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Test failed: " + message); }
        sealed class FakeCamera : ICamera {
            public bool Connected { get { return true; } }
            public string CameraName { get { return "Test camera"; } }
            public IEnumerable<Control> Capabilities { get { return new Control[0]; } }
            public List<string> Calls = new List<string>();
            public bool Fail;
            public List<Dictionary<string, object>> Describe() {
                return new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { {"id","exposureTime"}, {"kind","slider"}, {"value",-6} },
                    new Dictionary<string, object> { {"id","exposureAuto"}, {"kind","toggle"}, {"enabled",true} }
                };
            }
            public void Apply(string id, int? value, bool? enabled) {
                if (Fail) throw new InvalidOperationException("Simulated device failure");
                Calls.Add(id);
            }
        }
    }
}
