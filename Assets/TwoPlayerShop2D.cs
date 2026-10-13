using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2人プレイ用: 商品棚(画面2)で1P/2Pがカーソルで商品を掴むと、
/// 商品とカーソルがそのプレイヤーの画面(1P=画面1 / 2P=画面3)へ移る。
/// 自分の画面でAボタンを押すと商品を落とし、カーソルは商品棚へ戻る。
/// コントローラーは「最初にAボタンを押した方が1P、次が2P」で割り当てる。
/// キーボードは予備(1P: WASD+Space / 2P: 矢印+Enter)。
/// DisplayTestSwitcher と併用する。
/// </summary>
public class TwoPlayerShop2D : MonoBehaviour
{
    [Header("参照")]
    public DisplayTestSwitcher switcher;

    [Header("画面の割り当て(0始まり)")]
    public int screenP1 = 0;     // ディスプレイ1 = 1P
    public int screenShop = 1;   // ディスプレイ2 = 商品棚
    public int screenP2 = 2;     // ディスプレイ3 = 2P

    [Header("表示用レイヤー番号(名前の登録は不要)")]
    public int layerP1 = 8;
    public int layerShop = 9;
    public int layerP2 = 10;

    [Header("カーソル")]
    public float cursorSpeed = 8f;
    public Color p1Color = new Color(1f, 0.3f, 0.3f);
    public Color p2Color = new Color(0.3f, 0.5f, 1f);

    [Header("カーソル画像(未設定なら矢印を自動生成)")]
    public Sprite p1CursorSprite;
    public Sprite p2CursorSprite;
    [Tooltip("カーソルの大きさ(画像が大きすぎ/小さすぎるときに調整)")]
    public float cursorScale = 1.2f;

    [Header("コントローラー")]
    [Tooltip("スティックの遊び(これ未満は無視)")]
    public float stickDeadzone = 0.2f;
    [Tooltip("ONならキーボードも予備として使える(1P: WASD+Space / 2P: 矢印+Enter)")]
    public bool useKeyboard = true;

    [Header("動作")]
    public bool arrangeCameras = true;
    public bool showOverlayLabel = true;

    class Player
    {
        public string name;
        public int screen;
        public int layer;
        public Transform cursorShop;     // 棚画面のカーソル
        public Transform cursorOwn;      // 自分の画面のカーソル
        public Vector2 shopPos;
        public Vector2 ownPos;
        public Color color;
        public bool inShop = true;       // true: 棚を操作中 / false: 自分の画面を操作中
        public GameObject held;          // 掴んでいる商品(自分の画面で持ち運び中)
    }

    Player p1, p2;
    readonly Dictionary<Collider2D, GameObject> products = new Dictionary<Collider2D, GameObject>();
    readonly Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    static Sprite squareSprite, circleSprite, arrowSprite;
    string lastEvent = "";
    int dropOrder = 0;   // 落とした順に手前へ重ねる
    Gamepad pad1, pad2;  // 参加済みのコントローラー(Aボタンで割り当て)

    // ---------- 初期化 ----------

    void Start()
    {
        if (switcher == null) switcher = FindFirstObjectByType<DisplayTestSwitcher>();
        if (switcher == null) { Debug.LogError("[Shop2D] DisplayTestSwitcher が見つかりません"); return; }

        CreateProducts();
        if (arrangeCameras) ArrangeCameras();

        p1 = CreatePlayer("1P", screenP1, layerP1, p1Color, p1CursorSprite, new Vector2(-4f, -3f));
        p2 = CreatePlayer("2P", screenP2, layerP2, p2Color, p2CursorSprite, new Vector2(4f, -3f));

        SetupCullingMasks();

        // 最初は両プレイヤーとも棚を操作中
        foreach (var p in new[] { p1, p2 })
        {
            p.cursorShop.gameObject.SetActive(true);
            p.cursorOwn.gameObject.SetActive(false);
        }
    }

    void CreateProducts()
    {
        Color[] cols = {
            new Color(1f, 0.5f, 0.2f), new Color(0.3f, 0.8f, 1f), new Color(0.9f, 0.9f, 0.3f),
            new Color(0.8f, 0.4f, 0.9f), new Color(0.4f, 0.9f, 0.5f), new Color(0.9f, 0.9f, 0.9f)
        };
        for (int i = 0; i < cols.Length; i++)
        {
            float x = -4f + (i % 3) * 4f;
            float y = 1.5f - (i / 3) * 3f;
            RegisterProduct(MakeSprite($"Product_{i + 1}", new Vector2(x, y), i % 2 == 1, cols[i], layerShop));
        }
    }

