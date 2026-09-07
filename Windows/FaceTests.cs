using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Web.Script.Serialization;

namespace CameraDock {
    static class FaceTests {
        static void Check(bool value,string message) { if(!value) throw new Exception("Face test: "+message); }
        public static void Run(string root) {
            string path=@"\\?\usb#vid_1532&pid_0e0c&mi_00#9&6638c63&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";
            string encoded="Razer Kiyo V2 X:"+path.Replace("#","#22");
            Check(ObsDevice.Matches(encoded,path.ToUpperInvariant()),"OBS escaped UVC path matches DirectShow ignoring case");
            Check(!ObsDevice.Matches(encoded,path.Replace("6638c63","6638c64")),"same model at different device instance rejected");
            Check(!ObsDevice.Matches(encoded,path+"extra"),"partial device path rejected");
            Check(!ObsDevice.Matches("Camera:",path)&&!ObsDevice.Matches(null,path)&&!ObsDevice.Matches(encoded,null),"missing device identity rejected");
            Check(ObsDevice.Matches("Name#3Awith#22escapes:device#3Apath#223A","device:path#3A"),"OBS decoding order and encoded name separator");
            TestMotion(root);
            // Expected digest independently verified with Node's crypto implementation.
            Check(ObsClient.Authentication("supersecretpassword","lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=","+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=")=="1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=","OBS authentication protocol fixture");
            var original=new Dictionary<string,object>{{"sourceWidth",1920},{"sourceHeight",1080},{"cropLeft",100},{"cropRight",100},{"cropTop",40},{"cropBottom",40},{"scaleX",.5},{"scaleY",.5},{"positionX",100},{"positionY",200}};
            var crop=FaceMath.Crop(original,.5,.5,2);
            Check(J.Int(crop,"cropLeft")==530&&J.Int(crop,"cropTop")==290,"center crop includes existing margins");
            Check(Math.Abs((1920-J.Int(crop,"cropLeft")-J.Int(crop,"cropRight"))*J.Number(crop,"scaleX")-860)<.01,"tracking preserves visible width");
            foreach(double x in new[]{-.5,0,.5,1,1.5}) foreach(double y in new[]{0,.5,1}) {
                crop=FaceMath.Crop(original,x,y,3);
                Check(J.Int(crop,"cropLeft")>=100&&J.Int(crop,"cropRight")>=100&&J.Int(crop,"cropTop")>=40&&J.Int(crop,"cropBottom")>=40,"crop stays within source");
            }
            crop=FaceMath.Crop(original,.5,.5,1);
            Check(J.Int(crop,"cropLeft")==100&&J.Int(crop,"cropRight")==100,"zoom one restores full original region");
            var edited=new Dictionary<string,object>(original);edited["positionX"]=101;
            Check(!FaceMath.SameTransform(original,edited),"external OBS edit must stop tracking");
            var noBounds=new Dictionary<string,object>(original){{"boundsWidth",0},{"boundsHeight",0},{"boundsType","OBS_BOUNDS_NONE"}};
            Check(!FaceMath.Writable(noBounds).ContainsKey("boundsWidth")&&!FaceMath.Writable(noBounds).ContainsKey("boundsHeight"),"OBS rejects zero-sized unused bounds on restore");
            Check(FaceMath.ExposureDirection(0,140)==0,"black frame never increases exposure");
            Check(FaceMath.ExposureDirection(60,140)==1&&FaceMath.ExposureDirection(240,140)==-1,"exposure correction direction");
            Check(FaceMath.ExposureDirection(135,140)==0,"exposure deadband");
            var control=new Control{Min=3,Max=203,Step=5};
            var search=new FocusSearch(control,3,true);int samples=0;
            while(!search.Done) {
                int v=search.Next();Check(v>=3&&v<=203&&(v-3)%5==0,"focus candidate respects range and step");
                search.Observe(10000-Math.Pow(v-103,2));
                if(++samples>20) throw new Exception("Unbounded focus scan");
            }
            Check(Math.Abs(search.Best-103)<=5,"focus scan converges on sharpness maximum");
            var gray=new byte[64*64];for(int i=0;i<gray.Length;i++)gray[i]=140;
            var face=new FaceSample{x=.1,y=.1,width=.8,height=.8};FaceDetection.Measure(gray,64,64,face);
            Check(face.brightness==140&&face.sharpness==0,"uniform face ROI luminance and sharpness");
            // Actual Windows detector runtime smoke test, no camera and no downloaded model.
            using(var bitmap=new Bitmap(128,128)) using(var memory=new MemoryStream()) {
                using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Gray);
                bitmap.Save(memory,ImageFormat.Jpeg);
                Check(!new FaceDetection().Analyze(memory.ToArray()).found,"native detector rejects blank frame");
            }
        }
        static void TestMotion(string root) {
            var a=new MotionAxis(0,.45,10);var b=new MotionAxis(0,.45,10);
            for(int i=0;i<30;i++)a.Step(1,1.0/30);
            for(int i=0;i<60;i++)b.Step(1,1.0/60);
            Check(Math.Abs(a.Value-b.Value)<1e-9,"smoothing must not depend on frame rate");
            var limited=new MotionAxis(0,.45,.45);double last=0;
            for(int i=0;i<90;i++){limited.Step(1,1.0/30);Check(limited.Value>=last&&limited.Value<=1&&limited.Value-last<=.45/30+1e-9,"motion speed bound and no overshoot");last=limited.Value;}
            var baseline=new Dictionary<string,object>{{"sourceWidth",1920},{"sourceHeight",1080},{"cropLeft",0},{"cropRight",0},{"cropTop",0},{"cropBottom",0},{"scaleX",1.0},{"scaleY",1.0},{"positionX",0},{"positionY",0}};
            var fake=new MotionObs(baseline);
            var recovery=new TrackingRecovery{scene="Test",source="Camera",itemId=7,original=baseline,last=baseline};
            using(var tracking=new SmoothTracking(fake,recovery)) {
                tracking.Observe(new FaceSample{found=true,x=.66,y=.24,width=.1,height=.12},1.8);
                tracking.Start();
                // No new detections, and the caller is blocked for over a second. Animation
                // must continue independently, instead of making four large jumps per second.
                Thread.Sleep(1200);
                Check(tracking.Error==null,"motion worker failed: "+tracking.Error);
                var times=fake.Times();
                Check(times.Length>=18,"motion must continue between slow detector updates");
                var intervals=times.Skip(1).Select((t,i)=>t-times[i]).OrderBy(t=>t).ToArray();
                Check(intervals[intervals.Length/2]<65,"median animation interval should be close to 33 ms");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root,"motion-timing.json"),new JavaScriptSerializer().Serialize(new{updates=times.Length,medianIntervalMs=intervals[intervals.Length/2],fps=(times.Length-1)*1000/(times.Last()-times.First())}));
                fake.Edit();
                var timeout=Stopwatch.StartNew();
                while(tracking.Error==null&&timeout.ElapsedMilliseconds<2000)Thread.Sleep(10);
                Check(tracking.Error!=null,"motion must detect a manual OBS edit");
                tracking.Stop();int count=fake.Times().Length;
                Thread.Sleep(80);Check(fake.Times().Length==count,"no transforms may be written after Stop");
            }
        }
        sealed class MotionObs : IObsClient {
            readonly object gate=new object();
            Dictionary<string,object> transform;
            readonly List<double> timestamps=new List<double>();
            readonly Stopwatch clock=Stopwatch.StartNew();
            readonly JavaScriptSerializer json=new JavaScriptSerializer();
            public MotionObs(Dictionary<string,object> initial){transform=new Dictionary<string,object>(initial);}
            public bool Connected {get{return true;}}
            public double[] Times(){lock(gate)return timestamps.ToArray();}
            public void Edit(){lock(gate)transform["positionX"]=123;}
            public Dictionary<string,object> Call(string type,object data){
                Thread.Sleep(2);
                lock(gate){
                    if(type=="GetSceneItemTransform")return new Dictionary<string,object>{{"sceneItemTransform",new Dictionary<string,object>(transform)}};
                    if(type!="SetSceneItemTransform")throw new Exception("Unexpected animation request: "+type);
                    var body=json.Deserialize<Dictionary<string,object>>(json.Serialize(data));
                    foreach(var pair in J.Map(body,"sceneItemTransform"))transform[pair.Key]=pair.Value;
                    timestamps.Add(clock.Elapsed.TotalMilliseconds);return new Dictionary<string,object>();
                }
            }
            public void Dispose(){}
        }
    }
}
