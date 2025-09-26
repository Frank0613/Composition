# server.py  (WebSocket + non-blocking JPEG decode + debounced keyboard + OpenCV viewer)
import asyncio
import base64
import io
import json
import os
import time
import threading
import queue

import websockets
from PIL import Image
import numpy as np
import cv2

HOST = "0.0.0.0"
PORT = 8765

# ===== 影像顯示 / 儲存設定 =====
DISPLAY_FPS = 15                 # OpenCV 視窗顯示頻率
RESIZE_FOR_VIEW = (640, 360)     # 顯示用縮圖尺寸；設為 None 則不縮
SAVE_LATEST = True               # 是否每幀覆寫存 latest.jpg
SAVE_EVERY = 0                   # >0 表示每 N 幀另存 frame_xxx.jpg；0 表示不另外存
SAVE_DIR = "frames"
os.makedirs(SAVE_DIR, exist_ok=True)

# ===== 姿態 offset（由鍵盤控制）======
pose_offset = {"yaw": 0.0, "z": 0.0}

# ===== 鍵盤去抖（秒）======
KEY_MIN_INTERVAL = 0.25  # 250ms 內同鍵不重複

# ===== OpenCV 顯示用佇列：只保留最新一張，避免塞爆 =====
frame_q = queue.Queue(maxsize=1)

# 全域計數
frame_counter = 0


def _decode_img_to_numpy(b64):
    """在背景執行緒解碼 JPEG → np.array(RGB)"""
    img_bytes = base64.b64decode(b64)
    img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
    return np.array(img)  # H, W, 3 (RGB)


def modify_pose(in_pos, in_rot, seq):
    """根據全域 offset 修改姿態；避免 None"""
    out_pos = dict(in_pos) if in_pos else {"x": 0.0, "y": 0.0, "z": 0.0}
    out_rot = dict(in_rot) if in_rot else {"x": 0.0, "y": 0.0, "z": 0.0}

    out_rot["y"] = (float(out_rot.get("y", 0.0)) + pose_offset["yaw"]) % 360.0
    out_pos["z"] = float(out_pos.get("z", 0.0)) + pose_offset["z"]
    return out_pos, out_rot


def gui_thread(display_fps=DISPLAY_FPS, window_name="Unity Stream", resize_to=RESIZE_FOR_VIEW):
    """OpenCV 顯示執行緒（有界佇列，只顯示最新影像）"""
    interval = 1.0 / max(1, display_fps)
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    last = 0.0
    while True:
        try:
            img = frame_q.get(timeout=1.0)  # RGB
            img_bgr = cv2.cvtColor(img, cv2.COLOR_RGB2BGR)
            if resize_to:
                img_bgr = cv2.resize(img_bgr, resize_to)
            now = time.time()
            if now - last >= interval:
                cv2.imshow(window_name, img_bgr)
                last = now
            # 必須呼叫 waitKey 才會更新視窗；q 可關
            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
        except queue.Empty:
            pass
        except Exception as e:
            print("[GUI] error:", e)
            time.sleep(0.05)
    cv2.destroyAllWindows()


def keyboard_thread():
    """Windows 友善鍵盤監聽（msvcrt）；R/T 去抖單次觸發"""
    print("[KEYBOARD] Press R (yaw +10) / T (z -0.1). One press = one change. (Ctrl+C to quit)")
    try:
        import msvcrt  # Windows only
    except ImportError:
        print("[KEYBOARD] msvcrt not available — keyboard disabled.")
        return

    last_time = {"R": 0.0, "T": 0.0}
    global pose_offset

    while True:
        if msvcrt.kbhit():
            # 清空同批次事件，只保留最後一鍵
            last_key = None
            while msvcrt.kbhit():
                ch = msvcrt.getwch()
                if ch:
                    last_key = ch.upper()

            if last_key in ("R", "T"):
                now = time.time()
                if now - last_time[last_key] >= KEY_MIN_INTERVAL:
                    last_time[last_key] = now
                    if last_key == "R":
                        pose_offset["yaw"] += 10.0
                        print(f"[KEYBOARD] R → yaw offset = {pose_offset['yaw']}")
                    elif last_key == "T":
                        pose_offset["z"] -= 0.1
                        print(f"[KEYBOARD] T → z offset = {pose_offset['z']:.2f}")
        time.sleep(0.01)


async def handle_client(ws):
    """處理 Unity 端傳來的單包 JSON，回傳修改後的姿態"""
    print("[WS] client connected")
    loop = asyncio.get_running_loop()
    last_print = time.time()
    recv_count = 0

    global frame_counter

    try:
        async for message in ws:
            recv_count += 1
            data = json.loads(message)

            seq = data.get("seq")
            pos = data.get("position")
            rot = data.get("rotationEuler")
            w   = data.get("width")
            h   = data.get("height")
            b64 = data.get("imageJpegBase64")

            # 背景執行緒處理影像
            np_img, saved = None, False
            if b64:
                try:
                    np_img = await loop.run_in_executor(None, _decode_img_to_numpy, b64)
                    frame_counter += 1

                    # 丟到顯示佇列（只保留最新）
                    try:
                        frame_q.put_nowait(np_img)
                    except queue.Full:
                        try:
                            _ = frame_q.get_nowait()  # 丟掉舊的
                        except queue.Empty:
                            pass
                        frame_q.put_nowait(np_img)

                    # 可選：存檔確認
                    if SAVE_LATEST:
                        Image.fromarray(np_img).save(os.path.join(SAVE_DIR, "latest.jpg"), "JPEG", quality=90)
                        saved = True
                    if SAVE_EVERY and (frame_counter % SAVE_EVERY == 0):
                        Image.fromarray(np_img).save(
                            os.path.join(SAVE_DIR, f"frame_{frame_counter:06d}.jpg"),
                            "JPEG", quality=90
                        )
                        saved = True
                except Exception as e:
                    print("[WARN] image decode failed:", e)

            # 修改姿態並回傳
            out_pos, out_rot = modify_pose(pos, rot, seq)
            resp = {"apply": True, "seq": seq, "position": out_pos, "rotationEuler": out_rot}
            await ws.send(json.dumps(resp))

            # 節流 log（每 0.5 秒）
            now = time.time()
            if (now - last_print) >= 0.5:
                fps = recv_count / (now - last_print)
                print(f"[IN] seq={seq} size={w}x{h} img={'Y' if b64 else 'N'} | recvFPS={fps:.1f}")
                if np_img is not None:
                    print(f"[IMG] frame={frame_counter} shape={np_img.shape} saved={'Y' if saved else 'N'} -> {os.path.join(SAVE_DIR,'latest.jpg') if SAVE_LATEST else ''}")
                print(f"[OUT] seq={seq} -> pos={out_pos} rot={out_rot} | offset(yaw={pose_offset['yaw']}, z={pose_offset['z']:.2f})")
                last_print = now
                recv_count = 0

    except websockets.ConnectionClosed as e:
        print(f"[WS] client disconnected: {e.code} {e.reason}")
    except Exception as e:
        print("[WS] handler error:", e)


async def main():
    # 啟動鍵盤與 GUI 執行緒
    t_kb = threading.Thread(target=keyboard_thread, daemon=True)
    t_kb.start()
    t_gui = threading.Thread(target=gui_thread, daemon=True)
    t_gui.start()

    async with websockets.serve(
        handle_client, HOST, PORT,
        max_size=None, max_queue=None,
        ping_interval=20, ping_timeout=20,
    ):
        print(f"[WS] server started at ws://{HOST}:{PORT}")
        await asyncio.Future()  # run forever


if __name__ == "__main__":
    asyncio.run(main())
