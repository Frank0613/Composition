from typing import Optional
from fastapi import FastAPI
from pydantic import BaseModel
from fastapi.responses import Response, HTMLResponse, JSONResponse
import base64
import threading
import time
import uvicorn
import webbrowser

app = FastAPI(title="Unity Camera Data Server (RGB + Depth)")

# ---------- Models ----------
class PoseData(BaseModel):
    px: float; py: float; pz: float
    rx: float; ry: float; rz: float

class TelemetryPayload(BaseModel):
    pose: PoseData
    fov: float
    # 新格式（推薦，與 DataStreamer.cs 對齊）
    cam_rgb_b64: Optional[str] = None
    cam_depth_b64: Optional[str] = None
    # 舊格式（只送一張 image）
    image_b64: Optional[str] = None
    width: Optional[int] = None
    height: Optional[int] = None

class ControlPose(BaseModel):
    px: Optional[float] = None; py: Optional[float] = None; pz: Optional[float] = None
    rx: Optional[float] = None; ry: Optional[float] = None; rz: Optional[float] = None

class ControlResponse(BaseModel):
    # 與 DataStreamer.cs 對應的欄位
    apply: bool = False
    fov: Optional[float] = None
    pose: Optional[ControlPose] = None
    # 新增：測試用旗標
    isReset: bool = False   # one-shot：回傳一次後自動清除
    isDone: bool  = False   # sticky：維持為 True，直到 reset 後清除

# ---------- In-memory states ----------
current_control = ControlResponse(apply=False, pose=ControlPose(), fov=None)
_state_lock = threading.Lock()

_last_rgb_jpeg: Optional[bytes] = None
_last_depth_jpeg: Optional[bytes] = None
_last_pose: Optional[PoseData] = None
_last_fov: Optional[float] = None
_last_wh = (None, None)
_last_update_ts: Optional[float] = None
_frame_count: int = 0

def _snapshot_and_fold_reset() -> ControlResponse:
    """
    取出 current_control 的快照。
    若 isReset 為 True：回傳快照後，立刻把 current_control.isReset 清為 False，
    並同時把 current_control.isDone 清為 False（視為一次完整 Reset 流程）。
    """
    global current_control
    with _state_lock:
        snap = ControlResponse(**current_control.dict())
        if current_control.isReset:
            # one-shot reset：回傳 true 這一次之後就清掉
            current_control.isReset = False
            current_control.isDone = False   # reset 時順便解除 done 鎖
        return snap

# ---------- API ----------
@app.post("/telemetry")
def receive_telemetry(payload: TelemetryPayload):
    """
    Unity 每次傳遞相機資訊 + 兩張畫面（RGB / Depth）。回傳 current_control 作為控制指令。
    """
    global _last_rgb_jpeg, _last_depth_jpeg, _last_pose, _last_fov, _last_wh, _last_update_ts, _frame_count
    with _state_lock:
        _last_pose = payload.pose
        _last_fov = payload.fov
        _last_wh = (payload.width, payload.height)
        _last_update_ts = time.time()
        _frame_count += 1

        # 優先讀新欄位
        if payload.cam_rgb_b64:
            try:
                _last_rgb_jpeg = base64.b64decode(payload.cam_rgb_b64)
            except Exception:
                _last_rgb_jpeg = None

        if payload.cam_depth_b64:
            try:
                _last_depth_jpeg = base64.b64decode(payload.cam_depth_b64)
            except Exception:
                _last_depth_jpeg = None

        # 兼容舊欄位：若只送 image_b64，當成 RGB
        if payload.image_b64 and _last_rgb_jpeg is None:
            try:
                _last_rgb_jpeg = base64.b64decode(payload.image_b64)
            except Exception:
                _last_rgb_jpeg = None

    # 回傳快照（含 isReset one-shot 處理 & reset 時清 isDone）
    return _snapshot_and_fold_reset()

@app.post("/set_control")
def set_control(ctrl: ControlResponse):
    """
    設定控制值（含 apply/pose/fov 與 isDone/isReset）。
    - isReset=True：下一次 /telemetry 回傳 isReset=true 後，會自動把伺服器端 isReset->False，且 isDone->False。
    - isDone=True：會持續為 True，直到發生 reset。
    """
    global current_control
    with _state_lock:
        # 僅更新有提供的欄位，未提供的保留
        d = ctrl.dict()
        for k, v in d.items():
            # pydantic 預設給 None 的我們就不覆蓋（避免不小心清掉 pose/fov）
            if v is None and k in ("fov", "pose"):
                continue
            setattr(current_control, k, v)
    return {"ok": True, "applied": current_control.dict()}

@app.get("/control")
def get_control():
    with _state_lock:
        return current_control

