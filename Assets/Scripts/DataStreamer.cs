using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

public enum ApplySpace { Absolute, Delta }
public enum LerpMode { Instant, Smooth }

public class DataStreamer : MonoBehaviour
{
    // -------- Singleton & Persist --------
    public static DataStreamer Instance { get; private set; }

    [Header("Refs")]
    public CameraController cameraController;
    public CameraScreen depthCameraController;
    public SceneController sceneController;

    [Header("Apply Mode")]
    public ApplySpace applySpace = ApplySpace.Absolute;
    public LerpMode lerpMode = LerpMode.Instant;

    [Header("Server")]
    public string serverBaseUrl = "http://127.0.0.1:8000";
    public string telemetryEndpoint = "/telemetry";
    public float sendInterval = 0.1f;

    [Header("Screen")]
    public int imageWidth = 640;
    public int imageHeight = 360;
    [Range(1, 100)]
    public int jpegQuality = 70;

    [Header("Debug")]
    public bool logEveryResponse = false;
    public bool logPickedCameras = true;

    // 連線事件
    public event Action OnConnected;
    public event Action OnDisconnected;

    private string _telemetryUrl;
    private Coroutine _loopCo;
    private bool _shouldRun = false;   // 按了 Connect 後維持 true（Reset 也不變）
    private bool _connected = false;
    private bool _doneHold = false;

    // ---------- Lifecycle ----------
    [Obsolete]
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        TryRebindRefs();
        BuildUrl();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [Obsolete]
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnEnable()
    {
        SceneController.OnSceneReset += HandleSceneReset;
    }
    void OnDisable()
    {
        SceneController.OnSceneReset -= HandleSceneReset;
    }

    [Obsolete]
    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {

        TryRebindRefs();
    }

    private void HandleSceneReset()
    {
        _doneHold = false; // 解除 isDone

    }

    private void BuildUrl()
    {
        _telemetryUrl = (serverBaseUrl ?? "").TrimEnd('/') + telemetryEndpoint;
    }

    // ---------- Rebind ----------
    [Obsolete]
    private void TryRebindRefs()
    {
        // 主相機（含 CameraController）
        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>(true);

        // depth：優先在主相機底下找子相機
        if (depthCameraController == null && cameraController != null)
        {
            var childScreens = cameraController.GetComponentsInChildren<CameraScreen>(true);
            // 排除主相機自己（CameraController 也繼承自 CameraScreen）
            depthCameraController = childScreens
                .FirstOrDefault(s => s != null && s != (CameraScreen)cameraController);
        }

        // 如果主相機下沒有，就退而求其次（全場景找第一個不是主相機的 CameraScreen）
        if (depthCameraController == null)
        {
            var allScreens = FindObjectsOfType<CameraScreen>(true);
            depthCameraController = allScreens
                .FirstOrDefault(s => s != null && (cameraController == null || s.gameObject != cameraController.gameObject));
        }

        // SceneController
        if (sceneController == null)
            sceneController = FindObjectOfType<SceneController>(true);

        if (logPickedCameras)
        {
            Debug.Log($"[DataStreamer] RGB={cameraController?.name ?? "null"}, DEPTH={depthCameraController?.name ?? "null"}");
        }

        if (cameraController == null)
            Debug.LogWarning("[DataStreamer] Rebind: CameraController (RGB) not found.");
        if (depthCameraController == null)
            Debug.LogWarning("[DataStreamer] Rebind: Depth CameraScreen not found (will send RGB only).");
    }

    // ---------- Public API ----------
    [Obsolete]
    public void Connect(string url, float interval, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(url)) serverBaseUrl = url;
        if (interval > 0f) sendInterval = interval;
        if (width > 0) imageWidth = width;
        if (height > 0) imageHeight = height;

        BuildUrl();

        // 保存到 PlayerPrefs
        PlayerPrefs.SetString("ds_url", serverBaseUrl);
        PlayerPrefs.SetFloat("ds_interval", sendInterval);
        PlayerPrefs.SetInt("ds_w", imageWidth);
        PlayerPrefs.SetInt("ds_h", imageHeight);
        PlayerPrefs.Save();

