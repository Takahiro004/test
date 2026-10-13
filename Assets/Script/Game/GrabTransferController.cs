using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 画面A(ディスプレイ1)で掴む → Aから消えて画面B(ディスプレイ2)に現れる、をテストする。
/// DisplayTestSwitcher と併用する。
/// </summary>
public class GrabTransferController : MonoBehaviour
{
    [Header("参照")]
    public DisplayTestSwitcher switcher;

    [Header("画面の割り当て(0始まり: 0=ディスプレイ1, 1=ディスプレイ2)")]
    public int screenA = 0;
    public int screenB = 1;

    [Header("表示用レイヤー番号(名前の登録は不要)")]
    public int layerA = 8;
    public int layerB = 9;

    [Header("動作")]
    [Tooltip("ONなら、画面Bでオブジェクトを掴むと画面Aに戻る")]
    public bool allowReturnFromB = true;
    public Color heldColor = Color.yellow;
    public bool showOverlayLabel = true;

    [Header("テスト用シーン自動構築")]
    public bool arrangeCameras = true;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    readonly Dictionary<Collider, GameObject> grabbables = new Dictionary<Collider, GameObject>();
    readonly Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();

    GameObject held;
    int heldDestScreen = -1;
    int lastScreen = -1;

    // ---------- 初期化 ----------

    void Start()
    {
        if (switcher == null) switcher = FindFirstObjectByType<DisplayTestSwitcher>();
        if (switcher == null) { Debug.LogError("[GrabTransfer] DisplayTestSwitcher が見つかりません"); return; }

        CreateTestObjects();
        if (arrangeCameras) ArrangeCameras();
        SetupCullingMasks();
    }

    void CreateTestObjects()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "TestGround";
        ground.transform.localScale = new Vector3(1.5f, 1, 1.5f);   // Defaultレイヤー=両画面に映る

        Register(MakePrimitive(PrimitiveType.Cube, "Grab_Cube", new Vector3(-2f, 0.5f, 0f)));
        Register(MakePrimitive(PrimitiveType.Sphere, "Grab_Sphere", new Vector3(0f, 0.5f, 0f)));
        Register(MakePrimitive(PrimitiveType.Capsule, "Grab_Capsule", new Vector3(2f, 1f, 0f)));
    }

    GameObject MakePrimitive(PrimitiveType type, string objName, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = objName;
        go.transform.position = pos;
        SetLayerRecursively(go, layerA);   // 最初は画面Aにある
        return go;
    }

    void Register(GameObject go)
    {
        var r = go.GetComponentInChildren<Renderer>();
        originalColors[go] = (r != null && TryGetColor(r, out var c)) ? c : Color.white;
        foreach (var col in go.GetComponentsInChildren<Collider>())
            grabbables[col] = go;
    }

    void ArrangeCameras()
    {
        Color[] bg = {
            new Color(0.45f, 0.12f, 0.12f),   // 画面1: 赤
            new Color(0.12f, 0.40f, 0.15f),   // 画面2: 緑
            new Color(0.12f, 0.20f, 0.50f)    // 画面3: 青
        };

        for (int i = 0; i < switcher.screens.Length; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;

            cam.orthographic = false;
            cam.transform.position = new Vector3(0f, 3.5f, -7f);
            cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            if (i < bg.Length) cam.backgroundColor = bg[i];
        }
    }

    void SetupCullingMasks()
    {
        for (int i = 0; i < switcher.screens.Length; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;

            int mask = 1 << 0;                       // Default(床など)は全画面に映す
            if (i == screenA) mask |= 1 << layerA;   // 画面Aだけレイヤー8を映す
            if (i == screenB) mask |= 1 << layerB;   // 画面Bだけレイヤー9を映す

            cam.cullingMask = mask;                  // それ以外の画面(画面3)は掴める物が映らない
        }
    }

    // ---------- メインループ ----------

    void Update()
    {
        if (switcher == null) return;

        int cur = switcher.Current;
        if (cur != lastScreen)
        {
            if (lastScreen >= 0)
                Debug.Log($"[GrabTransfer] 画面切替 {lastScreen + 1} → {cur + 1} / {(held != null ? held.name + " を保持中" : "保持なし")}");
            lastScreen = cur;
        }

        Camera cam = switcher.screens[cur].camera;
        if (cam == null) return;

        if (MousePressed())
        {
            if (held == null)
            {
                if (cur == screenA) TryGrab(cam, screenA, screenB, layerA, layerB);
                else if (cur == screenB && allowReturnFromB) TryGrab(cam, screenB, screenA, layerB, layerA);
            }
            else if (cur == heldDestScreen)
            {
                Release();
            }
        }

        // 移動先の画面を見ている間だけ、マウスに追従させる
        if (held != null && cur == heldDestScreen) FollowMouse(cam);
    }

    void TryGrab(Camera cam, int fromScreen, int toScreen, int fromLayer, int toLayer)
    {
        Ray ray = cam.ScreenPointToRay(MousePos());
        // 今の画面のレイヤーだけを対象にする(別画面のオブジェクトには当たらない)
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, 1 << fromLayer)) return;
        if (!grabbables.TryGetValue(hit.collider, out var go)) return;

        held = go;
        heldDestScreen = toScreen;

        SetLayerRecursively(go, toLayer);   // ← ここで元の画面から消え、移動先に現れる
        var r = go.GetComponentInChildren<Renderer>();
        if (r != null) SetColor(r, heldColor);

        Debug.Log($"[GrabTransfer] 画面{fromScreen + 1}で {go.name} を掴んだ → 画面{toScreen + 1}へ移動");
    }

    void FollowMouse(Camera cam)
    {
        // カメラに正対する面(現在のオブジェクトの奥行きを保つ)とマウス光線の交点へ
        var plane = new Plane(-cam.transform.forward, held.transform.position);
        Ray ray = cam.ScreenPointToRay(MousePos());
        if (plane.Raycast(ray, out float d))
            held.transform.position = ray.GetPoint(d);
    }

    void Release()
    {
        Debug.Log($"[GrabTransfer] 画面{switcher.Current + 1}で {held.name} を離した");
        var r = held.GetComponentInChildren<Renderer>();
        if (r != null && originalColors.TryGetValue(held, out var c)) SetColor(r, c);
        held = null;
        heldDestScreen = -1;
    }

    // ---------- ユーティリティ ----------

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
    }

    static bool TryGetColor(Renderer r, out Color c)
    {
        var m = r.sharedMaterial;
        if (m != null)
        {
            if (m.HasProperty(BaseColorId)) { c = m.GetColor(BaseColorId); return true; }
            if (m.HasProperty(ColorId)) { c = m.GetColor(ColorId); return true; }
        }
        c = Color.white;
        return false;
    }

    static void SetColor(Renderer r, Color c)
    {
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        r.SetPropertyBlock(mpb);
    }

    // ---------- 入力(新旧Input System両対応) ----------

    Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    bool MousePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || switcher == null) return;
        string state = held != null
            ? $"掴み中: {held.name} (画面{heldDestScreen + 1}で操作)"
            : "掴み: なし";
        GUI.Label(new Rect(10, 34, 700, 24), $"現在: 画面{switcher.Current + 1} / {state}");
    }
}