# 快速端點（可用 curl 或網址列直接測）
@app.post("/done_on")
def done_on():
    with _state_lock:
        current_control.isDone = True
    return {"ok": True, "isDone": True}

@app.post("/done_off")
def done_off():
    with _state_lock:
        current_control.isDone = False
    return {"ok": True, "isDone": False}

@app.post("/reset_once")
def reset_once():
    with _state_lock:
        current_control.isReset = True
    return {"ok": True, "isReset": True}

# ---------- Image endpoints ----------
@app.get("/latest_rgb.jpg")
def latest_rgb():
    with _state_lock:
        if _last_rgb_jpeg is None:
            return Response(status_code=204)
        return Response(content=_last_rgb_jpeg, media_type="image/jpeg")

@app.get("/latest_depth.jpg")
def latest_depth():
    with _state_lock:
        if _last_depth_jpeg is None:
            return Response(status_code=204)
        return Response(content=_last_depth_jpeg, media_type="image/jpeg")

# 兼容舊版路徑：/latest.jpg 仍回傳 RGB
@app.get("/latest.jpg")
def latest_frame_compat():
    return latest_rgb()

@app.get("/latest")
def latest_meta():
    with _state_lock:
        if _last_update_ts is None:
            return JSONResponse({"ready": False})
        return JSONResponse({
            "ready": True,
            "pose": None if _last_pose is None else {
                "px": _last_pose.px, "py": _last_pose.py, "pz": _last_pose.pz,
                "rx": _last_pose.rx, "ry": _last_pose.ry, "rz": _last_pose.rz,
            },
            "fov": _last_fov,
            "width": _last_wh[0],
            "height": _last_wh[1],
            "updated_at": _last_update_ts,
            "frame_count": _frame_count
        })

