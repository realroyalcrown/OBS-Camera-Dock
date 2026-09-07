using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage.Streams;

namespace CameraDock {
    public sealed class FaceSample {
        public double x, y, width, height, brightness, sharpness;
        public int count, frameWidth, frameHeight;
        public bool found, focusRegion;
    }
    sealed class FaceDetection {
        FaceDetector detector;
        FaceSample previous;
        DateTime lastFace;
        static T Wait<T>(IAsyncOperation<T> operation) {
            try {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (operation.Status == AsyncStatus.Started) {
                    if (DateTime.UtcNow > deadline) { operation.Cancel(); throw new TimeoutException("Detekce obličeje neodpovídá."); }
                    Thread.Sleep(5);
                }
                return operation.GetResults();
            } finally { operation.Close(); }
        }
        public void Initialize() {
            if (detector == null) detector = Wait(FaceDetector.CreateAsync());
        }
        public FaceSample Analyze(byte[] jpeg, FaceSample focusRegion = null) {
            Initialize();
            using (var stream = new MemoryStream(jpeg))
            using (var decoded = new Bitmap(stream))
            using (var image = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format24bppRgb)) {
                using (var graphics = Graphics.FromImage(image)) graphics.DrawImageUnscaled(decoded, 0, 0);
                int w = image.Width, h = image.Height;
                if (w > 1280 || h > 1280 || w < 32 || h < 32) throw new InvalidDataException("Neplatná velikost náhledu OBS.");
                byte[] gray = new byte[w * h];
                var bits = image.LockBits(new Rectangle(0,0,w,h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try {
                    byte[] row = new byte[w * 3];
                    for (int y = 0; y < h; y++) {
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                        for (int x = 0; x < w; x++) gray[y*w+x] = (byte)((29*row[x*3]+150*row[x*3+1]+77*row[x*3+2]) >> 8);
                    }
                } finally { image.UnlockBits(bits); }
                using (var bitmap = new SoftwareBitmap(BitmapPixelFormat.Gray8, w, h)) {
                    using (var writer = new DataWriter()) { writer.WriteBytes(gray); bitmap.CopyFromBuffer(writer.DetachBuffer()); }
                    var detected = Wait(detector.DetectFacesAsync(bitmap));
                    var faces = detected.Select(f => new FaceSample { found=true, count=detected.Count, frameWidth=w, frameHeight=h,
                        x=(double)f.FaceBox.X/w, y=(double)f.FaceBox.Y/h, width=(double)f.FaceBox.Width/w, height=(double)f.FaceBox.Height/h }).ToList();
                    if (faces.Count == 0) {
                        if(focusRegion!=null) {
                            var roi=new FaceSample{frameWidth=w,frameHeight=h,x=focusRegion.x,y=focusRegion.y,width=focusRegion.width,height=focusRegion.height,focusRegion=true};
                            Measure(gray,w,h,roi); return roi;
                        }
                        return new FaceSample { frameWidth=w, frameHeight=h };
                    }
                    FaceSample selected = faces.OrderByDescending(f => f.width*f.height).First();
                    if (previous != null && DateTime.UtcNow-lastFace < TimeSpan.FromSeconds(2)) {
                        var nearest = faces.OrderBy(f => FaceMath.Distance(previous,f)).First();
                        // Do not jump to a different person while the tracked face is briefly occluded.
                        if (FaceMath.Distance(previous,nearest) > 0.22) return new FaceSample { count=faces.Count, frameWidth=w, frameHeight=h };
                        selected = nearest;
                    }
                    Measure(gray,w,h,selected);
                    previous=selected; lastFace=DateTime.UtcNow; return selected;
                }
            }
        }
        internal static void Measure(byte[] gray, int w, int h, FaceSample face) {
            // Use the central face region, excluding most hair and background.
            int left=Math.Max(1,(int)((face.x+face.width*.18)*w)), right=Math.Min(w-2,(int)((face.x+face.width*.82)*w));
            int top=Math.Max(1,(int)((face.y+face.height*.18)*h)), bottom=Math.Min(h-2,(int)((face.y+face.height*.85)*h));
            var histogram=new int[256]; double sum=0, squares=0, lapSum=0, lapSquares=0; int n=0;
            for(int y=top;y<=bottom;y++) for(int x=left;x<=right;x++) {
                int i=y*w+x, v=gray[i]; histogram[v]++; sum+=v; squares+=v*v;
                int lap=gray[i-1]+gray[i+1]+gray[i-w]+gray[i+w]-4*v;
                lapSum+=lap; lapSquares+=(double)lap*lap; n++;
            }
            if(n==0) return;
            int count=0, median=0;
            for(;median<255;median++) { count+=histogram[median]; if(count>=n/2) break; }
            face.brightness=median;
            double variance=Math.Max(25,squares/n-(sum/n)*(sum/n));
            face.sharpness=Math.Max(0,lapSquares/n-(lapSum/n)*(lapSum/n))/variance;
        }
    }
}
