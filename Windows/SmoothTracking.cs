using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CameraDock {
    // Exact critically damped spring, using elapsed seconds instead of a per-detection lerp.
    sealed class MotionAxis {
        public double Value, Velocity;
        readonly double time, speed;
        public MotionAxis(double value,double responseTime,double maxSpeed) { Value=value; time=responseTime; speed=maxSpeed; }
        public void Step(double target,double elapsed) {
            double dt=FaceMath.Clamp(elapsed,0,.1);
            if(dt<=0) return;
            double omega=2/time, offset=Value-target, exp=Math.Exp(-omega*dt);
            double temp=(Velocity+omega*offset)*dt;
            double next=target+(offset+temp)*exp;
            double velocity=(Velocity-omega*temp)*exp;
            double delta=FaceMath.Clamp(next-Value,-speed*dt,speed*dt);
            if(Math.Abs(delta-(next-Value))>1e-12) velocity=delta/dt;
            next=Value+delta;
            if((target-Value)*(target-next)<0) { next=target; velocity=0; }
            Value=next; Velocity=velocity;
        }
    }
    sealed class SmoothTracking : IDisposable {
        readonly IObsClient obs;
        readonly TrackingRecovery recovery;
        readonly object gate=new object();
        readonly ManualResetEvent stop=new ManualResetEvent(false);
        readonly MotionAxis x,y,zoom;
        readonly double baseX,baseY;
        double targetX,targetY,targetZoom=1;
        readonly Stopwatch clock=Stopwatch.StartNew();
        double lastObservation;
        Thread worker;
        string error;
        int updates;
        public string Error { get { lock(gate) return error; } }
        public Dictionary<string,object> LastTransform { get { lock(gate) return recovery.last; } }
        public int Updates { get { lock(gate) return updates; } }
        public SmoothTracking(IObsClient client,TrackingRecovery state) {
            obs=client; recovery=state;
            double w=J.Number(state.original,"sourceWidth"),h=J.Number(state.original,"sourceHeight");
            baseX=(J.Number(state.original,"cropLeft")+(w-J.Number(state.original,"cropLeft")-J.Number(state.original,"cropRight"))/2)/w;
            baseY=(J.Number(state.original,"cropTop")+(h-J.Number(state.original,"cropTop")-J.Number(state.original,"cropBottom"))/2)/h;
            targetX=baseX;targetY=baseY;
            x=new MotionAxis(baseX,.45,.45);y=new MotionAxis(baseY,.45,.45);zoom=new MotionAxis(1,1.1,.65);
        }
        public void Start() {
            worker=new Thread(Run){IsBackground=true,Name="OBS smooth camera motion"};worker.Start();
        }
        public void Observe(FaceSample face,double maximumZoom) {
            if(face==null||!face.found) return;
            double h=J.Number(recovery.original,"sourceHeight");
            double available=h-J.Number(recovery.original,"cropTop")-J.Number(recovery.original,"cropBottom");
            double cx=face.x+face.width/2,cy=face.y+face.height/2;
            double z=FaceMath.Clamp(available*.32/(face.height*h),1,maximumZoom);
            lock(gate) {
                // Hysteresis is applied to the target, not the current animated position.
                // Tiny detection noise therefore cannot repeatedly stop/restart the motion.
                if(Math.Abs(cx-targetX)>.012) targetX=cx;
                if(Math.Abs(cy-targetY)>.012) targetY=cy;
                if(Math.Abs(z-targetZoom)>.04) targetZoom=z;
                lastObservation=clock.Elapsed.TotalSeconds;
            }
        }
        void Run() {
            bool timerResolution=timeBeginPeriod(1)==0;
            double previous=clock.Elapsed.TotalSeconds;
            try {
                while(!stop.WaitOne(0)) {
                    double begin=clock.Elapsed.TotalSeconds,dt=begin-previous;previous=begin;
                    double tx,ty,tz;
                    lock(gate) {
                        bool lost=begin-lastObservation>3;
                        tx=lost?baseX:targetX;ty=lost?baseY:targetY;tz=lost?1:targetZoom;
                    }
                    x.Step(tx,dt);y.Step(ty,dt);zoom.Step(tz,dt);
                    var current=J.Map(obs.Call("GetSceneItemTransform",new{sceneName=recovery.scene,sceneItemId=recovery.itemId}),"sceneItemTransform");
                    if(stop.WaitOne(0)) break;
                    if(!FaceMath.SameTransform(current,LastTransform)) throw new InvalidOperationException("Transformace zdroje se změnila v OBS; tracking zastaven.");
                    if(J.Number(current,"sourceWidth")!=J.Number(recovery.original,"sourceWidth")||J.Number(current,"sourceHeight")!=J.Number(recovery.original,"sourceHeight"))
                        throw new InvalidOperationException("Rozlišení zdroje se změnilo; obnovte záběr a spusťte tracking znovu.");
                    var transform=FaceMath.Crop(recovery.original,x.Value,y.Value,zoom.Value);
                    var expected=new Dictionary<string,object>(current);
                    foreach(var pair in transform) expected[pair.Key]=pair.Value;
                    if(!FaceMath.SameTransform(current,expected)) {
                        obs.Call("SetSceneItemTransform",new{sceneName=recovery.scene,sceneItemId=recovery.itemId,sceneItemTransform=transform});
                        lock(gate) { recovery.last=expected; updates++; }
                    }
                    // No screenshot or detector calls use this connection. Missed deadlines are
                    // dropped rather than queued, preventing delayed bursts of old positions.
                    int wait=Math.Max(1,(int)Math.Ceiling((1.0/30-(clock.Elapsed.TotalSeconds-begin))*1000));
                    if(stop.WaitOne(wait)) break;
                }
            } catch(Exception ex) { lock(gate) error=ex.Message; }
            finally { if(timerResolution) timeEndPeriod(1); }
        }
        public void Stop() {
            stop.Set();
            // ObsClient bounds each call to 3 s. Join before restoration so an in-flight
            // SetSceneItemTransform can never overwrite the restored transform afterwards.
            if(worker!=null) worker.Join();
        }
        public void Dispose() { Stop();stop.Dispose(); }
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
    }
}
