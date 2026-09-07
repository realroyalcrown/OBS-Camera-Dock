using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CameraDock {
    public sealed class FaceOptions {
        public string scene;
        public int itemId;
        public bool exposure, focus, tracking;
        public double target = 140, maxZoom = 1.6;
    }
    public sealed class TrackingRecovery {
        public string scene, source;
        public int itemId;
        public Dictionary<string, object> original, last;
    }
    sealed class FaceEngine : IDisposable {
        IObsClient obs;
        IObsClient motionObs;
        SmoothTracking motion;
        readonly string recoveryPath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly FaceDetection detector = new FaceDetection();
        readonly Dictionary<string,PresetValue> originalControls = new Dictionary<string,PresetValue>();
        TrackingRecovery recovery;
        FaceOptions options = new FaceOptions();
        List<Dictionary<string,object>> scenes = new List<Dictionary<string,object>>(), sources = new List<Dictionary<string,object>>();
        string scene = "", source = "", deviceId;
        bool running;
        string message = "Připojte OBS a vyberte zdroj kamery.", error;
        DateTime nextFrame, nextExposure, lastSeen, focusDue, nextValidation;
        FaceSample sample = new FaceSample(), previous;
        byte[] preview;
        int stableFrames;
        double focusReference;
        FocusSearch search;
        bool initialFocus;
        public FaceEngine(string root) {
            recoveryPath=Path.Combine(root,"face-tracking-recovery.json");
            if(File.Exists(recoveryPath)) {
                recovery=json.Deserialize<TrackingRecovery>(File.ReadAllText(recoveryPath));
                message="Z předchozího běhu zůstal výřez. Připojte OBS a obnovte původní záběr.";
            }
        }
        public bool Running { get { return running; } }
        public byte[] Preview { get { return preview; } }
        public object State() {
            return new { obsConnected=obs!=null&&obs.Connected, running=running, message=message, error=error,
                scenes=scenes, sources=sources, scene=scene, source=source, options=options, face=sample,
                focusSearching=search!=null, recoveryPending=recovery!=null&&!running,
                controlsRecoveryPending=originalControls.Count>0&&!running,
                trackingTransform=motion!=null?motion.LastTransform:(recovery==null?null:recovery.last),
                trackingUpdates=motion==null?0:motion.Updates, trackingTargetFps=30 };
        }
        public void Connect(int port,string password,Camera camera) {
            if(running || originalControls.Count>0) Stop(camera);
            if(obs!=null) obs.Dispose(); obs=null;
            if(motionObs!=null) motionObs.Dispose(); motionObs=null;
            obs=new ObsClient(port,password); error=null;
            try { motionObs=new ObsClient(port,password); }
            catch { obs.Dispose();obs=null;throw; }
            var result=obs.Call("GetSceneList",null);
            scenes=J.Items(result,"scenes").Select(x=>new Dictionary<string,object>{{"name",J.String(x,"sceneName")}}).ToList();
            scene=J.String(result,"currentProgramSceneName");
            ListSources(scene);
            message="OBS připojeno. Vyberte správný zdroj a požadované funkce.";
        }
        public void ListSources(string selectedScene) {
            RequireObs(); if(running) throw new InvalidOperationException("Nejdřív zastavte analýzu.");
            if(!scenes.Any(x=>J.String(x,"name")==selectedScene)) throw new ArgumentException("Scéna není v seznamu OBS.");
            scene=selectedScene;
            sources=J.Items(obs.Call("GetSceneItemList",new {sceneName=scene}),"sceneItems")
                .Where(x=>J.String(x,"inputKind")=="dshow_input")
                .Select(x=>new Dictionary<string,object>{{"id",J.Int(x,"sceneItemId")},{"name",J.String(x,"sourceName")},{"enabled",J.Bool(x,"sceneItemEnabled")}}).ToList();
        }
        void RequireObs() { if(obs==null || !obs.Connected) throw new InvalidOperationException("Připojte OBS WebSocket 5.x."); }
        public void Start(FaceOptions value, Camera camera) {
            RequireObs();
            if(value==null || !DoubleCompat.IsFinite(value.target) || value.target<90 || value.target>180 || !DoubleCompat.IsFinite(value.maxZoom) || value.maxZoom<1 || value.maxZoom>3)
                throw new ArgumentException("Jas musí být 90–180 a maximální zoom 1–3×.");
            if(value.scene!=scene) throw new ArgumentException("Načtěte seznam zdrojů vybrané scény.");
            var item=sources.Find(x=>J.Int(x,"id")==value.itemId);
            if(item==null) throw new ArgumentException("Vyberte zdroj kamery ve scéně.");
            var live=J.Items(obs.Call("GetSceneItemList",new{sceneName=scene}),"sceneItems").FirstOrDefault(x=>J.Int(x,"sceneItemId")==value.itemId);
            if(live==null||J.String(live,"sourceName")!=J.String(item,"name")) throw new ArgumentException("Zdroj ve scéně se změnil. Načtěte jeho seznam znovu.");
            if(!J.Bool(live,"sceneItemEnabled")) throw new ArgumentException("Zdroj je v OBS skrytý. Nejprve ho zapněte.");
            Stop(camera);
            if(recovery!=null) throw new InvalidOperationException("Nejdřív obnovte záběr z předchozího běhu.");
            source=J.String(item,"name");
            if(value.exposure || value.focus) {
                if(!camera.Connected) throw new InvalidOperationException("Pro expozici a focus musí být kamera připojená v hlavním doku.");
                string obsDevice=J.String(J.Map(obs.Call("GetInputSettings",new {inputName=source}),"inputSettings"),"video_device_id");
                if(!ObsDevice.Matches(obsDevice,camera.DeviceId))
                    throw new InvalidOperationException("Zdroj OBS neodpovídá kameře vybrané v hlavním doku. Vyberte stejnou kameru.");
                if(value.exposure && !camera.Controls.Any(c=>c.Id=="exposureTime" || c.Id=="gain")) throw new InvalidOperationException("Kamera nepodporuje ruční expozici ani gain.");
                if(value.focus && !camera.Controls.Any(c=>c.Id=="focus" && c.Max>c.Min)) throw new InvalidOperationException("Kamera nepodporuje ruční ostření.");
            }
            detector.Initialize();
            options=value; deviceId=camera.DeviceId; error=null; preview=null; sample=new FaceSample(); previous=null;
            stableFrames=0; search=null; initialFocus=true; focusReference=0;
            nextFrame=nextExposure=focusDue=nextValidation=DateTime.MinValue; lastSeen=DateTime.UtcNow;
            originalControls.Clear();
            foreach(var d in (value.exposure||value.focus ? camera.Describe() : new List<Dictionary<string,object>>())) {
                string id=J.String(d,"id");
                if((value.exposure && new[]{"exposureTime","exposureAuto","gain"}.Contains(id)) || (value.focus && new[]{"focus","focusAuto"}.Contains(id)))
                    originalControls[id]=J.String(d,"kind")=="toggle" ? new PresetValue{enabled=J.Bool(d,"enabled")} : new PresetValue{value=J.Int(d,"value")};
            }
            if(value.tracking) {
                if(motionObs==null||!motionObs.Connected) throw new InvalidOperationException("Připojte OBS znovu pro plynulé trackování.");
                var transform=GetTransform(scene,value.itemId);
                FaceMath.Crop(transform,.5,.5,1); // Validate geometry before saving or writing anything.
                recovery=new TrackingRecovery{scene=scene,source=source,itemId=value.itemId,original=transform,last=transform};
                SaveRecovery();
                motion=new SmoothTracking(motionObs,recovery);
            }
            running=true; message="Hledám obličej…";
            try {
                if(value.exposure) {
                    foreach(string id in new[]{"exposureTime","gain"}) {
                        var c=camera.Controls.Find(x=>x.Id==id);
                        if(c!=null) { int flags; camera.Apply(id,camera.Read(c,out flags),null); }
                    }
                }
                if(motion!=null) motion.Start();
            } catch { Stop(camera); throw; }
        }
        void SaveRecovery() {
            Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath));
            string temp=recoveryPath+".tmp"; File.WriteAllText(temp,json.Serialize(recovery));
            if(File.Exists(recoveryPath)) File.Replace(temp,recoveryPath,null); else File.Move(temp,recoveryPath);
        }
        Dictionary<string,object> GetTransform(string s,int id) { return J.Map(obs.Call("GetSceneItemTransform",new{sceneName=s,sceneItemId=id}),"sceneItemTransform"); }
        void SetTransform(string s,int id,Dictionary<string,object> t) { obs.Call("SetSceneItemTransform",new{sceneName=s,sceneItemId=id,sceneItemTransform=t}); }
        public void Restore(bool explicitRequest) {
            if(recovery==null) return; RequireObs();
            var item=J.Items(obs.Call("GetSceneItemList",new{sceneName=recovery.scene}),"sceneItems").FirstOrDefault(x=>J.Int(x,"sceneItemId")==recovery.itemId);
            string actual=item==null ? "" : J.String(item,"sourceName");
            if(actual!=recovery.source) throw new InvalidOperationException("Původní zdroj OBS se změnil; automatické obnovení je zastavené.");
            if(!explicitRequest && !FaceMath.SameTransform(GetTransform(recovery.scene,recovery.itemId),recovery.last))
                throw new InvalidOperationException("Záběr byl upraven v OBS. Automaticky ho nepřepíšu; můžete zvolit Obnovit původní záběr.");
            SetTransform(recovery.scene,recovery.itemId,FaceMath.Writable(recovery.original));
            File.Delete(recoveryPath); recovery=null;
            error=null; message="Původní záběr obnoven.";
        }
        public void Stop(Camera camera) {
            if(!running && originalControls.Count==0 && recovery==null) return;
            running=false; search=null; preview=null; sample=new FaceSample();
            if(motion!=null) { motion.Dispose();motion=null; }
            var failures=new List<string>();
            if(originalControls.Count>0 && camera.DeviceId==deviceId) {
                try { Presets.Apply(camera,originalControls); originalControls.Clear(); }
                catch(Exception ex) { failures.Add("Obnova kamery: "+ex.Message); }
            }
            if(recovery!=null) { try { Restore(false); } catch(Exception ex) { failures.Add(ex.Message); } }
            message="Analýza zastavena; původní nastavení obnoveno.";
            if(failures.Count>0) { error=String.Join(" ",failures); message="Analýza zastavena. Obnovení vyžaduje pozornost."; throw new InvalidOperationException(error); }
        }
        public void Tick(Camera camera) {
            if(!running) return;
            if(motion!=null&&motion.Error!=null) {
                string reason=motion.Error;
                try { Stop(camera); } catch(Exception ex) { reason+=" "+ex.Message; }
                error=reason;message="Analýza pozastavena: "+reason;return;
            }
            if(DateTime.UtcNow<nextFrame) return;
            nextFrame=DateTime.UtcNow.AddMilliseconds(250);
            try {
                if((options.exposure||options.focus) && camera.DeviceId!=deviceId) throw new InvalidOperationException("Vybraná kamera se změnila.");
                if(DateTime.UtcNow>=nextValidation) {
                    nextValidation=DateTime.UtcNow.AddSeconds(1);
                    var item=J.Items(obs.Call("GetSceneItemList",new{sceneName=scene}),"sceneItems").FirstOrDefault(x=>J.Int(x,"sceneItemId")==options.itemId);
                    if(item==null||J.String(item,"sourceName")!=source||!J.Bool(item,"sceneItemEnabled")) throw new InvalidOperationException("Zdroj kamery ve scéně se změnil nebo byl skrytý.");
                    if(options.exposure||options.focus) {
                        string activeDevice=J.String(J.Map(obs.Call("GetInputSettings",new{inputName=source}),"inputSettings"),"video_device_id");
                        if(!ObsDevice.Matches(activeDevice,deviceId)) throw new InvalidOperationException("V OBS byla vybrána jiná USB kamera. Řízení původní kamery bylo zastaveno.");
                    }
                }
                string image=J.String(obs.Call("GetSourceScreenshot",new{sourceName=source,imageFormat="jpg",imageWidth=640,imageCompressionQuality=85}),"imageData");
                const string prefix="data:image/jpg;base64,";
                int comma=image.IndexOf(',');
                if(comma<0 || !(image.StartsWith(prefix) || image.StartsWith("data:image/jpeg;base64,"))) throw new IOException("OBS nevrátilo JPEG náhled.");
                preview=Convert.FromBase64String(image.Substring(comma+1));
                sample=detector.Analyze(preview,search!=null&&DateTime.UtcNow-lastSeen<TimeSpan.FromSeconds(1.5)?previous:null);
                if(!sample.found) {
                    stableFrames=0; message="Obličej nenalezen — expozice a focus pozastaveny.";
                    // A focus step may briefly blur the detector. Measure only the last known face ROI
                    // for at most 1.5 s; never feed this inferred region into exposure or tracking.
                    if(search!=null&&sample.focusRegion&&DateTime.UtcNow-lastSeen<TimeSpan.FromSeconds(1.5)) {
                        Focus(camera); message="Ostřím v poslední oblasti obličeje…"; return;
                    }
                    if(search!=null && DateTime.UtcNow-lastSeen>TimeSpan.FromSeconds(1)) {
                        camera.Apply("focus",search.Best,null); search=null; focusDue=DateTime.UtcNow.AddSeconds(2);
                    }
                    return;
                }
                bool stable=previous==null || (FaceMath.Distance(previous,sample)<.06 && Math.Abs(previous.height-sample.height)<.08);
                stableFrames=stable ? stableFrames+1 : 0; previous=sample; lastSeen=DateTime.UtcNow;
                message="Obličej nalezen"+(sample.count>1 ? " — sleduji jeden obličej." : ".");
                if(motion!=null) motion.Observe(sample,options.maxZoom);
                if(!stable && search!=null) { camera.Apply("focus",search.Best,null); search=null; focusDue=DateTime.UtcNow.AddSeconds(2); }
                if(stableFrames<3 || sample.brightness<15 || sample.brightness>240) return;
                if(options.focus) Focus(camera);
                if(options.exposure && search==null && DateTime.UtcNow>=nextExposure) Exposure(camera);
            } catch(Exception ex) {
                string cause=ex.Message;
                try { Stop(camera); } catch(Exception restore) { cause+=" "+restore.Message; }
                error=cause; message="Analýza pozastavena: "+cause;
            }
        }
        void Exposure(Camera camera) {
            nextExposure=DateTime.UtcNow.AddSeconds(1);
            var exposure=camera.Controls.Find(c=>c.Id=="exposureTime");
            var gain=camera.Controls.Find(c=>c.Id=="gain");
            int direction=FaceMath.ExposureDirection(sample.brightness,options.target), flags;
            if(exposure!=null && direction!=0) {
                int current=camera.Read(exposure,out flags);
                // Avoid long shutter times and motion blur in a live video call/stream.
                int next=exposure.Snap(current+direction*exposure.Step);
                int ceiling=exposure.Snap(Math.Min(-5,exposure.Max));
                if(next!=current && (direction<0 || next<=ceiling)) { camera.Apply(exposure.Id,next,null); return; }
            }
            if(gain!=null && Math.Abs(sample.brightness-options.target)>12) {
                int current=camera.Read(gain,out flags);
                int next=gain.Snap(current+Math.Sign(options.target-sample.brightness)*Math.Max(gain.Step,(gain.Max-gain.Min)*.025));
                if(next!=current) camera.Apply(gain.Id,next,null);
            }
        }
        void Focus(Camera camera) {
            var c=camera.Controls.Find(x=>x.Id=="focus"); int flags;
            if(search==null && DateTime.UtcNow>=focusDue && (initialFocus || sample.sharpness<focusReference*.65)) {
                search=new FocusSearch(c,camera.Read(c,out flags),initialFocus);
                camera.Apply("focus",search.Next(),null); focusDue=DateTime.UtcNow.AddMilliseconds(550);
            } else if(search!=null && DateTime.UtcNow>=focusDue) {
                search.Observe(sample.sharpness);
                if(search.Done) {
                    camera.Apply("focus",search.Best,null); focusReference=search.BestScore;
                    search=null; initialFocus=false; focusDue=DateTime.UtcNow.AddSeconds(8);
                } else { camera.Apply("focus",search.Next(),null); focusDue=DateTime.UtcNow.AddMilliseconds(550); }
            }
            if(search!=null) message="Ostřím na obličej — měřím kontrast…";
        }
        public void Dispose() { if(motion!=null) motion.Dispose();if(motionObs!=null) motionObs.Dispose();if(obs!=null) obs.Dispose(); }
    }
    sealed class FocusSearch {
        readonly Control control;
        readonly Queue<int> candidates=new Queue<int>();
        int current;
        bool refined;
        public int Best { get; private set; }
        public double BestScore { get; private set; }
        public bool Done { get { return candidates.Count==0&&refined; } }
        public FocusSearch(Control c,int start,bool full) {
            control=c; Best=start; BestScore=-1;
            var values=new List<int>{start};
            if(full) for(int i=0;i<=8;i++) values.Add(c.Snap(c.Min+(c.Max-c.Min)*i/8.0));
            else for(int i=-2;i<=2;i++) values.Add(c.Snap(start+i*Math.Max(c.Step,(c.Max-c.Min)/32.0)));
            foreach(int v in values.Distinct().OrderBy(v=>Math.Abs(v-start))) candidates.Enqueue(v);
            refined=!full;
        }
        public int Next() { current=candidates.Dequeue(); return current; }
        public void Observe(double score) {
            if(score>BestScore) { BestScore=score; Best=current; }
            if(candidates.Count==0&&!refined) {
                refined=true;
                var values=new List<int>();
                for(int i=-2;i<=2;i++) values.Add(control.Snap(Best+i*Math.Max(control.Step,(control.Max-control.Min)/32.0)));
                foreach(int v in values.Distinct()) candidates.Enqueue(v);
            }
        }
    }
    static class DoubleCompat {
        public static bool IsFinite(double value) { return !Double.IsNaN(value)&&!Double.IsInfinity(value); }
    }
}
