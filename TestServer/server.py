from typing import Optional
from fastapi import FastAPI
from pydantic import BaseModel
from fastapi.responses import Response, HTMLResponse, JSONResponse
import base64
import threading
import time
import uvicorn
import webbrowser

app = FastAPI(title="Unity Camera Data Server (with Viewer)")

# ---------- Models ----------
class PoseData(BaseModel):
    px: float; py: float; pz: float
    rx: float; ry: float; rz: float

class TelemetryPayload(BaseModel):
    pose: PoseData
    fov: float
    image_b64: Optional[str] = None
    width: Optional[int] = None
    height: Optional[int] = None

class ControlPose(BaseModel):
    px: Optional[float] = None; py: Optional[float] = None; pz: Optional[float] = None
    rx: Optional[float] = None; ry: Optional[float] = None; rz: Optional[float] = None

class ControlResponse(BaseModel):
    apply: bool = False
    fov: Optional[float] = None
    pose: Optional[ControlPose] = None

# ---------- In-memory states ----------
current_control = ControlResponse(apply=False, pose=ControlPose(), fov=None)

_state_lock = threading.Lock()
_last_jpeg: Optional[bytes] = None
_last_pose: Optional[PoseData] = None
_last_fov: Optional[float] = None
_last_wh = (None, None)
_last_update_ts: Optional[float] = None
_frame_count: int = 0

# ---------- API ----------
@app.post("/telemetry")
def receive_telemetry(payload: TelemetryPayload):
    """
    Unity 每次傳遞相機資訊 + 畫面。伺服器回傳 current_control，讓 Unity 立即套用。
    """
    global _last_jpeg, _last_pose, _last_fov, _last_wh, _last_update_ts, _frame_count
    with _state_lock:
        _last_pose = payload.pose
        _last_fov = payload.fov
        _last_wh = (payload.width, payload.height)
        _last_update_ts = time.time()
        _frame_count += 1
        if payload.image_b64:
            try:
                _last_jpeg = base64.b64decode(payload.image_b64)
            except Exception:
                # 影像壞掉就忽略，但其他資料仍可顯示
                _last_jpeg = None
    return current_control

@app.post("/set_control")
def set_control(ctrl: ControlResponse):
    """
    從外部設定新的控制命令（只要填你要改的欄位即可，Unity 端會做部份套用）。
    """
    global current_control
    current_control = ctrl
    return {"ok": True, "applied": current_control.dict()}

@app.get("/control")
def get_control():
    return current_control

# ---------- Viewer helpers ----------
@app.get("/latest.jpg")
def latest_frame():
    """
    回傳最新一張相機 JPEG 畫面（給 <img> 使用）。
    """
    with _state_lock:
        if _last_jpeg is None:
            return Response(status_code=204)
        return Response(content=_last_jpeg, media_type="image/jpeg")

@app.get("/latest")
def latest_meta():
    """
    回傳最新姿態/FOV/時間戳/幀數等資訊（給 viewer JS 輪詢）。
    """
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

@app.get("/viewer")
def viewer():
    """
    簡單的可視化儀表板：左側是即時影像，右側顯示 pose/fov/幀數。
    """
    html = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>Unity Camera Viewer</title>
  <style>
    body { font-family: system-ui, -apple-system, Segoe UI, Roboto, "Noto Sans", Arial, "Microsoft JhengHei", sans-serif; margin: 0; background: #0b0f14; color: #eef2f6; }
    .wrap { display: grid; grid-template-columns: 2fr 1fr; gap: 16px; padding: 16px; }
    .card { background: #121821; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,.4); padding: 16px; }
    h1 { margin: 0 0 12px; font-size: 18px; font-weight: 600; }
    #img { width: 100%; border-radius: 8px; display: block; background: #0e131a; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .kv { background: #0e131a; border-radius: 8px; padding: 12px; }
    .kv b { display:block; font-size: 12px; color:#a8b3c7; margin-bottom: 6px;}
    .kv span { font-size: 16px;}
    .green { color: #7ee787; }
    .muted { color: #a8b3c7; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace; }
    .ctrl { margin-top: 12px; }
    button { background:#1f6feb; color:white; border:0; border-radius:8px; padding:8px 12px; cursor:pointer; }
    input { background:#0e131a; color:#eef2f6; border:1px solid #223043; border-radius:8px; padding:6px 8px; width:100%; }
    label { font-size:12px; color:#a8b3c7; display:block; margin-top:8px;}
  </style>
</head>
<body>
  <div class="wrap">
    <div class="card">
      <h1>Live Frame</h1>
      <img id="img" src="/latest.jpg" alt="frame">
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
          <button id="apply">Apply</button>
        </div>
        <div class="row" style="margin-top:6px;">
          <button id="clear">Clear Control</button>
        </div>
      </div>
    </div>
  </div>

<script>
let lastBust = 0;

function bustUrl(u) {
  lastBust = (lastBust + 1) % 1e9;
  const sep = u.includes("?") ? "&" : "?";
  return u + sep + "_=" + lastBust;
}

async function tick() {
  try {
    const r = await fetch("/latest");
    const j = await r.json();
    if (!j.ready) {
      document.getElementById("size").textContent = "-";
      document.getElementById("fov").textContent = "-";
      document.getElementById("pos").textContent = "-";
      document.getElementById("rot").textContent = "-";
      return;
    }
    if (j.width && j.height) {
      document.getElementById("size").textContent = j.width + "×" + j.height;
    }
    document.getElementById("fov").textContent = j.fov ?? "-";
    if (j.pose) {
      const p = j.pose;
      document.getElementById("pos").textContent = `${p.px.toFixed(3)}, ${p.py.toFixed(3)}, ${p.pz.toFixed(3)}`;
      document.getElementById("rot").textContent = `${p.rx.toFixed(2)}, ${p.ry.toFixed(2)}, ${p.rz.toFixed(2)}`;
    }
    document.getElementById("fc").textContent = j.frame_count ?? "-";
    if (j.updated_at) {
      const d = new Date(j.updated_at * 1000);
      document.getElementById("ts").textContent = d.toLocaleTimeString();
    }
    // 刷新影像（cache-busting）
    const img = document.getElementById("img");
    img.src = bustUrl("/latest.jpg");
  } catch (e) {
    // ignore
  }
}

async function applyControl() {
  const body = { apply: true, pose: {}, };
  const g = id => document.getElementById(id).value.trim();
  const f = g("fov_in");
  const fields = ["px","py","pz","rx","ry","rz"];
  fields.forEach(k => {
    const v = g(k);
    if (v !== "") body.pose[k] = parseFloat(v);
  });
  if (f !== "") body.fov = parseFloat(f);
  await fetch("/set_control", { method:"POST", headers:{ "Content-Type":"application/json" }, body: JSON.stringify(body) });
}

async function clearControl() {
  await fetch("/set_control", { method:"POST", headers:{ "Content-Type":"application/json" }, body: JSON.stringify({apply:false})});
}

document.getElementById("apply").addEventListener("click", applyControl);
document.getElementById("clear").addEventListener("click", clearControl);

setInterval(tick, 250); // 4Hz viewer refresh，Unity 可更高頻傳
</script>
</body>
</html>
    """
    return HTMLResponse(html)

if __name__ == "__main__":
    import uvicorn
    import webbrowser

    port = 8000
    url = f"http://127.0.0.1:{port}/viewer"

    print(f"\n🚀 Server running at {url}")
    print("Press Ctrl+C to stop.\n")

    # 自動開啟瀏覽器
    webbrowser.open(url)

    uvicorn.run("server:app", host="0.0.0.0", port=port, reload=False)
