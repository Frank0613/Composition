using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


[Serializable] class Vec3 { public float x, y, z; public Vec3() { } public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; } public Vector3 ToV3() => new Vector3(x, y, z); }
[Serializable]
class PacketOut
{
    public long seq;
    public long timestampMs;
    public Vec3 position;
    public Vec3 rotationEuler;
    public float fov;
    public int width, height;
    public string imageJpegBase64;
}
[Serializable]
class PacketIn
{
    public bool apply;
    public long seq;
    public Vec3 position;
    public Vec3 rotationEuler;
}



public class DataStreamer : MonoBehaviour
{
    [Header("Refs")]
    public Camera targetCamera;
    private CameraController cameraController;
    [Header("WebSocket")]
    public string wsUrl = "ws://127.0.0.1:8765";
    [Range(1, 60)] public int sendFPS = 15;

    [Header("Capture")]
    public int captureWidth = 640;
    public int captureHeight = 360;
    [Range(1, 100)] public int jpegQuality = 70;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private WaitForSeconds _wait;
    private long _seq = 0;
    private readonly ConcurrentQueue<Action> _mainJobs = new ConcurrentQueue<Action>();
    Task _inflightSend;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        cameraController = targetCamera.GetComponent<CameraController>();
        if (!cameraController)
        {
            Debug.LogError("CameraController not find");
            enabled = false;
        }

        _wait = new WaitForSeconds(1f / Mathf.Max(1, sendFPS));
    }
    async void OnEnable()
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        try
        {
            var uri = new Uri(wsUrl);
            await _ws.ConnectAsync(uri, _cts.Token);
            Debug.Log("[WS] Connected");
        }
        catch (Exception e)
        {
            Debug.LogError("[WS] Connect failed: " + e.Message);
            enabled = false; return;
        }

        _ = Task.Run(ReceiveLoop);
        StartCoroutine(SendLoop());
    }
    void Update()
    {
        while (_mainJobs.TryDequeue(out var job)) job?.Invoke();
    }

    IEnumerator SendLoop()
    {
        while (true)
        {
            yield return _wait;
            if (_ws == null || _ws.State != WebSocketState.Open) continue;

            // 丟幀策略：上一個送出還沒完成就跳過
            if (_inflightSend != null && !_inflightSend.IsCompleted) continue;

            string b64 = cameraController.GetCameraScreen(captureWidth, captureHeight, jpegQuality);

            var t = targetCamera.transform;
            var packet = new PacketOut
            {
                seq = ++_seq,
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                position = new Vec3(t.position),
                rotationEuler = new Vec3(t.eulerAngles),
                fov = targetCamera.fieldOfView,
                width = captureWidth,
                height = captureHeight,
                imageJpegBase64 = b64
            };

            var buffer = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            var seg = new ArraySegment<byte>(buffer);
            _inflightSend = _ws.SendAsync(seg, WebSocketMessageType.Text, true, _cts.Token);
            // 不等待，下一圈再看它是否完成
        }
    }
    async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 1024]; // 1MB
        try
        {
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                int offset = 0;
                WebSocketReceiveResult result;
                do
                {
                    var seg = new ArraySegment<byte>(buffer, offset, buffer.Length - offset);
                    result = await _ws.ReceiveAsync(seg, _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", _cts.Token);
                        Debug.Log("[WS] Closed by server");
                        return;
                    }

                    offset += result.Count;
                    if (offset >= buffer.Length)
                    {
                        Debug.LogWarning("[WS] Message too large");
                        break;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text && offset > 0)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, offset);
                    try
                    {
                        var inbound = JsonUtility.FromJson<PacketIn>(msg);
                        if (inbound != null && inbound.apply)
                        {
                            _mainJobs.Enqueue(() =>
                            {
                                if (inbound.position != null) cameraController.SetCameraPosition(inbound.position.ToV3());
                                if (inbound.rotationEuler != null) cameraController.SetCameraEulerAngles(inbound.rotationEuler.ToV3());
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[WS] Parse error: " + ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning("[WS] ReceiveLoop error: " + e.Message);
        }
    }

    async void OnDisable()
    {
        try
        {
            _cts?.Cancel();
            if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived))
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
        }
        catch (Exception e) { Debug.LogWarning("[WS] Close error: " + e.Message); }
        finally
        {
            _ws?.Dispose(); _ws = null;
            _cts?.Dispose(); _cts = null;
        }
    }
}
