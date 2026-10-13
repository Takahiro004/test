using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 1枚のディスプレイで3画面(カメラ+Canvas)を確認するテスト用スクリプト。
/// 分割表示ON: 3画面を横に並べて同時表示。
/// 分割表示OFF: 1画面ずつ切り替えて表示。
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
    [Tooltip("ONでテスト(1枚のモニターに集約)。OFFで本番(Display 1/2/3に分配)")]
    public bool testMode = true;

    [Tooltip("ビルド後に実機が複数枚ある場合は自動でテストモードOFFにする")]
    public bool autoDisableIfMultiDisplay = true;

    [Header("同時表示(1枚のモニターで3画面を並べる)")]
    [Tooltip("ONで3画面を横に並べて同時表示。OFFで1画面ずつ切替(F2で切替)")]
    public bool splitView = true;

    [Tooltip("1画面ぶんの基本の表示範囲(Orthographic Size)。商品が収まる大きさに合わせる")]
    public float baseOrthoSize = 5f;

    [Tooltip("本番の1画面のアスペクト比(16:9なら1.778)。分割で縦長になった分、表示範囲を広げる")]
    public float targetAspect = 16f / 9f;

    [Header("操作")]
    public bool showOverlayLabel = true;

    int current = 0;
    bool initialized = false;

    /// <summary>現在選択中の画面番号(0始まり)。他のスクリプトから読むために公開。</summary>
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

        // 1/2/3キーで画面選択、Tabで順送り(分割表示OFFのときに表示が切り替わる)
        if (GetKeyDown(1)) Show(0);
        else if (GetKeyDown(2)) Show(1);
        else if (GetKeyDown(3)) Show(2);
        else if (GetNextKeyDown()) Show((current + 1) % screens.Length);
        else if (GetPrevKeyDown()) Show((current - 1 + screens.Length) % screens.Length);

        // F2: 分割表示 / 1画面表示 の切替
        if (GetSplitToggleKeyDown())
        {
            splitView = !splitView;
            Show(current);
        }

        // F1: 本番配分に戻す(ビルド後の確認用)
        if (GetToggleKeyDown())
        {
            testMode = false;
            ApplyMode();
            return;
        }

        // ウィンドウサイズが変わっても崩れないよう、毎フレーム適用
        ApplySplitLayout();
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
            // 全て Display 1 に集約
            foreach (var s in screens)
            {
                if (s.camera != null) s.camera.targetDisplay = 0;
                if (s.canvases != null)
                    foreach (var c in s.canvases)
                        if (c != null) c.targetDisplay = 0;
            }
            Show(current);
            ApplySplitLayout();
        }
        else
        {
            // 本番配置に戻す(全て有効化 + 元のTarget Display + 画面全体に描画)
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                if (s.camera != null)
                {
                    s.camera.targetDisplay = s.originalCameraDisplay;
                    s.camera.rect = new Rect(0f, 0f, 1f, 1f);
                    if (s.camera.orthographic) s.camera.orthographicSize = baseOrthoSize;
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
            bool selected = (i == current);
            var s = screens[i];

            // 分割表示中は全カメラ有効、1画面表示中は選択中のみ有効
            if (s.camera != null) s.camera.enabled = splitView || selected;

            // Overlay Canvas は範囲を分けられないので、選択中の画面のものだけ表示
            if (s.canvases != null)
                foreach (var c in s.canvases)
                    if (c != null) c.gameObject.SetActive(selected);
        }

        ApplySplitLayout();
    }

    // 分割表示: カメラを横に3等分して並べる
    void ApplySplitLayout()
    {
        int n = screens.Length;
        if (n == 0) return;

        float cellAspect = ((float)Screen.width / n) / Mathf.Max(1, Screen.height);

        for (int i = 0; i < n; i++)
        {
            var cam = screens[i].camera;
            if (cam == null) continue;

            if (splitView)
            {
                cam.rect = new Rect((float)i / n, 0f, 1f / n, 1f);

                // 縦長になった分、表示範囲を広げて、16:9のときと同じ横幅が映るようにする
                if (cam.orthographic)
                    cam.orthographicSize = Mathf.Max(baseOrthoSize, baseOrthoSize * targetAspect / cellAspect);
            }
            else
            {
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                if (cam.orthographic) cam.orthographicSize = baseOrthoSize;
            }
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
        return k != null && k.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    bool GetPrevKeyDown()
    {
        return false;   // 矢印キーは2Pカーソルで使うため無効化
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

    bool GetSplitToggleKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && k.f2Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F2);
#endif
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || !testMode || screens.Length == 0) return;

        string mode = splitView ? "分割表示(左:画面1 / 中:画面2 / 右:画面3)" : $"1画面表示 {current + 1}/{screens.Length}";
        GUI.Label(new Rect(10, 10, 900, 24), $"[TEST] {mode}   F2: 分割/1画面切替  1/2/3/Tab: 画面選択");
    }
}