    GameObject MakeSprite(string objName, Vector2 pos, bool circle, Color color, int layer)
    {
        var go = new GameObject(objName);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(2f, 2f, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circle ? GetCircleSprite() : GetSquareSprite();
        sr.color = color;

        if (circle) go.AddComponent<CircleCollider2D>();
        else go.AddComponent<BoxCollider2D>();

        go.layer = layer;
        return go;
    }

    void RegisterProduct(GameObject go)
    {
        originalColors[go] = go.GetComponent<SpriteRenderer>().color;
        foreach (var col in go.GetComponentsInChildren<Collider2D>())
            products[col] = go;
    }

    Player CreatePlayer(string pname, int screen, int ownLayer, Color color, Sprite cursorSprite, Vector2 startPos)
    {
        var p = new Player { name = pname, screen = screen, layer = ownLayer, color = color, shopPos = startPos, ownPos = Vector2.zero };

        // 棚画面に出すカーソル(レイヤー: 棚)
        p.cursorShop = MakeCursor($"Cursor_{pname}_Shop", color, layerShop, cursorSprite).transform;
        p.cursorShop.position = startPos;

        // 自分の画面に出すカーソル(商品を落とす位置を選ぶ用)
        p.cursorOwn = MakeCursor($"Cursor_{pname}_Own", color, ownLayer, cursorSprite).transform;
        p.cursorOwn.position = Vector2.zero;
        return p;
    }

    GameObject MakeCursor(string objName, Color color, int layer, Sprite customSprite)
    {
        var go = new GameObject(objName);
        go.transform.localScale = new Vector3(cursorScale, cursorScale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();

        if (customSprite != null)
        {
            sr.sprite = customSprite;
            sr.color = Color.white;      // 画像そのままの色で表示
        }
        else
        {
            sr.sprite = GetArrowSprite();
            sr.color = color;            // 自動生成の矢印は1P赤/2P青で着色
        }

        sr.sortingOrder = 1000;   // 商品より常に手前
        go.layer = layer;
        // カーソルにコライダーは付けない(掴み判定の邪魔になるため)
        return go;
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

            int mask = 1 << 0;   // Default は全画面
            if (i == screenP1) mask |= 1 << layerP1;
            if (i == screenShop) mask |= 1 << layerShop;
            if (i == screenP2) mask |= 1 << layerP2;
            cam.cullingMask = mask;
        }
    }

    // ---------- メインループ ----------

    void Update()
    {
        if (switcher == null || p1 == null) return;

        // 参加(Aボタンを押したコントローラーを1P→2Pの順に割り当てる)
        TryAssignPads(out bool joined1, out bool joined2);

        MoveCursor(p1, GetMove1P());
        MoveCursor(p2, GetMove2P());

        // 参加したフレームのAボタンは「掴む」として扱わない
        if (!joined1 && Grab1PPressed()) PressAction(p1);
        if (!joined2 && Grab2PPressed()) PressAction(p2);
    }

    void TryAssignPads(out bool joined1, out bool joined2)
    {
        joined1 = joined2 = false;

        // 抜かれたコントローラーは外す
        if (pad1 != null && !pad1.added) pad1 = null;
        if (pad2 != null && !pad2.added) pad2 = null;

        foreach (var g in Gamepad.all)
        {
            if (g == pad1 || g == pad2) continue;
            if (!g.buttonSouth.wasPressedThisFrame && !g.startButton.wasPressedThisFrame) continue;

            if (pad1 == null) { pad1 = g; joined1 = true; Debug.Log($"[Shop2D] 1Pに割り当て: {g.displayName}"); }
            else if (pad2 == null) { pad2 = g; joined2 = true; Debug.Log($"[Shop2D] 2Pに割り当て: {g.displayName}"); }
        }
    }

    void MoveCursor(Player p, Vector2 input)
    {
        Vector2 delta = input * cursorSpeed * Time.deltaTime;

        // 操作中の画面の位置だけを動かす
        if (p.inShop) p.shopPos = ClampToView(p.shopPos + delta);
        else p.ownPos = ClampToView(p.ownPos + delta);

        p.cursorShop.position = p.shopPos;
        p.cursorOwn.position = p.ownPos;

        // 操作中の画面のカーソルだけ表示する
        p.cursorShop.gameObject.SetActive(p.inShop);
        p.cursorOwn.gameObject.SetActive(!p.inShop);

        // 掴んでいる商品はカーソルについてくる
        if (p.held != null)
            p.held.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);
    }

