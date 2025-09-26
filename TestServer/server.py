import asyncio, json, base64, io
import websockets
from PIL import Image
import numpy as np

HOST = "0.0.0.0"
PORT = 8765

async def handle_client(ws):
    print("[WS] client connected")
    try:
        async for message in ws:
            # 1) 收到 Unity 單包 JSON
            data = json.loads(message)

            seq = data.get("seq")
            pos = data.get("position")
            rot = data.get("rotationEuler")
            fov = data.get("fov")
            w   = data.get("width")
            h   = data.get("height")
            b64 = data.get("imageJpegBase64")

            print(f"[IN] seq={seq} pos={pos} rot={rot} fov={fov} size={w}x{h} img={'Y' if b64 else 'N'}")

            # 2) 可選：解碼影像（測試 OK 後需要再打開）
            if b64:
                try:
                    img_bytes = base64.b64decode(b64)
                    img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                    np_img = np.array(img)  # H,W,3
                    # TODO: 這裡放 AI 推論
                except Exception as e:
                    print("[WARN] image decode failed:", e)

            # 3) 算出新的相機姿態（示範：只改 Yaw=180）
            resp = {
                "apply": True,
                "seq": seq,                 # 回帶 seq 以對帳
                "position": pos,            # 不改就回原值
                "rotationEuler": {"x": 0.0, "y": 45.0, "z": 0.0}
            }

            # 4) 回傳
            await ws.send(json.dumps(resp))
            print(f"[OUT] seq={seq} -> applied yaw=180")
    except websockets.ConnectionClosed as e:
        print(f"[WS] client disconnected: {e.code} {e.reason}")

async def main():
    # max_size=None 允許大訊息（影像 Base64）
    async with websockets.serve(
        lambda ws: handle_client(ws),
        HOST,
        PORT,
        max_size=None
    ):
        print(f"[WS] server started at ws://{HOST}:{PORT}")
        await asyncio.Future()  # run forever

if __name__ == "__main__":
    asyncio.run(main())