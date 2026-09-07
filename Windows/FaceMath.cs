using System;
using System.Collections.Generic;

namespace CameraDock {
    static class FaceMath {
        public static double Clamp(double value,double min,double max) { return Math.Max(min,Math.Min(max,value)); }
        public static double Distance(FaceSample a, FaceSample b) {
            return Math.Sqrt(Math.Pow(a.x+a.width/2-b.x-b.width/2,2)+Math.Pow(a.y+a.height/2-b.y-b.height/2,2));
        }
        public static Dictionary<string, object> Crop(Dictionary<string, object> original, double cx, double cy, double zoom) {
            double w=J.Number(original,"sourceWidth"), h=J.Number(original,"sourceHeight");
            double left=J.Number(original,"cropLeft"), top=J.Number(original,"cropTop");
            double usableW=w-left-J.Number(original,"cropRight"), usableH=h-top-J.Number(original,"cropBottom");
            if(usableW<2 || usableH<2) throw new ArgumentException("Zdroj kamery v OBS neposkytuje obraz nebo je celý oříznutý. Aktivujte scénu s kamerou a zkontrolujte její obraz.");
            zoom=Clamp(zoom,1,3);
            int cropW=Math.Max(2,(int)Math.Round(usableW/zoom)), cropH=Math.Max(2,(int)Math.Round(usableH/zoom));
            int x=(int)Math.Round(Clamp(cx*w-cropW/2.0,left,left+usableW-cropW));
            int y=(int)Math.Round(Clamp(cy*h-cropH/2.0,top,top+usableH-cropH));
            return new Dictionary<string, object> {
                {"cropLeft",x},{"cropTop",y},{"cropRight",(int)w-x-cropW},{"cropBottom",(int)h-y-cropH},
                {"scaleX",J.Number(original,"scaleX")*usableW/cropW},{"scaleY",J.Number(original,"scaleY")*usableH/cropH}
            };
        }
        public static string[] TransformKeys = {"positionX","positionY","rotation","scaleX","scaleY","alignment","boundsType","boundsAlignment","boundsWidth","boundsHeight","cropLeft","cropRight","cropTop","cropBottom"};
        public static Dictionary<string, object> Writable(Dictionary<string, object> transform) {
            var result=new Dictionary<string, object>();
            foreach(var key in TransformKeys) if(transform.ContainsKey(key)) {
                // GetSceneItemTransform reports zero for unused bounds; Set rejects values below one.
                if((key=="boundsWidth"||key=="boundsHeight")&&J.Number(transform,key)<1) continue;
                result[key]=transform[key];
            }
            return result;
        }
        public static bool SameTransform(Dictionary<string, object> a,Dictionary<string, object> b) {
            foreach(var key in TransformKeys) {
                if(!a.ContainsKey(key) || !b.ContainsKey(key)) continue;
                if(key=="boundsType") { if(J.String(a,key)!=J.String(b,key)) return false; }
                else if(Math.Abs(J.Number(a,key)-J.Number(b,key))>(key.StartsWith("scale") ? .002 : .1)) return false;
            }
            return true;
        }
        public static int ExposureDirection(double brightness,double target) {
            if(brightness<=0) return 0; // Black/inactive source is not an exposure measurement.
            double ev=Math.Log(target/brightness,2);
            return ev>.72 ? 1 : ev<-.72 ? -1 : 0;
        }
    }
}