    static Vector2 ClampToView(Vector2 v)
    {
        return new Vector2(Mathf.Clamp(v.x, -8.5f, 8.5f), Mathf.Clamp(v.y, -4.5f, 4.5f));
    }

    // Aボタン: 棚なら「掴む」、自分の画面なら「落とす」
    void PressAction(Player p)
    {
        if (p.inShop) TryPickUp(p);
        else Drop(p);
    }

    void TryPickUp(Player p)
    {
        Collider2D col = Physics2D.OverlapPoint(p.shopPos, 1 << layerShop);
        if (col == null || !products.TryGetValue(col, out var go)) return;

        // 商品を自分の画面へ移し、カーソルも自分の画面へ
        SetLayer(go, p.layer);
        go.GetComponent<SpriteRenderer>().color = originalColors[go];
        go.GetComponent<SpriteRenderer>().sortingOrder = 999;   // 持ち運び中は手前に

        p.held = go;
        p.ownPos = Vector2.zero;   // 自分の画面の中央から始める
        p.inShop = false;
        go.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);

        lastEvent = $"{p.name}が {go.name} を掴んだ → 画面{p.screen + 1}へ移動(Aで落とす)";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    void Drop(Player p)
    {
        if (p.held == null) { p.inShop = true; return; }

        // 今のカーソル位置に落として、カーソルは棚へ戻る
        p.held.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);
        p.held.GetComponent<SpriteRenderer>().sortingOrder = ++dropOrder;

        lastEvent = $"{p.name}が {p.held.name} を落とした → カーソルは棚へ戻る";
        Debug.Log($"[Shop2D] {lastEvent}");

        p.held = null;
        p.inShop = true;
    }

    static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
    }

    // ---------- スプライト生成 ----------

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

    // 左上向きの三角形(矢印カーソル)。先端がピボット
    static Sprite GetArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;
        const int size = 64;
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 左上(0, size-1)を先端とする三角形
                int fromTop = (size - 1) - y;
                bool inside = x <= fromTop * 0.6f && fromTop <= size - 8 && x <= size - 8 - fromTop * 0.2f && fromTop >= x * 1.3f;
                px[y * size + x] = inside ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 1f), 64f);
        return arrowSprite;
    }

    // ---------- 入力(コントローラー優先 + キーボード予備) ----------

    // 1P = 最初にAボタンを押したコントローラー、2P = 次に押したコントローラー
    Gamepad PadOf(int playerIndex)
    {
        return playerIndex == 0 ? pad1 : pad2;
    }

    // 現在つながっているコントローラー数
    int PadCount => Gamepad.all.Count;

    Keyboard Kb => useKeyboard ? Keyboard.current : null;

    Vector2 StickOrDpad(Gamepad pad)
    {
        Vector2 v = pad.leftStick.ReadValue();
        if (v.magnitude < stickDeadzone) v = Vector2.zero;
        if (v == Vector2.zero) v = pad.dpad.ReadValue();   // 十字キーでも動かせる
        return v;
    }

    Vector2 GetMove1P()
    {
        // キーボードを押している間はキーボード優先、それ以外はコントローラー
        var k = Kb;
        if (k != null)
        {
            var kv = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
                                 (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            if (kv != Vector2.zero) return kv;
        }

        var pad = PadOf(0);
        return pad != null ? StickOrDpad(pad) : Vector2.zero;
    }

    Vector2 GetMove2P()
    {
        var k = Kb;
        if (k != null)
        {
            var kv = new Vector2((k.rightArrowKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed ? 1 : 0),
                                 (k.upArrowKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed ? 1 : 0));
            if (kv != Vector2.zero) return kv;
        }

        var pad = PadOf(1);
        return pad != null ? StickOrDpad(pad) : Vector2.zero;
    }

    bool Grab1PPressed()
    {
        var pad = PadOf(0);
        if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;   // Aボタン
        var k = Kb;
        return k != null && k.spaceKey.wasPressedThisFrame;
    }

    bool Grab2PPressed()
    {
        var pad = PadOf(1);
        if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;   // Aボタン
        var k = Kb;
        return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || switcher == null) return;
        GUI.Label(new Rect(10, 34, 1100, 24), $"{lastEvent}");
        GUI.Label(new Rect(10, 58, 1100, 24), $"接続中のコントローラー: {PadCount}台");
        GUI.Label(new Rect(10, 82, 1100, 24),
            "1P: " + (pad1 != null ? pad1.displayName : "未参加(コントローラーのAボタンを押す)") +
            " / 2P: " + (pad2 != null ? pad2.displayName : (pad1 != null ? "未参加(2台目のAボタンを押す)" : "未参加")));
    }
}