using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CameraDock {
    public sealed class PresetValue { public int? value; public bool? enabled; }
    public sealed class Preset { public string id; public string name; public Dictionary<string, PresetValue> values; }
    public sealed class Panel { public List<Preset> items = new List<Preset>(); public string selectedId; }
    public sealed class Library {
        public Panel expo = new Panel(), obraz = new Panel(), optika = new Panel();
        public string startupPresetId;
    }
    sealed class Presets {
        public Library Data = new Library();
        readonly string path;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        static readonly Dictionary<string, string[]> ids = new Dictionary<string, string[]> {
            {"expo", new[] {"exposureTime","exposureAuto","focus","focusAuto","gain","brightness"}},
            {"obraz", new[] {"contrast","saturation","sharpness","whiteBalance","whiteBalanceAuto"}},
            {"optika", new[] {"zoom","pan","tilt","backlight"}}
        };
        public Presets(string root) {
            Directory.CreateDirectory(root); path = Path.Combine(root, "presets-windows.json");
            if (File.Exists(path)) {
                Data = json.Deserialize<Library>(File.ReadAllText(path));
                if (Data == null || Data.expo == null || Data.obraz == null || Data.optika == null)
                    throw new InvalidDataException("Neplatný soubor presetů: " + path);
            }
        }
        public Panel GetPanel(string panel) {
            switch (panel) { case "expo": return Data.expo; case "obraz": return Data.obraz; case "optika": return Data.optika; }
            throw new ArgumentException("Neznámý panel.");
        }
        void Save() {
            string temp = path + ".tmp";
            File.WriteAllText(temp, json.Serialize(Data));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
        public Dictionary<string, PresetValue> Snapshot(ICamera camera, string panel) {
            GetPanel(panel);
            if (!camera.Connected) throw new InvalidOperationException("Kamera není připojena.");
            var values = new Dictionary<string, PresetValue>();
            foreach (var d in camera.Describe().Where(d => ids[panel].Contains((string)d["id"]))) {
                values[(string)d["id"]] = (string)d["kind"] == "toggle"
                    ? new PresetValue { enabled = (bool)d["enabled"] } : new PresetValue { value = (int)d["value"] };
            }
            return values;
        }
        public static void Apply(ICamera camera, Dictionary<string, PresetValue> values) {
            // Manual values first; restore AUTO last, otherwise a numeric Set disables AUTO again.
            foreach (var pair in values.Where(p => p.Value.value.HasValue)) camera.Apply(pair.Key, pair.Value.value, null);
            foreach (var pair in values.Where(p => p.Value.enabled.HasValue)) camera.Apply(pair.Key, null, pair.Value.enabled);
        }
        public void Action(string action, string panelName, string id, string name, ICamera camera) {
            Panel panel = GetPanel(panelName);
            if (action == "create") {
                if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Zadejte název presetu.");
                var p = new Preset { id = Guid.NewGuid().ToString(), name = name.Trim(), values = Snapshot(camera, panelName) };
                panel.items.Add(p); panel.selectedId = p.id;
            } else {
                if (action == "load") {
                    panel = new[] { Data.expo, Data.obraz, Data.optika }.FirstOrDefault(x => x.items.Any(item => item.id == id));
                    if (panel == null) throw new ArgumentException("Preset neexistuje.");
                }
                var p = panel.items.Find(x => x.id == id);
                if (p == null) throw new ArgumentException("Preset neexistuje.");
                switch (action) {
                    case "save": p.values = Snapshot(camera, panelName); panel.selectedId = id; break;
                    case "load": Apply(camera, p.values); panel.selectedId = id; break;
                    case "delete": panel.items.Remove(p); if (panel.selectedId == id) panel.selectedId = null; break;
                    default: throw new ArgumentException("Neznámá operace.");
                }
            }
            Save();
        }
        public void Startup(ICamera camera) {
            if (!camera.Connected) return;
            var values = new Dictionary<string, PresetValue>();
            foreach (var c in camera.Capabilities) {
                double value;
                switch (c.Id) {
                    case "exposureTime": value = Math.Log(1.0 / 60, 2); break;
                    case "gain": value = c.Min + (c.Max - c.Min) * 0.5; break;
                    case "brightness": value = c.Min + (c.Max - c.Min) * 0.48; break;
                    case "focus": value = c.Min + (c.Max - c.Min) * 0.68; break;
                    case "whiteBalance": value = 4200; break;
                    default: continue;
                }
                values[c.Id] = new PresetValue { value = c.Snap(value) };
                if (c.AutoId != null) values[c.AutoId] = new PresetValue { enabled = false };
            }
            // Preserve the original Kiyo startup behavior; other models keep their current settings.
            if (camera.CameraName.IndexOf("Kiyo", StringComparison.OrdinalIgnoreCase) < 0 || values.Count == 0) return;
            var existing = Data.expo.items.Find(p => p.id == "rrc_base");
            if (existing == null) { existing = new Preset { id = "rrc_base", name = "rrc_base" }; Data.expo.items.Insert(0, existing); }
            existing.values = values;
            Apply(camera, values); Data.expo.selectedId = existing.id; Data.startupPresetId = existing.id; Save();
        }
    }
}
