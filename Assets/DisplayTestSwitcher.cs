using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 1枚のディスプレイで3画面(カメラ+Canvas)を切り替えて確認するテスト用スクリプト。
/// テストモード中は全て Display 1 に出力し、選択中の画面だけ表示する。
/// </summary>
public class DisplayTestSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class ScreenSet
    {
        public string label = "Screen";
        public Camera camera;
        public Canvas[] canvases;   // この画面に属するCanvas(任意)

        [HideInInspector] public int originalCameraDisplay;
        [HideInInspector] public int[] originalCanvasDisplays;
    }

    [Header("画面1〜3の設定")]
    public ScreenSet[] screens = new ScreenSet[3];

    [Header("テストモード")]
    [Tooltip("ONでテスト(1画面ずつ切替)。OFFで本番(Display 1/2/3に分配)")]
    public bool testMode = true;

    [Tooltip("ビルド後に実機が複数枚ある場合は自動でテストモードOFFにする")]
    public bool autoDisableIfMultiDisplay = true;

    [Header("操作")]
    public bool showOverlayLabel = true;

    int current = 0;
    bool initialized = false;
    public int Current => current;

    void Start()
    {
        // 元のTarget Displayを保存
        foreach (var s in screens)
        {
            if (s.camera != null) s.originalCameraDisplay = s.camera.targetDisplay;

            if (s.canvases != null)
            {
                s.originalCanvasDisplays = new int[s.canvases.Length];
                for (int i = 0; i < s.canvases.Length; i++)
                    if (s.canvases[i] != null)
                        s.originalCanvasDisplays[i] = s.canvases[i].targetDisplay;
            }
        }
        initialized = true;

        // ビルド後に複数ディスプレイが接続されていれば本番モードへ
        if (!Application.isEditor && autoDisableIfMultiDisplay && Display.displays.Length > 1)
            testMode = false;

        ApplyMode();
    }

    void Update()
    {
        if (!testMode) return;

        // 1/2/3キーで直接切替、Tab or 矢印キーで順送り
        if (GetKeyDown(1)) Show(0);
        else if (GetKeyDown(2)) Show(1);
        else if (GetKeyDown(3)) Show(2);
        else if (GetNextKeyDown()) Show((current + 1) % screens.Length);
        else if (GetPrevKeyDown()) Show((current - 1 + screens.Length) % screens.Length);

        // F1でテストモードのON/OFF(本番配分の確認用)
        if (GetToggleKeyDown())
        {
            testMode = false;
            ApplyMode();
        }
    }

    void OnValidate()
    {
        // Inspectorでチェックを変えたときに、実行中なら反映
        if (Application.isPlaying && initialized) ApplyMode();
    }

    // ---------- 切替ロジック ----------

    void ApplyMode()
    {
        if (testMode)
        {
            // 全て Display 1 に集約し、選択中のみ表示
            foreach (var s in screens)
            {
                if (s.camera != null) s.camera.targetDisplay = 0;
                if (s.canvases != null)
                    foreach (var c in s.canvases)
                        if (c != null) c.targetDisplay = 0;
            }
            Show(current);
        }
        else
        {
            // 本番配置に戻す(全て有効化 + 元のTarget Display)
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                if (s.camera != null)
                {
                    s.camera.targetDisplay = s.originalCameraDisplay;
                    s.camera.enabled = true;
                }
                if (s.canvases != null)
                    for (int j = 0; j < s.canvases.Length; j++)
                        if (s.canvases[j] != null)
                        {
                            s.canvases[j].targetDisplay = s.originalCanvasDisplays[j];
                            s.canvases[j].gameObject.SetActive(true);
                        }
            }
            // Display 2以降を有効化
            for (int i = 1; i < Display.displays.Length && i < 3; i++)
                Display.displays[i].Activate();
        }
    }

    public void Show(int index)
    {
        if (index < 0 || index >= screens.Length) return;
        current = index;

        for (int i = 0; i < screens.Length; i++)
        {
            bool active = (i == current);
            var s = screens[i];

            if (s.camera != null) s.camera.enabled = active;

            if (s.canvases != null)
                foreach (var c in s.canvases)
                    if (c != null) c.gameObject.SetActive(active);
        }
    }

    // ---------- 入力(旧Input Manager / 新Input System 両対応) ----------

    bool GetKeyDown(int n)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return false;
        return n == 1 ? k.digit1Key.wasPressedThisFrame
             : n == 2 ? k.digit2Key.wasPressedThisFrame
             : k.digit3Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Alpha0 + n);
#endif
    }

    bool GetNextKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.tabKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.RightArrow);
#endif
    }

    bool GetPrevKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && k.leftArrowKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.LeftArrow);
#endif
    }

    bool GetToggleKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && k.f1Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F1);
#endif
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || !testMode || screens.Length == 0) return;

        string name = screens[current].label;
        string text = $"[TEST] {current + 1}/{screens.Length}: {name}   (1/2/3 or ←/→/Tab)";
        GUI.Label(new Rect(10, 10, 600, 24), text);
    }
}