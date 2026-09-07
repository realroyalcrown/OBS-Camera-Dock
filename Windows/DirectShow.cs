using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CameraDock {
    // DirectShow ABI: HRESULTs must be preserved, Windows LONG is always 32 bits.
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum {
        [PreserveSig] int CreateClassEnumerator(ref Guid category, out IEnumMoniker enumerator, int flags);
    }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, ref object value);
    }
    [ComImport, Guid("C6E13370-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMCameraControl {
        [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int def, out int caps);
        [PreserveSig] int Set(int property, int value, int flags);
        [PreserveSig] int Get(int property, out int value, out int flags);
    }
    [ComImport, Guid("C6E13360-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMVideoProcAmp {
        [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int def, out int caps);
        [PreserveSig] int Set(int property, int value, int flags);
        [PreserveSig] int Get(int property, out int value, out int flags);
    }
    interface ICamera {
        bool Connected { get; }
        string CameraName { get; }
        IEnumerable<Control> Capabilities { get; }
        List<Dictionary<string, object>> Describe();
        void Apply(string id, int? value, bool? enabled);
    }
    sealed class Camera : IDisposable, ICamera {
        object filter;
        IAMCameraControl camera;
        IAMVideoProcAmp video;
        public string Name, Error;
        string preferred;
        public string DeviceId;
        public readonly List<Dictionary<string, string>> Devices = new List<Dictionary<string, string>>();
        public readonly List<Control> Controls = new List<Control>();
        public IEnumerable<Control> Capabilities { get { return Controls; } }
        public string CameraName { get { return Name; } }
        public Camera(string preferredName) { preferred = preferredName; Rescan(); }
        public void Select(string id) { preferred = id; Rescan(); }
        public void Dispose() {
            Controls.Clear(); camera = null; video = null;
            if (filter != null) Marshal.ReleaseComObject(filter);
            filter = null; Name = null;
        }
        public void Rescan() {
            Dispose(); Error = null; DeviceId = null; Devices.Clear();
            object dev = null; IEnumMoniker enumeration = null;
            try {
                dev = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")));
                Guid category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
                int hr = ((ICreateDevEnum)dev).CreateClassEnumerator(ref category, out enumeration, 0);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (hr == 0 && enumeration != null) {
                    var monikers = new IMoniker[1];
                    while (enumeration.Next(1, monikers, IntPtr.Zero) == 0) {
                        object bag = null;
                        try {
                            Guid bagId = typeof(IPropertyBag).GUID;
                            monikers[0].BindToStorage(null, null, ref bagId, out bag);
                            object name;
                            if (((IPropertyBag)bag).Read("FriendlyName", out name, IntPtr.Zero) < 0) continue;
                            object path;
                            if (((IPropertyBag)bag).Read("DevicePath", out path, IntPtr.Zero) < 0) continue;
                            string devicePath = Convert.ToString(path);
                            Devices.Add(new Dictionary<string, string> { {"id",devicePath}, {"name",Convert.ToString(name)} });
                            bool exact = String.Equals(devicePath, preferred, StringComparison.OrdinalIgnoreCase);
                            bool match = exact || Convert.ToString(name).IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match || filter != null) continue;
                            Name = Convert.ToString(name); DeviceId = devicePath;
                            Guid iid = new Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"); // IBaseFilter
                            try { monikers[0].BindToObject(null, null, ref iid, out filter); }
                            catch (Exception ex) { Error = "Kameru " + Convert.ToString(name) + " nelze otevřít: " + ex.Message; continue; }
                            camera = filter as IAMCameraControl; video = filter as IAMVideoProcAmp;
                        } finally {
                            if (bag != null) Marshal.ReleaseComObject(bag);
                            Marshal.ReleaseComObject(monikers[0]);
                        }
                    }
                }
                if (filter == null) { Error = Error ?? "Vyberte připojenou kameru nebo ji zkuste znovu vyhledat."; return; }
                Add("exposureTime", "Expozice", true, 4, "exposureAuto");
                Add("focus", "Focus", true, 6, "focusAuto");
                Add("zoom", "Zoom", true, 3, null);
                Add("pan", "Pan", true, 0, null);
                Add("tilt", "Tilt", true, 1, null);
                Add("brightness", "Jas", false, 0, null);
                Add("contrast", "Kontrast", false, 1, null);
                Add("saturation", "Saturace", false, 3, null);
                Add("sharpness", "Ostrost", false, 4, null);
                Add("whiteBalance", "Teplota bílé", false, 7, "whiteBalanceAuto");
                Add("backlight", "Backlight", false, 8, null);
                Add("gain", "Gain / ISO", false, 9, null);
            } catch (Exception ex) { Dispose(); Error = "Kameru nelze otevřít: " + ex.Message; }
            finally {
                if (enumeration != null) Marshal.ReleaseComObject(enumeration);
                if (dev != null) Marshal.ReleaseComObject(dev);
            }
        }
        void Add(string id, string label, bool isCamera, int property, string autoId) {
            if ((isCamera && camera == null) || (!isCamera && video == null)) return;
            int min, max, step, def, caps;
            int hr = isCamera ? camera.GetRange(property, out min, out max, out step, out def, out caps)
                : video.GetRange(property, out min, out max, out step, out def, out caps);
            if (hr < 0 || max < min || (caps & 2) == 0) return;
            Controls.Add(new Control { Id = id, Label = label, IsCamera = isCamera, Property = property,
                Min = min, Max = max, Step = Math.Max(1, step), Default = def, Caps = caps,
                AutoId = (caps & 1) != 0 ? autoId : null });
        }
        public bool Connected { get { return filter != null; } }
        public int Read(Control c, out int flags) {
            int value;
            int hr = c.IsCamera ? camera.Get(c.Property, out value, out flags) : video.Get(c.Property, out value, out flags);
            Marshal.ThrowExceptionForHR(hr); return value;
        }
        public void Write(Control c, int value, int flags) {
            int hr = c.IsCamera ? camera.Set(c.Property, value, flags) : video.Set(c.Property, value, flags);
            if (hr < 0) throw new InvalidOperationException(c.Label + ": ovladač odmítl změnu (0x" + hr.ToString("X8") + ").");
        }
        public List<Dictionary<string, object>> Describe() {
            var result = new List<Dictionary<string, object>>();
            foreach (Control c in Controls) {
                int flags; int value = Read(c, out flags);
                var d = new Dictionary<string, object> {
                    {"id",c.Id}, {"label",c.Label}, {"kind","slider"}, {"value",value},
                    {"minimum",c.Min}, {"maximum",c.Max}, {"step",c.Step},
                    {"dependsOn",c.AutoId == null ? null : "!" + c.AutoId},
                    {"unit",c.Id == "whiteBalance" ? "K" : (c.Id == "pan" || c.Id == "tilt" ? "°" : null)}
                };
                if (c.Id == "exposureTime") d["encoding"] = "log2Seconds";
                // Keep the driver's native direction; focus direction varies by driver/firmware.
                if (c.Id == "focus") d["focusRawScale"] = true;
                result.Add(d);
                if (c.AutoId != null) result.Add(new Dictionary<string, object> {
                    {"id",c.AutoId}, {"label",c.Label + " AUTO"}, {"kind","toggle"}, {"enabled",(flags & 1) != 0}
                });
            }
            return result;
        }
        public void Apply(string id, int? value, bool? enabled) {
            if (!Connected) throw new InvalidOperationException("Kamera není připojena.");
            if (String.IsNullOrEmpty(id)) throw new ArgumentException("Chybí id ovladače.");
            var c = Controls.Find(x => x.Id == id || x.AutoId == id);
            if (c == null) throw new ArgumentException("Nepodporovaný ovladač: " + id);
            if (id == c.AutoId) {
                if (!enabled.HasValue) throw new ArgumentException("Chybí enabled.");
                int flags; int current = Read(c, out flags);
                Write(c, current, enabled.Value ? 1 : 2);
            } else {
                if (!value.HasValue) throw new ArgumentException("Chybí value.");
                Write(c, c.Snap(value.Value), 2);
            }
        }
        public void Reset() {
            if (!Connected) throw new InvalidOperationException("Kamera není připojena.");
            foreach (var c in Controls) Write(c, c.Default, (c.Caps & 1) != 0 ? 1 : 2);
        }
    }
    sealed class Control {
        public string Id, Label, AutoId;
        public bool IsCamera;
        public int Property, Min, Max, Step, Default, Caps;
        public int Snap(double value) {
            return (int)Math.Max(Min, Math.Min(Max, Min + Math.Round((value - Min) / Step, MidpointRounding.AwayFromZero) * Step));
        }
    }
}
