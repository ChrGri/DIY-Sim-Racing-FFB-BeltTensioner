#pragma once

#include <Arduino.h>

// Self-contained live telemetry page (no external libraries: the ESP32 access point has no internet)
static const char WEBGRAPHS_INDEX_HTML[] PROGMEM = R"rawliteral(<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Belt Tensioner Graphs</title>
<style>
:root{--bg:#f4f5f7;--card:#fff;--fg:#1c1f24;--muted:#6b7280;--grid:#e3e5e9;--border:#d9dce1;--accent:#2563eb}
@media (prefers-color-scheme:dark){:root{--bg:#15171b;--card:#1f2228;--fg:#e8eaed;--muted:#9aa0a8;--grid:#2e323a;--border:#353a43;--accent:#60a5fa}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
header{display:flex;flex-wrap:wrap;gap:8px 16px;align-items:center;padding:12px 16px;border-bottom:1px solid var(--border);background:var(--card)}
h1{font-size:17px;margin:0;font-weight:600}
.status{display:flex;align-items:center;gap:6px;color:var(--muted)}
.dot{width:9px;height:9px;border-radius:50%;background:#9ca3af}
.dot.ok{background:#16a34a}.dot.err{background:#dc2626}
.controls{display:flex;flex-wrap:wrap;gap:8px;margin-left:auto;align-items:center}
button,select{font:inherit;color:var(--fg);background:var(--bg);border:1px solid var(--border);border-radius:6px;padding:5px 10px;cursor:pointer}
button.on{background:var(--accent);border-color:var(--accent);color:#fff}
main{padding:12px 16px;max-width:1400px;margin:0 auto}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;margin-bottom:12px}
.card{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:10px 12px}
.card h2{font-size:13px;margin:0 0 6px;color:var(--muted);font-weight:600;text-transform:uppercase;letter-spacing:.04em}
.kv{display:grid;grid-template-columns:repeat(3,1fr);gap:6px 10px}
.kv div span{display:block;color:var(--muted);font-size:12px}
.kv div b{font-variant-numeric:tabular-nums;font-size:16px;font-weight:600}
.charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,560px),1fr));gap:12px}
.chart{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:8px 10px 6px}
.chart h3{font-size:14px;margin:0 0 2px;font-weight:600}
.legend{display:flex;flex-wrap:wrap;gap:4px 12px;font-size:12px;color:var(--muted);margin-bottom:4px}
.legend i{display:inline-block;width:12px;height:3px;border-radius:2px;margin-right:4px;vertical-align:middle}
canvas{display:block;width:100%;height:230px;touch-action:none}
#wifi{display:none;margin-bottom:12px}
#wifi.open{display:block}
.wgrid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,320px),1fr));gap:12px 24px}
.wstat{display:grid;grid-template-columns:auto 1fr;gap:4px 12px;margin:0}
.wstat dt{color:var(--muted)}.wstat dd{margin:0;overflow-wrap:anywhere}
form label{display:block;color:var(--muted);font-size:12px;margin:6px 0 2px}
input[type=text],input[type=password]{width:100%;font:inherit;color:var(--fg);background:var(--bg);border:1px solid var(--border);border-radius:6px;padding:6px 8px}
.row{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:8px}
.row input[type=text]{flex:1;min-width:0}
.msg{margin-top:8px;min-height:1.4em;font-size:13px}.msg.err{color:#dc2626}.msg.ok{color:#16a34a}
a{color:var(--accent)}
</style>
</head>
<body>
<header>
<h1>DIY Belt Tensioner &ndash; Live Graphs</h1>
<div class="status"><span class="dot" id="dot"></span><span id="conn">connecting&hellip;</span></div>
<div class="controls">
<label>Window <select id="win"><option value="5">5 s</option><option value="10" selected>10 s</option><option value="30">30 s</option><option value="60">60 s</option></select></label>
<button id="pause">Pause</button>
<button id="clear">Clear</button>
<button id="csv">CSV</button>
<button id="wifiBtn">WiFi</button>
</div>
</header>
<main>
<div class="card" id="wifi"><h2>WiFi settings</h2><div class="wgrid">
<dl class="wstat" id="wstat"></dl>
<form id="wform" autocomplete="off">
<label for="ssid">Local network (SSID)</label>
<div class="row" style="margin-top:0"><input type="text" id="ssid" list="nets" maxlength="32" required><datalist id="nets"></datalist><button type="button" id="scan">Scan</button></div>
<label for="pass">Password</label>
<input type="password" id="pass" maxlength="63" placeholder="empty = keep saved password / open network">
<div class="row"><label style="margin:0;color:var(--fg)"><input type="checkbox" id="showpw"> show</label></div>
<div class="row"><button type="submit" class="on">Save &amp; connect</button><button type="button" id="retry">Retry</button><button type="button" id="forget">Forget network</button></div>
<div class="msg" id="wmsg"></div>
</form>
</div></div>
<div class="cards" id="cards"></div>
<div class="charts" id="charts"></div>
</main>
<script>
const HOMING=["Idle","Start","Wait servo","Approach min","Backoff min","Approach max","Backoff max","Return home","Homed","Failed"];
const COLORS=[["#2563eb","#93c5fd","#0891b2"],["#ea580c","#fdba74","#c026d3"]];
const KEEP_MS=60000;
let info=null,seq=0,samples=[],paused=false,hoverX=null,lastOk=0,rateCount=0,rate=0;
const $=id=>document.getElementById(id);
const css=n=>getComputedStyle(document.documentElement).getPropertyValue(n).trim();

// Servo reports current in % of its rated current (Pr7.12): derive ampere, torque and mechanical power
// from the motor ratings (rated torque = Pr7.14 power / Pr7.10 speed)
function ratedTorque(a){const r=info.rated[a];return r.rpm>0?r.powerW/(r.rpm*2*Math.PI/60):0}
function parse(row){
  const s={t:row[0],a:[]};
  for(let i=0;i<info.axes;i++){
    // Steps are converted with each axis' own spindle pitch
    const o=1+i*9,f=row[o+7],cur=row[o+4],speed=row[o+8],k=info.stepsPerMm[i];
    const torque=cur/100*ratedTorque(i),omega=speed/info.stepsPerRev*2*Math.PI;
    s.a.push({pos:row[o]/k,target:row[o+1]<0?null:row[o+1]/k,servo:row[o+2]/k,speed:speed/k,err:row[o+3],cur,volt:row[o+5]/10,hs:row[o+6],
      valid:!!(f&1),homed:!!(f&2),powered:!!(f&4),amps:cur/100*info.rated[i].currentA,torque,power:torque*omega});
  }
  return s;
}

// Chart definitions: each series reads one value from an axis sample (null = gap)
function chartDefs(){
  const defs=[
    {title:"Position (mm)",unit:"mm",dec:2,series:a=>[
      {label:`M${a+1} actual`,get:s=>s.pos},
      {label:`M${a+1} target`,get:s=>s.target,dash:[5,4]},
      {label:`M${a+1} servo encoder`,get:s=>s.homed?s.servo:null}]},
    {title:"Servo current (A)",unit:"A",dec:2,series:a=>[{label:`M${a+1} current`,get:s=>s.valid?s.amps:null}],
      lines:a=>{const v=info.threshold[a]/100*info.rated[a].currentA;return[{label:`M${a+1} homing threshold (${info.threshold[a]} %)`,v},{v:-v}]}},
    {title:"Mechanical power (W)",unit:"W",dec:1,series:a=>[{label:`M${a+1} power (+ drive / - braking)`,get:s=>s.valid?s.power:null}]},
    {title:"DC bus voltage (V)",unit:"V",dec:1,series:a=>[{label:`M${a+1} voltage`,get:s=>s.valid?s.volt:null}]},
    {title:"Following error (steps)",unit:"steps",dec:0,series:a=>[{label:`M${a+1} error`,get:s=>s.valid?s.err:null}]}
  ];
  for(const d of defs){
    d.all=[];d.refs=[];
    for(let a=0;a<info.axes;a++){
      d.series(a).forEach((s,k)=>{s.axis=a;s.color=COLORS[a][k];d.all.push(s)});
      if(d.lines)d.lines(a).forEach(l=>{l.color=COLORS[a][0];d.refs.push(l)});
    }
  }
  return defs;
}

let charts=[];
function buildUi(){
  const cards=$("cards");cards.innerHTML="";
  for(let a=0;a<info.axes;a++){
    const r=info.rated[a];
    cards.insertAdjacentHTML("beforeend",`<div class="card"><h2>Motor ${a+1} &ndash; ${r.powerW} W, ${r.currentA.toFixed(2)} A, ${ratedTorque(a).toFixed(2)} Nm rated (${r.fromServo?"read from servo":"default, servo not read yet"})</h2><div class="kv">
<div><span>Position</span><b id="p${a}">-</b></div><div><span>Target</span><b id="g${a}">-</b></div><div><span>Speed</span><b id="s${a}">-</b></div>
<div><span>Current</span><b id="c${a}">-</b></div><div><span>Torque</span><b id="q${a}">-</b></div><div><span>Power</span><b id="w${a}">-</b></div>
<div><span>Voltage</span><b id="v${a}">-</b></div><div><span>Error</span><b id="e${a}">-</b></div><div><span>State</span><b id="h${a}">-</b></div></div></div>`);
  }
  cards.insertAdjacentHTML("beforeend",`<div class="card"><h2>Device</h2><div class="kv">
<div><span>Firmware</span><b>${info.version}</b></div><div><span>Sample rate</span><b id="rate">-</b></div><div><span>Range</span><b>${info.range.map((r,i)=>(r/info.stepsPerMm[i]).toFixed(0)).join(" / ")} mm</b></div>
<div><span>Pitch</span><b>${info.stepsPerMm.map(k=>(info.stepsPerRev/k).toFixed(1)).join(" / ")} mm</b></div></div></div>`);
  const host=$("charts");host.innerHTML="";
  charts=chartDefs().map(d=>{
    const el=document.createElement("div");el.className="chart";
    el.innerHTML=`<h3>${d.title}</h3><div class="legend">${d.all.map(s=>`<span><i style="background:${s.color}"></i>${s.label}</span>`).join("")}${d.refs.filter(l=>l.label).map(l=>`<span><i style="background:${l.color};opacity:.5"></i>${l.label}</span>`).join("")}</div><canvas></canvas>`;
    host.appendChild(el);
    const cv=el.querySelector("canvas");
    cv.addEventListener("pointermove",e=>{const r=cv.getBoundingClientRect();hoverX=(e.clientX-r.left)/r.width});
    cv.addEventListener("pointerleave",()=>{hoverX=null});
    return {def:d,cv};
  });
}

function niceStep(range){
  const raw=range/5,p=Math.pow(10,Math.floor(Math.log10(raw))),n=raw/p;
  return (n<1.5?1:n<3?2:n<7?5:10)*p;
}

function drawChart(c,view,t0,t1){
  const cv=c.cv,dpr=window.devicePixelRatio||1,W=cv.clientWidth,H=cv.clientHeight;
  if(cv.width!==Math.round(W*dpr)||cv.height!==Math.round(H*dpr)){cv.width=Math.round(W*dpr);cv.height=Math.round(H*dpr)}
  const g=cv.getContext("2d");g.setTransform(dpr,0,0,dpr,0,0);g.clearRect(0,0,W,H);
  const d=c.def,L=54,R=8,T=8,B=22,pw=W-L-R,ph=H-T-B;
  let lo=Infinity,hi=-Infinity;
  for(const s of view)for(const se of d.all){const v=se.get(s.a[se.axis]);if(v!==null){if(v<lo)lo=v;if(v>hi)hi=v}}
  for(const l of d.refs){if(l.v<lo)lo=l.v;if(l.v>hi)hi=l.v}
  if(lo===Infinity){lo=0;hi=1}
  if(hi-lo<1e-6){lo-=1;hi+=1}
  const pad=(hi-lo)*0.08;lo-=pad;hi+=pad;
  const step=niceStep(hi-lo);lo=Math.floor(lo/step)*step;hi=Math.ceil(hi/step)*step;
  const X=t=>L+(t-t0)/(t1-t0)*pw,Y=v=>T+(hi-v)/(hi-lo)*ph;
  g.font="11px system-ui,sans-serif";g.lineWidth=1;
  g.strokeStyle=css("--grid");g.fillStyle=css("--muted");g.textAlign="right";g.textBaseline="middle";
  const dec=Math.max(0,-Math.floor(Math.log10(step)));
  for(let v=lo;v<=hi+step/2;v+=step){const y=Math.round(Y(v))+.5;g.beginPath();g.moveTo(L,y);g.lineTo(L+pw,y);g.stroke();g.fillText(v.toFixed(dec),L-6,y)}
  g.textAlign="center";g.textBaseline="top";
  const span=(t1-t0)/1000,ts=span<=10?1:span<=30?5:10;
  for(let s=0;s<=span;s+=ts){const x=Math.round(X(t1-s*1000))+.5;g.beginPath();g.moveTo(x,T);g.lineTo(x,T+ph);g.stroke();g.fillText(s===0?"now":`-${s}s`,x,T+ph+5)}
  g.save();g.beginPath();g.rect(L,T,pw,ph);g.clip();
  for(const l of d.refs){g.strokeStyle=l.color;g.globalAlpha=.45;g.setLineDash([3,3]);g.beginPath();g.moveTo(L,Y(l.v));g.lineTo(L+pw,Y(l.v));g.stroke()}
  g.globalAlpha=1;g.lineWidth=1.6;
  for(const se of d.all){
    g.strokeStyle=se.color;g.setLineDash(se.dash||[]);g.beginPath();let pen=false;
    for(const s of view){const v=se.get(s.a[se.axis]);if(v===null){pen=false;continue}const x=X(s.t),y=Y(v);if(pen)g.lineTo(x,y);else{g.moveTo(x,y);pen=true}}
    g.stroke();
  }
  g.setLineDash([]);g.restore();
  if(hoverX!==null&&view.length){
    const tx=t0+(hoverX*W-L)/pw*(t1-t0);let best=view[0];
    for(const s of view)if(Math.abs(s.t-tx)<Math.abs(best.t-tx))best=s;
    const x=X(best.t);g.strokeStyle=css("--muted");g.lineWidth=1;g.beginPath();g.moveTo(x,T);g.lineTo(x,T+ph);g.stroke();
    const lines=[`${((best.t-t1)/1000).toFixed(2)} s`];
    for(const se of d.all){const v=se.get(best.a[se.axis]);lines.push(`${se.label}: ${v===null?"-":v.toFixed(d.dec)}`)}
    g.font="12px system-ui,sans-serif";const bw=Math.max(...lines.map(l=>g.measureText(l).width))+14,bh=lines.length*16+8;
    let bx=x+10;if(bx+bw>L+pw)bx=x-10-bw;
    g.fillStyle=css("--card");g.strokeStyle=css("--border");g.fillRect(bx,T+4,bw,bh);g.strokeRect(bx+.5,T+4.5,bw,bh);
    g.textAlign="left";g.textBaseline="top";
    lines.forEach((l,i)=>{g.fillStyle=i===0?css("--muted"):d.all[i-1].color;g.fillText(l,bx+7,T+9+i*16)});
  }
}

function updateCards(s){
  for(let a=0;a<info.axes;a++){
    const x=s.a[a];
    $("p"+a).textContent=x.pos.toFixed(2)+" mm";
    $("g"+a).textContent=x.target===null?"-":x.target.toFixed(2)+" mm";
    $("s"+a).textContent=x.speed.toFixed(0)+" mm/s";
    $("c"+a).textContent=x.valid?`${x.amps.toFixed(2)} A (${x.cur} %)`:"-";
    $("q"+a).textContent=x.valid?x.torque.toFixed(2)+" Nm":"-";
    $("w"+a).textContent=x.valid?x.power.toFixed(1)+" W":"-";
    $("v"+a).textContent=x.valid?x.volt.toFixed(1)+" V":"no servo";
    $("e"+a).textContent=x.valid?x.err:"-";
    $("h"+a).textContent=(HOMING[x.hs]||x.hs)+(x.powered?"":" (off)");
  }
  $("rate").textContent=rate.toFixed(0)+" Hz";
}

let frozen=null;
function render(){
  const view=paused&&frozen?frozen:samples;
  if(info&&view.length){
    const t1=view[view.length-1].t,t0=t1-$("win").value*1000;
    let i=view.length-1;while(i>0&&view[i-1].t>=t0)i--;
    const vis=view.slice(Math.max(0,i-1));
    for(const c of charts)drawChart(c,vis,t0,t1);
    updateCards(view[view.length-1]);
  }
  requestAnimationFrame(render);
}

async function poll(){
  try{
    const r=await fetch("/data?since="+seq,{cache:"no-store"});
    const j=await r.json();
    seq=j.seq;
    for(const row of j.s)samples.push(parse(row));
    rateCount+=j.s.length;
    if(samples.length){const tEnd=samples[samples.length-1].t;let k=0;while(k<samples.length&&samples[k].t<tEnd-KEEP_MS)k++;if(k)samples.splice(0,k)}
    lastOk=Date.now();$("dot").className="dot ok";$("conn").textContent="live";
    setTimeout(poll,j.s.length>=128?0:80);
  }catch(e){
    $("dot").className="dot err";$("conn").textContent="no connection";
    setTimeout(poll,1000);
  }
}
setInterval(()=>{rate=rateCount;rateCount=0},1000);

$("pause").onclick=()=>{paused=!paused;frozen=paused?samples.slice():null;$("pause").classList.toggle("on",paused);$("pause").textContent=paused?"Resume":"Pause"};
$("clear").onclick=()=>{samples=[];frozen=paused?[]:null};
$("csv").onclick=()=>{
  const head=["time_ms"];
  for(let a=1;a<=info.axes;a++)head.push(`m${a}_pos_mm`,`m${a}_target_mm`,`m${a}_servo_mm`,`m${a}_speed_mm_s`,`m${a}_error_steps`,`m${a}_current_pct`,`m${a}_current_a`,`m${a}_torque_nm`,`m${a}_power_w`,`m${a}_voltage_v`,`m${a}_state`);
  const rows=[head.join(",")];
  for(const s of(frozen||samples)){
    const r=[s.t];
    for(const x of s.a)r.push(x.pos.toFixed(3),x.target===null?"":x.target.toFixed(3),x.homed?x.servo.toFixed(3):"",x.speed.toFixed(1),x.valid?x.err:"",x.valid?x.cur:"",
      x.valid?x.amps.toFixed(3):"",x.valid?x.torque.toFixed(3):"",x.valid?x.power.toFixed(2):"",x.valid?x.volt.toFixed(1):"",HOMING[x.hs]||x.hs);
    rows.push(r.join(","));
  }
  const url=URL.createObjectURL(new Blob([rows.join("\n")],{type:"text/csv"}));
  const el=document.createElement("a");el.href=url;el.download="belt_tensioner_"+Date.now()+".csv";el.click();URL.revokeObjectURL(url);
};

// WiFi settings panel
const esc=s=>String(s).replace(/[&<>"]/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]));
let wifiTimer=null,ssidFilled=false;
function wmsg(t,cls){const m=$("wmsg");m.textContent=t;m.className="msg "+(cls||"")}
async function wifiStatus(){
  try{
    const w=await (await fetch("/wifi",{cache:"no-store"})).json();
    let sta;
    if(w.connected)sta=`connected to <b>${esc(w.ssid)}</b> (${w.rssi} dBm)<br><a href="http://${w.ip}/">http://${w.ip}/</a> &middot; <a href="http://${w.host}/">http://${w.host}/</a>`;
    else if(w.connecting)sta=`connecting to <b>${esc(w.ssid)}</b>&hellip;`;
    else if(w.failed)sta=`could not connect to <b>${esc(w.ssid)}</b>`;
    else sta=w.ssid?`not connected (${esc(w.ssid)})`:"not configured";
    $("wstat").innerHTML=`<dt>Local network</dt><dd>${sta}</dd>
<dt>Saved on device</dt><dd>${w.saved?"yes":"no"+(w.ssid?" (Config.h default)":"")}</dd>
<dt>Access point</dt><dd>${w.ap?`<b>${esc(w.apSsid)}</b> &ndash; <a href="http://${w.apIp}/">http://${w.apIp}/</a>${w.apCloseIn>=0?`<br>closes in ${w.apCloseIn} s`:""}`:"off"}</dd>`;
    if(!ssidFilled&&w.ssid){$("ssid").value=w.ssid;ssidFilled=true}
  }catch(e){$("wstat").innerHTML="<dt>Status</dt><dd>no connection to device</dd>"}
}
$("wifiBtn").onclick=()=>{
  const open=$("wifi").classList.toggle("open");$("wifiBtn").classList.toggle("on",open);
  clearInterval(wifiTimer);if(open){wifiStatus();wifiTimer=setInterval(wifiStatus,2000)}
};
async function wifiPost(params,okText){
  try{
    const j=await (await fetch("/wifi",{method:"POST",body:new URLSearchParams(params)})).json();
    if(j.ok){wmsg(okText,"ok");$("pass").value=""}else wmsg(j.error||"Error","err");
  }catch(e){wmsg("No answer from the device. If you are on its access point, it may be switching networks.","err")}
  setTimeout(wifiStatus,500);
}
$("wform").onsubmit=e=>{e.preventDefault();wifiPost({action:"save",ssid:$("ssid").value,pass:$("pass").value},"Saved. Connecting - the new address appears above.")};
$("retry").onclick=()=>wifiPost({action:"connect"},"Connecting...");
$("forget").onclick=()=>{if(confirm("Delete the saved network from the device?"))wifiPost({action:"forget"},"Saved network deleted.")};
$("showpw").onchange=e=>{$("pass").type=e.target.checked?"text":"password"};
$("scan").onclick=async()=>{
  const b=$("scan");b.disabled=true;b.textContent="Scanning...";
  try{
    let j=await (await fetch("/scan?start=1",{cache:"no-store"})).json();
    for(let i=0;i<40&&j.running;i++){await new Promise(r=>setTimeout(r,500));j=await (await fetch("/scan",{cache:"no-store"})).json()}
    const nets=(j.networks||[]).sort((x,y)=>y.rssi-x.rssi).filter((n,i,a)=>a.findIndex(m=>m.ssid===n.ssid)===i);
    $("nets").innerHTML=nets.map(n=>`<option value="${esc(n.ssid)}">${n.rssi} dBm${n.open?" (open)":""}</option>`).join("");
    wmsg(nets.length?`${nets.length} networks found - pick one in the SSID field.`:"No networks found.",nets.length?"ok":"err");
  }catch(e){wmsg("Scan failed.","err")}
  b.disabled=false;b.textContent="Scan";
};

(async function start(){
  try{info=await (await fetch("/info")).json()}catch(e){$("dot").className="dot err";$("conn").textContent="no connection";setTimeout(start,1000);return}
  buildUi();poll();requestAnimationFrame(render);
  // Motor ratings are read from the servo after it connects: refresh until all axes report them
  const refresh=setInterval(async()=>{
    try{
      const n=await (await fetch("/info",{cache:"no-store"})).json();
      if(JSON.stringify(n.rated)!==JSON.stringify(info.rated)){info=n;buildUi()}
      if(n.rated.every(r=>r.fromServo))clearInterval(refresh);
    }catch(e){}
  },3000);
})();
</script>
</body>
</html>
)rawliteral";