        // 重新啟動 loop
        if (_loopCo != null) StopCoroutine(_loopCo);
        _shouldRun = true;
        _connected = false;
        TryRebindRefs();
        _loopCo = StartCoroutine(StreamLoop());
    }

    public void Disconnect()
    {
        _shouldRun = false;
        if (_loopCo != null)
        {
            StopCoroutine(_loopCo);
            _loopCo = null;
        }
        if (_connected)
        {
            _connected = false;
            OnDisconnected?.Invoke();
        }
    }

    public bool IsConnected() => _connected;
    private ApplySpace GetApplySpace()
    {
        var ui = UIManager.Instance;
        if (ui != null && ui.Applymode != null)
        {
            var idx = ui.Applymode.value;
            var label = ui.Applymode.options != null && ui.Applymode.options.Count > idx
                ? (ui.Applymode.options[idx].text ?? "").Trim().ToLowerInvariant()
                : "";

            if (label.Contains("absolute") || label.Contains("絕對")) return ApplySpace.Absolute;
            if (label.Contains("delta") || label.Contains("增量") || label.Contains("相對")) return ApplySpace.Delta;

            // Fallback：索引 0 當 Absolute，1 當 Delta
            return (idx == 0) ? ApplySpace.Absolute : ApplySpace.Delta;
        }
        return applySpace;
    }


    private LerpMode GetLerpMode()
    {
        var ui = UIManager.Instance;
        if (ui != null && ui.Lerpmode != null)
        {
            var idx = ui.Lerpmode.value;
            var label = ui.Lerpmode.options != null && ui.Lerpmode.options.Count > idx
                ? (ui.Lerpmode.options[idx].text ?? "").Trim().ToLowerInvariant()
                : "";

            if (label.Contains("smooth") || label.Contains("平滑"))
                return LerpMode.Smooth;
            if (label.Contains("instant") || label.Contains("瞬移") || label.Contains("立即"))
                return LerpMode.Instant;

            // 萬一文字沒有符合，就 fallback：索引 0 視為 Smooth、索引 1 視為 Instant
            return (idx == 0) ? LerpMode.Smooth : LerpMode.Instant;
        }

        // 沒有 UI 時，退回欄位值
        return lerpMode;
    }

    // ---------- Loop ----------
    [Obsolete]
    IEnumerator StreamLoop()
    {
        var wait = new WaitForSeconds(sendInterval);
        while (_shouldRun)
        {
            bool ok = true;
            yield return StartCoroutine(SendOnce(success => ok = success));
            if (!ok)
            {
                _shouldRun = false; // 視為斷線並停掉
                yield break;
            }
            wait = new WaitForSeconds(sendInterval);
            yield return wait;
        }
    }

    // ---------- One tick ----------
    [Obsolete]
    IEnumerator SendOnce(Action<bool> onDone)
    {
        if (cameraController == null)
        {
            TryRebindRefs();
            if (cameraController == null) { onDone?.Invoke(true); yield break; }
        }

        Vector3 pos = cameraController.GetCameraPosition();
        Vector3 eul = cameraController.GetCameraEulerAngles();
        float fov = cameraController.GetCameraFOV();

        string rgb_b64 = cameraController.GetCameraScreen(imageWidth, imageHeight, jpegQuality);

        string depth_b64 = null;
        if (depthCameraController != null && depthCameraController != (CameraScreen)cameraController)
        {
            depth_b64 = depthCameraController.GetCameraScreen(imageWidth, imageHeight, jpegQuality);
        }

        var payload = new JObject
        {
            ["pose"] = new JObject
            {
                ["px"] = pos.x,
                ["py"] = pos.y,
                ["pz"] = pos.z,
                ["rx"] = eul.x,
                ["ry"] = eul.y,
                ["rz"] = eul.z
            },
            ["fov"] = fov,
            ["width"] = imageWidth,
            ["height"] = imageHeight
        };
        if (!string.IsNullOrEmpty(rgb_b64)) payload["cam_rgb_b64"] = rgb_b64;
        if (!string.IsNullOrEmpty(depth_b64)) payload["cam_depth_b64"] = depth_b64;

        string json = payload.ToString(Newtonsoft.Json.Formatting.None);

        // (2) 送出
        using (var req = new UnityWebRequest(_telemetryUrl, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[DataStreamer] Telemetry error ({req.responseCode}): {req.error}");
                if (_connected)
                {
                    _connected = false;
                    OnDisconnected?.Invoke();
                }
                onDone?.Invoke(false);
                yield break;
            }


            if (!_connected)
            {
                _connected = true;
                OnConnected?.Invoke();
            }

            string respText = req.downloadHandler.text;
            if (logEveryResponse) Debug.Log($"[DataStreamer] Resp: {respText}");


            try
            {
                var j = JObject.Parse(respText);

                bool isReset = j.Value<bool?>("isReset") == true;
                bool isDone = j.Value<bool?>("isDone") == true;

                if (isReset)
                {
                    if (sceneController == null) sceneController = FindObjectOfType<SceneController>(true);
                    if (sceneController != null) sceneController.ResetScene();
                    else Debug.LogWarning("[DataStreamer] isReset=true，但 SceneController 未綁定。");
                    onDone?.Invoke(true);
                    yield break;
                }

                if (isDone) _doneHold = true;
                if (_doneHold) { onDone?.Invoke(true); yield break; }

                bool apply = j.Value<bool?>("apply") == true;
                if (!apply) { onDone?.Invoke(true); yield break; }

                var poseObj = j["pose"] as JObject;

                Vector3 curPos = cameraController.GetCameraPosition();
                Vector3 curRot = cameraController.GetCameraEulerAngles();

                var modeSpace = GetApplySpace();
                var modeLerp = GetLerpMode();

                Vector3 targetPos = curPos;
                Vector3 targetRot = curRot;

                if (poseObj != null)
                {
                    float? px = poseObj.Value<float?>("px");
                    float? py = poseObj.Value<float?>("py");
                    float? pz = poseObj.Value<float?>("pz");
                    float? rx = poseObj.Value<float?>("rx");
                    float? ry = poseObj.Value<float?>("ry");
                    float? rz = poseObj.Value<float?>("rz");

                    bool hasP = px.HasValue || py.HasValue || pz.HasValue;
                    bool hasR = rx.HasValue || ry.HasValue || rz.HasValue;

                    if (hasP || hasR)
                    {
                        if (px.HasValue)
                            targetPos.x = (modeSpace == ApplySpace.Absolute) ? px.Value : curPos.x + px.Value;
                        if (py.HasValue)
                            targetPos.y = (modeSpace == ApplySpace.Absolute) ? py.Value : curPos.y + py.Value;
                        if (pz.HasValue)
                            targetPos.z = (modeSpace == ApplySpace.Absolute) ? pz.Value : curPos.z + pz.Value;

                        if (rx.HasValue)
                            targetRot.x = (modeSpace == ApplySpace.Absolute) ? rx.Value : curRot.x + rx.Value;
                        if (ry.HasValue)
                            targetRot.y = (modeSpace == ApplySpace.Absolute) ? ry.Value : curRot.y + ry.Value;
                        if (rz.HasValue)
                            targetRot.z = (modeSpace == ApplySpace.Absolute) ? rz.Value : curRot.z + rz.Value;

                        cameraController.SetCameraPosition(targetPos, ApplySpace.Absolute, modeLerp);
                        cameraController.SetCameraEulerAngles(targetRot, ApplySpace.Absolute, modeLerp);
                    }
                }

                if (j.ContainsKey("fov") && j["fov"]?.Type != JTokenType.Null)
                {
                    float fovVal = j.Value<float>("fov");
                    cameraController.SetCameraFOV(fovVal);
                }

                onDone?.Invoke(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DataStreamer] Parse/apply failed: {ex.Message}");
                onDone?.Invoke(true);
            }
        }
    }
}
