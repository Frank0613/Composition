using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


[Serializable]
class Vec3
{
    public float x, y, z;
    public Vec3() { }
    public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToV3() => new Vector3(x, y, z);
}

// Packages to be sent out
[Serializable]
class PacketOut
{
    public long seq;
    public long timestampMs;
    public Vec3 position;
    public Vec3 rotationEuler;
    public float fov; // field of view
    public int width, height;
    public string imageJpegBase64;
}

// Packages sent back by the server
[Serializable]
class PacketIn
{
    public bool apply; // respone
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

    private ClientWebSocket _ws; // WebSocket Connection Object
    private CancellationTokenSource _cts; // Control cancellation, interrupt WebSocket at any time
    private WaitForSeconds _wait; // FPS Controller
    private long _seq = 0; // Packet sequence number
    private readonly ConcurrentQueue<Action> _mainJobs = new ConcurrentQueue<Action>(); // Main thread task queue
    Task _inflightSend; // Avoid packet stuck

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

        _ = Task.Run(ReceiveLoop); // Open ReceiveLoop
        StartCoroutine(SendLoop()); // Open SendLoop
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

            // Skip the previous one if it is not finished yet
            if (_inflightSend != null && !_inflightSend.IsCompleted) continue;

            string b64 = cameraController.GetCameraScreen(captureWidth, captureHeight, jpegQuality);
            Vector3 _pos = cameraController.GetCameraPosition();
            Vector3 _eul = cameraController.GetCameraEulerAngles();
            float _fov = cameraController.GetCameraFOV();

            var packet = new PacketOut
            {
                seq = ++_seq,
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                position = new Vec3(_pos),
                rotationEuler = new Vec3(_eul),
                fov = _fov,
                width = captureWidth,
                height = captureHeight,
                imageJpegBase64 = b64
            };

            var buffer = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)); // Convert PacketOut to Json file
            var seg = new ArraySegment<byte>(buffer);
            _inflightSend = _ws.SendAsync(seg, WebSocketMessageType.Text, true, _cts.Token); // Send this data asynchronously via WebSocket and save the returned Task to _inflightSend

        }
    }
    async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 1024]; // 1MB
        try
        {
            // keep receiving
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                int offset = 0;
                WebSocketReceiveResult result;
                do
                {
                    var seg = new ArraySegment<byte>(buffer, offset, buffer.Length - offset);
                    result = await _ws.ReceiveAsync(seg, _cts.Token);

                    // If server closed
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", _cts.Token);
                        Debug.Log("[WS] Closed by server");
                        return;
                    }

                    // If msg too large -> quit
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
                        // Receive Json -> PacketIn msg
                        var inbound = JsonUtility.FromJson<PacketIn>(msg);
                        // If apply, throw into mainJob
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

    // Close the WebSocket and release resources
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
