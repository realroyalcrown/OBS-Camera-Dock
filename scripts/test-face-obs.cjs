// Integration test against a local OBS WebSocket double; never touches real OBS.
// Usage: node scripts/test-face-obs.cjs http://127.0.0.1:24682 path/to/blank.jpg
const http=require('node:http'), crypto=require('node:crypto'), fs=require('node:fs'), assert=require('node:assert/strict');
const base=process.argv[2], fixture=process.argv[3];
if(!base||!fixture)throw new Error('Pass an isolated helper URL and a blank JPEG fixture.');
const original={alignment:5,boundsAlignment:0,boundsHeight:0,boundsType:'OBS_BOUNDS_NONE',boundsWidth:0,cropBottom:0,cropLeft:0,cropRight:0,cropTop:0,positionX:0,positionY:0,rotation:0,scaleX:1,scaleY:1,sourceWidth:1920,sourceHeight:1080,width:1920,height:1080};
let transform={...original}, writes=0;
const sockets=new Set();
const server=http.createServer((req,res)=>res.end('test server'));
const hash=x=>crypto.createHash('sha256').update(x).digest('base64');
function frame(data){const b=Buffer.from(JSON.stringify(data));if(b.length<126)return Buffer.concat([Buffer.from([0x81,b.length]),b]);const h=Buffer.alloc(4);h[0]=0x81;h[1]=126;h.writeUInt16BE(b.length,2);return Buffer.concat([h,b]);}
server.on('upgrade',(req,socket)=>{
 sockets.add(socket);socket.on('close',()=>sockets.delete(socket));socket.on('error',()=>{});
 const accept=crypto.createHash('sha1').update(req.headers['sec-websocket-key']+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
 socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: '+accept+'\r\n\r\n');
 socket.write(frame({op:0,d:{rpcVersion:1,authentication:{salt:'test-salt',challenge:'test-challenge'}}}));
 let incoming=Buffer.alloc(0),identified=false;
 socket.on('data',chunk=>{
  incoming=Buffer.concat([incoming,chunk]);
  while(incoming.length>=2){
   let n=incoming[1]&127,offset=2;if(n===126){if(incoming.length<4)return;n=incoming.readUInt16BE(2);offset=4;}else if(n===127){if(incoming.length<10)return;n=Number(incoming.readBigUInt64BE(2));offset=10;}
   const masked=(incoming[1]&128)!==0, maskAt=offset;if(masked)offset+=4;if(incoming.length<offset+n)return;
   const op=incoming[0]&15,payload=Buffer.from(incoming.subarray(offset,offset+n));if(masked)for(let i=0;i<n;i++)payload[i]^=incoming[maskAt+i%4];incoming=incoming.subarray(offset+n);
   if(op===8){socket.end();return;}if(op!==1)continue;
   const message=JSON.parse(payload);
   if(!identified){if(message.op!==1||message.d.authentication!==hash(hash('test-password'+'test-salt')+'test-challenge')){socket.end(Buffer.from([0x88,0x02,0x0f,0xa9]));return;}identified=true;socket.write(frame({op:2,d:{negotiatedRpcVersion:1}}));continue;}
   const d=message.d,p=d.requestData;let response={},error=null;
   switch(d.requestType){
    case 'GetSceneList':response={scenes:[{sceneName:'Test'}],currentProgramSceneName:'Test'};break;
    case 'GetSceneItemList':response={sceneItems:[{inputKind:'dshow_input',sceneItemId:7,sourceName:'Test camera',sceneItemEnabled:true}]};break;
    case 'GetInputSettings':response={inputSettings:{video_device_id:'not-the-selected-device'}};break;
    case 'GetSceneItemTransform':response={sceneItemTransform:transform};break;
    case 'SetSceneItemTransform':
     if(('boundsWidth'in p.sceneItemTransform&&p.sceneItemTransform.boundsWidth<1)||('boundsHeight'in p.sceneItemTransform&&p.sceneItemTransform.boundsHeight<1)){error='bounds must be at least 1';break;}
     transform={...transform,...p.sceneItemTransform};writes++;break;
    case 'GetSourceScreenshot':response={imageData:'data:image/jpeg;base64,'+fs.readFileSync(fixture).toString('base64')};break;
    default:error='Unsupported test request '+d.requestType;
   }
   // Deliberately fragment network writes to exercise the real client's framing.
   const bytes=frame({op:7,d:{requestId:d.requestId,requestStatus:{result:!error,code:error?400:100,comment:error||''},responseData:response}});
   socket.write(bytes.subarray(0,7));socket.write(bytes.subarray(7));
  }
 });
});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function api(path,body,expected=200){const r=await fetch(base+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await r.json();assert.equal(r.status,expected,JSON.stringify(data));return data;}
(async()=>{
 await new Promise(r=>server.listen(4466,'127.0.0.1',r));
 try{
  await api('/api/face/connect',{port:4466,password:'wrong'},400);
  const connected=await api('/api/face/connect',{port:4466,password:'test-password'});assert.equal(connected.sources.length,1);
  await api('/api/face/start',{scene:'Test',itemId:7,tracking:true,exposure:false,focus:false,maxZoom:9,target:140},400);
  await api('/api/face/start',{scene:'Test',itemId:7,tracking:true,exposure:false,focus:false,maxZoom:1.6,target:140});
  await sleep(3800);let state=await api('/api/face/state');assert.equal(state.running,true);assert.equal(state.face.found,false);assert.deepEqual(transform,original,'lost face with no previous movement should keep the original frame');
  const preview=await fetch(base+'/api/face/preview');assert.equal(preview.status,200);assert.ok((await preview.arrayBuffer()).byteLength>0);
  await api('/api/face/stop',{});assert.deepEqual(transform,original,'stop restores source including zero unused bounds');
  await api('/api/face/start',{scene:'Test',itemId:7,tracking:true,exposure:false,focus:false,maxZoom:1.6,target:140});
  transform.positionX=73;await sleep(3800);state=await api('/api/face/state');assert.equal(state.running,false);assert.equal(state.recoveryPending,true);assert.equal(transform.positionX,73,'external OBS edit must be preserved');
  await api('/api/face/restore',{});assert.deepEqual(transform,original,'explicit recovery restores original');
  console.log('OBS integration passed: authentication, fragmented replies, preview/detection, invalid settings, crop restore, external-edit protection and recovery.');
 }finally{for(const s of sockets)s.destroy();await new Promise(r=>server.close(r));}
})().catch(e=>{console.error(e);process.exitCode=1;});
