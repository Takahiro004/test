using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 2D版: 画面Aで掴む → Aから消えて画面Bに現れる。
/// DisplayTestSwitcher と併用する。
/// </summary>
public class GrabTransfer2D : MonoBehaviour
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
    public bool allowReturnFromB = true;
    public Color heldColor = Color.yellow;
    public bool showOverlayLabel = true;

    [Header("テスト用シーン自動構築")]
    public bool arrangeCameras = true;

    readonly Dictionary<Collider2D, GameObject> grabbables = new Dictionary<Collider2D, GameObject>();
    readonly Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();

    static Sprite squareSprite, circleSprite;

    GameObject held;
    int heldDestScreen = -1;
    int lastScreen = -1;

    // ---------- 初期化 ----------

    void Start()
    {
        if (switcher == null) switcher = FindFirstObjectByType<DisplayTestSwitcher>();
        if (switcher == null) { Debug.LogError("[GrabTransfer2D] DisplayTestSwitcher が見つかりません"); return; }

        CreateTestObjects();
        if (arrangeCameras) ArrangeCameras();
        SetupCullingMasks();
    }

    void CreateTestObjects()
    {
        Register(MakeSprite("Grab_Square", new Vector2(-3f, 0f), false, new Color(1f, 0.5f, 0.2f)));
        Register(MakeSprite("Grab_Circle", new Vector2(0f, 0f), true, new Color(0.3f, 0.8f, 1f)));
        Register(MakeSprite("Grab_Square2", new Vector2(3f, 0f), false, new Color(0.9f, 0.9f, 0.9f)));
    }

    GameObject MakeSprite(string objName, Vector2 pos, bool circle, Color color)
    {
        var go = new GameObject(objName);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(2f, 2f, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circle ? GetCircleSprite() : GetSquareSprite();
        sr.color = color;

        if (circle) go.AddComponent<CircleCollider2D>();
        else go.AddComponent<BoxCollider2D>();

        SetLayerRecursively(go, layerA);   // 最初は画面Aにある
        return go;
    }

    void Register(GameObject go)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        originalColors[go] = sr != null ? sr.color : Color.white;
        foreach (var col in go.GetComponentsInChildren<Collider2D>())
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

            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
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

            int mask = 1 << 0;                       // Default は全画面に映す
            if (i == screenA) mask |= 1 << layerA;
            if (i == screenB) mask |= 1 << layerB;
            cam.cullingMask = mask;
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
                Debug.Log($"[GrabTransfer2D] 画面切替 {lastScreen + 1} → {cur + 1} / {(held != null ? held.name + " を保持中" : "保持なし")}");
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

        if (held != null && cur == heldDestScreen) FollowMouse(cam);
    }

    void TryGrab(Camera cam, int fromScreen, int toScreen, int fromLayer, int toLayer)
    {
        Vector2 world = cam.ScreenToWorldPoint(MousePos());
        // 今の画面のレイヤーだけを対象にする
        Collider2D col = Physics2D.OverlapPoint(world, 1 << fromLayer);
        if (col == null || !grabbables.TryGetValue(col, out var go)) return;

        held = go;
        heldDestScreen = toScreen;

        SetLayerRecursively(go, toLayer);   // 元の画面から消え、移動先に現れる
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = heldColor;

        Debug.Log($"[GrabTransfer2D] 画面{fromScreen + 1}で {go.name} を掴んだ → 画面{toScreen + 1}へ移動");
    }

    void FollowMouse(Camera cam)
    {
        Vector3 world = cam.ScreenToWorldPoint(MousePos());
        held.transform.position = new Vector3(world.x, world.y, 0f);
    }

    void Release()
    {
        Debug.Log($"[GrabTransfer2D] 画面{switcher.Current + 1}で {held.name} を離した");
        var sr = held.GetComponent<SpriteRenderer>();
        if (sr != null && originalColors.TryGetValue(held, out var c)) sr.color = c;
        held = null;
        heldDestScreen = -1;
    }

    // ---------- ユーティリティ ----------

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
    }

    // 画像ファイルなしで使えるスプライトをコードで生成する
    static Sprite GetSquareSprite()
    {
        if (squareSprite != null) return squareSprite;
        var tex = new Texture2D(64, 64);
        var px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        squareSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
        return squareSprite;
    }

    static Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int size = 64;
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                px[y * size + x] = (dx * dx + dy * dy <= r * r) ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        return circleSprite;
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