# ---------- Simple viewer (RGB + Depth + Controls) ----------
@app.get("/viewer")
def viewer():
    html = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>Unity Camera Viewer (RGB + Depth)</title>
  <style>
    body { font-family: system-ui, -apple-system, Segoe UI, Roboto, "Noto Sans", Arial, "Microsoft JhengHei", sans-serif; margin: 0; background: #0b0f14; color: #eef2f6; }
    .wrap { display: grid; grid-template-columns: 2fr 2fr 1.2fr; gap: 16px; padding: 16px; }
    .card { background: #121821; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,.4); padding: 16px; }
    h1 { margin: 0 0 12px; font-size: 18px; font-weight: 600; }
    img.frame { width: 100%; border-radius: 8px; display: block; background: #0e131a; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .kv { background: #0e131a; border-radius: 8px; padding: 12px; }
    .kv b { display:block; font-size: 12px; color:#a8b3c7; margin-bottom: 6px;}
    .kv span { font-size: 16px;}
    .green { color: #7ee787; }
    .muted { color: #a8b3c7; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace; }
    .ctrl { margin-top: 12px; }
    button { background:#1f6feb; color:white; border:0; border-radius:8px; padding:8px 12px; cursor:pointer; }
    button.warn { background:#d29922; }
    button.danger { background:#f85149; }
    input { background:#0e131a; color:#eef2f6; border:1px solid #223043; border-radius:8px; padding:6px 8px; width:100%; }
    label { font-size:12px; color:#a8b3c7; display:block; margin-top:8px;}
    .cols { display:grid; grid-template-columns:1fr 1fr; gap:12px;}
    .pill { display:inline-block; font-size:12px; padding:2px 8px; border-radius:999px; background:#0e131a; border:1px solid #223043; margin-right:6px;}
  </style>
</head>
<body>
  <div class="wrap">
    <div class="card">
      <h1>RGB</h1>
      <img class="frame" id="img_rgb" src="/latest_rgb.jpg" alt="rgb">
    </div>
    <div class="card">
      <h1>Depth (Colorized)</h1>
      <img class="frame" id="img_depth" src="/latest_depth.jpg" alt="depth">
    </div>
    <div class="card">
      <h1>Telemetry</h1>
      <div class="row">
        <div class="kv"><b>Size</b><span id="size" class="mono muted">-</span></div>
        <div class="kv"><b>FOV</b><span id="fov" class="mono">-</span></div>
      </div>
      <div class="row" style="margin-top:8px;">
        <div class="kv"><b>Pos (px,py,pz)</b><span id="pos" class="mono">-</span></div>
        <div class="kv"><b>Rot (rx,ry,rz)</b><span id="rot" class="mono">-</span></div>
      </div>
      <div class="row" style="margin-top:8px;">
        <div class="kv"><b>Frames</b><span id="fc" class="mono">-</span></div>
        <div class="kv"><b>Updated</b><span id="ts" class="mono muted">-</span></div>
      </div>

      <div class="ctrl">
        <h1>Quick Control</h1>
        <div class="row">
          <div class="kv"><b>Status</b>
            <div><span class="pill" id="s_apply">apply: -</span>
                 <span class="pill" id="s_done">isDone: -</span>
                 <span class="pill" id="s_reset">isReset(next): -</span></div>
          </div>
        </div>

        <label>Set Position (px,py,pz)</label>
        <div class="row">
          <input id="px" placeholder="px">
          <input id="py" placeholder="py">
        </div>
        <div class="row" style="margin-top:6px;">
          <input id="pz" placeholder="pz">
          <input id="fov_in" placeholder="fov (opt)">
        </div>
        <div class="row" style="margin-top:6px;">
          <input id="rx" placeholder="rx (deg)">
          <input id="ry" placeholder="ry (deg)">
        </div>
        <div class="row" style="margin-top:6px;">
          <input id="rz" placeholder="rz (deg)">
          <button id="apply_btn">Apply</button>
        </div>

        <div class="row" style="margin-top:6px;">
          <button class="warn" id="done_on">Done ON</button>
          <button id="done_off">Done OFF</button>
        </div>
        <div class="row" style="margin-top:6px;">
          <button class="danger" id="reset_once">RESET once</button>
          <button id="clear">Clear Control</button>
        </div>
      </div>
    </div>
  </div>

<script>
let bust = 0;
function bustUrl(u){ bust=(bust+1)%1e9; return u+(u.includes("?")?"&":"?")+"_="+bust; }

async function tick(){
  try{
    const m = await (await fetch("/latest")).json();
    if(!m.ready){
      document.getElementById("size").textContent="-";
      document.getElementById("fov").textContent="-";
      document.getElementById("pos").textContent="-";
      document.getElementById("rot").textContent="-";
    }else{
      if(m.width && m.height){
        document.getElementById("size").textContent = m.width + "×" + m.height;
      }
      document.getElementById("fov").textContent = m.fov ?? "-";
      if(m.pose){
        const p=m.pose;
        document.getElementById("pos").textContent = `${p.px.toFixed(3)}, ${p.py.toFixed(3)}, ${p.pz.toFixed(3)}`;
        document.getElementById("rot").textContent = `${p.rx.toFixed(2)}, ${p.ry.toFixed(2)}, ${p.rz.toFixed(2)}`;
      }
      document.getElementById("fc").textContent = m.frame_count ?? "-";
      if(m.updated_at){
        const d = new Date(m.updated_at*1000);
        document.getElementById("ts").textContent = d.toLocaleTimeString();
      }
    }

    // 控制狀態
    const c = await (await fetch("/control")).json();
    document.getElementById("s_apply").textContent = "apply: " + (c.apply ? "true":"false");
    document.getElementById("s_done").textContent  = "isDone: " + (c.isDone ? "true":"false");
    document.getElementById("s_reset").textContent = "isReset(next): " + (c.isReset ? "true":"false");

    // 刷新兩張圖
    document.getElementById("img_rgb").src   = bustUrl("/latest_rgb.jpg");
    document.getElementById("img_depth").src = bustUrl("/latest_depth.jpg");
  }catch(e){ /* ignore */ }
}

async function applyControl(){
  const body = { apply: true, pose: {} };
  const g = id => document.getElementById(id).value.trim();
  const f = g("fov_in");
  ["px","py","pz","rx","ry","rz"].forEach(k=>{
    const v=g(k); if(v!=="") body.pose[k]=parseFloat(v);
  });
  if(Object.keys(body.pose).length===0) delete body.pose;
  if(f!=="") body.fov=parseFloat(f);
  await fetch("/set_control",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
}
async function clearControl(){
  await fetch("/set_control",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({apply:false, pose:null, fov:null})});
}
async function doneOn(){ await fetch("/done_on",{method:"POST"}); }
async function doneOff(){ await fetch("/done_off",{method:"POST"}); }
async function resetOnce(){ await fetch("/reset_once",{method:"POST"}); }

document.getElementById("apply_btn").addEventListener("click", applyControl);
document.getElementById("clear").addEventListener("click", clearControl);
document.getElementById("done_on").addEventListener("click", doneOn);
document.getElementById("done_off").addEventListener("click", doneOff);
document.getElementById("reset_once").addEventListener("click", resetOnce);

setInterval(tick, 250);
</script>
</body>
</html>
    """
    return HTMLResponse(html)

if __name__ == "__main__":
    port = 8000
    url = f"http://127.0.0.1:{port}/viewer"
    print(f"\n🚀 Server running at {url}")
    print("Press Ctrl+C to stop.\n")
    webbrowser.open(url)
    uvicorn.run("server:app", host="0.0.0.0", port=port, reload=False)
