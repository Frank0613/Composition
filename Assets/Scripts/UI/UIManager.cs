using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    // -------- Singleton & Persist --------
    public static UIManager Instance { get; private set; }

    public SceneController sceneController;
    public DataStreamer dataStreamer;

    [Header("Streamer Setting & Reset")]
    public GameObject SettingUI;
    public Button SettingBtn;
    public Button ResetBtn;
    public TMP_Text Logtext;

    [Header("Setting UI")]
    public Button CloseBtn;
    public Button ConnectBtn;
    public TMP_InputField URL_input;
    public TMP_InputField Feq_input;      // send interval (seconds)
    public TMP_InputField width_input;
    public TMP_InputField height_input;
    public TMP_Dropdown Applymode;
    public TMP_Dropdown Lerpmode;

    public void ShowSettingUI() => SettingUI.SetActive(true);
    public void HideSettingUI() => SettingUI.SetActive(false);

    [System.Obsolete]
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [System.Obsolete]
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [System.Obsolete]
    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (sceneController == null) sceneController = FindObjectOfType<SceneController>(true);
        if (dataStreamer == null) dataStreamer = DataStreamer.Instance;

        // 重新綁 Reset 按鈕
        if (ResetBtn != null)
        {
            ResetBtn.onClick.RemoveAllListeners();
            ResetBtn.onClick.AddListener(() =>
            {
                if (sceneController != null) sceneController.ResetScene();
                else AppendLog("SceneController not found.");
            });
        }
    }

    [System.Obsolete]
    void Start()
    {
        HideSettingUI();
        SettingBtn.onClick.AddListener(() => { ShowSettingUI(); });
        CloseBtn.onClick.AddListener(() => { HideSettingUI(); });

        if (ResetBtn != null)
        {
            ResetBtn.onClick.RemoveAllListeners();
            ResetBtn.onClick.AddListener(() =>
            {
                if (sceneController != null) sceneController.ResetScene();
                else AppendLog("SceneController not found.");
            });
        }

        if (dataStreamer == null) dataStreamer = DataStreamer.Instance;
        if (dataStreamer != null)
        {
            dataStreamer.OnConnected += () => SetLog("Connected");
            dataStreamer.OnDisconnected += () => SetLog("Disconnected");
        }

        // 從 PlayerPrefs 還原輸入
        if (URL_input) URL_input.text = PlayerPrefs.GetString("ds_url", "http://127.0.0.1:8000");
        if (Feq_input) Feq_input.text = PlayerPrefs.GetFloat("ds_interval", 0.1f).ToString("0.###");
        if (width_input) width_input.text = PlayerPrefs.GetInt("ds_w", 640).ToString();
        if (height_input) height_input.text = PlayerPrefs.GetInt("ds_h", 360).ToString();

        // Dropdown 從 PlayerPrefs 還原並即時套用
        if (Applymode) Applymode.value = PlayerPrefs.GetInt("ds_apply_mode", 0);
        if (Lerpmode) Lerpmode.value = PlayerPrefs.GetInt("ds_lerp_mode", 0);
        ApplyUIToStreamer();

        if (Applymode) Applymode.onValueChanged.AddListener(_ =>
        {
            PlayerPrefs.SetInt("ds_apply_mode", Applymode.value);
            PlayerPrefs.Save();
            ApplyUIToStreamer();
        });

        if (Lerpmode) Lerpmode.onValueChanged.AddListener(_ =>
        {
            PlayerPrefs.SetInt("ds_lerp_mode", Lerpmode.value);
            PlayerPrefs.Save();
            ApplyUIToStreamer();
        });

        // Connect
        ConnectBtn.onClick.AddListener(OnClickConnect);
    }

    [System.Obsolete]
    private void OnClickConnect()
    {
        if (dataStreamer == null)
        {
            SetLog("No DataStreamer!");
            return;
        }

        // 先把 dropdown 套用到 streamer
        ApplyUIToStreamer();

        string url = URL_input != null ? URL_input.text.Trim() : "";
        float interval = ParseFloatSafe(Feq_input?.text, 0.1f);
        int w = ParseIntSafe(width_input?.text, 640);
        int h = ParseIntSafe(height_input?.text, 360);

        dataStreamer.Connect(url, interval, w, h);
        SetLog("Connecting...");
        HideSettingUI();
    }

    private void ApplyUIToStreamer()
    {
        if (dataStreamer == null) dataStreamer = DataStreamer.Instance;
        if (dataStreamer == null) return;

        if (Applymode)
            dataStreamer.applySpace = (Applymode.value == 0) ? ApplySpace.Absolute : ApplySpace.Delta;
        if (Lerpmode)
            dataStreamer.lerpMode = (Lerpmode.value == 0) ? LerpMode.Smooth : LerpMode.Instant;
    }

    private static float ParseFloatSafe(string s, float defVal)
    {
        if (string.IsNullOrWhiteSpace(s)) return defVal;
        if (float.TryParse(s, out var v)) return v;
        return defVal;
    }

    private static int ParseIntSafe(string s, int defVal)
    {
        if (string.IsNullOrWhiteSpace(s)) return defVal;
        if (int.TryParse(s, out var v)) return v;
        return defVal;
    }

    private void SetLog(string msg)
    {
        if (Logtext) Logtext.text = msg;
        else Debug.Log($"[UIManager] {msg}");
    }
    private void AppendLog(string msg)
    {
        if (Logtext) Logtext.text = (Logtext.text + "\n" + msg);
        else Debug.Log($"[UIManager] {msg}");
    }
}
