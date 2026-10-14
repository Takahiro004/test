using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2人プレイ用: 商品棚(画面2)で1P/2Pがカーソルで商品を掴むと、
/// 商品とカーソルがそのプレイヤーの画面(1P=画面1 / 2P=画面3)へ移る。
/// 自分の画面では横にだけ動ける(高さは dropHeight で固定)。
/// Aボタンを押すと商品を離し、Rigidbody2Dで自由落下する。
/// 離した後、カーソルは商品棚へ戻る。
/// 1P/2Pの画面にはレジ袋があり、商品が入った瞬間にその商品の金額が出て、袋の合計に足される。
/// コントローラーは「最初にAボタンを押した方が1P、次が2P」で割り当てる。
/// キーボードは予備(1P: WASD+Space / 2P: 矢印+Enter)。
/// 棚の商品は取られると、少し待ってから同じ場所に同じ商品が補充される。
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

    [Header("自分の画面(商品を落とす位置を選ぶとき)")]
    [Tooltip("商品を持って横移動するときの高さ(画面の中心が0、上の端が5)。上限なし。5より大きいと画面の外(上)から落ちてくる")]
    public float dropHeight = 3.5f;

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

    [Header("落下(物理)")]
    [Tooltip("重力の強さ(大きいほど速く落ちる)")]
    public float gravityScale = 2f;
    [Tooltip("ONなら、1P/2P画面に床と左右の壁を作る")]
    public bool createFloor = true;
    [Tooltip("床の上の面の高さ(画面の中心が0、下の端が-5)。再生中に変えてもすぐ反映される")]
    public float floorY = -4.5f;
    public float wallX = 8.9f;
    [Tooltip("ONなら、落下中に商品が回転しない")]
    public bool freezeRotation = false;

    [Header("商品の画像(未設定なら四角・丸を自動生成)")]
    [Tooltip("棚の左上から順に Product_1, 2, 3 … の画像。空の欄は四角・丸のまま")]
    public Sprite[] productSprites = new Sprite[6];
    [Tooltip("画像の商品の大きさ(縦横の長いほうがこの長さになる。四角・丸は2)")]
    public float productImageSize = 2f;

    [Header("棚の補充")]
    [Tooltip("ONなら、取られた商品を同じ場所に補充する")]
    public bool refillShelf = true;
    [Tooltip("取られてから補充されるまでの秒数")]
    public float refillDelay = 1.5f;

    [Header("レジ袋(1P/2Pの画面)")]
    [Tooltip("ONなら、1P/2Pの画面にレジ袋を置く")]
    public bool createBag = true;
    [Tooltip("袋の横の位置(画面の中心が0、右の端が8.9)")]
    public float bagX = 0f;
    [Tooltip("袋の幅")]
    public float bagWidth = 5f;
    [Tooltip("袋の高さ(床から上へ)")]
    public float bagHeight = 4f;
    [Tooltip("袋のふちの太さ")]
    public float bagWallThickness = 0.3f;
    public Color bagColor = new Color(1f, 1f, 1f, 0.85f);
    [Tooltip("袋の画像(未設定なら白いU字)。画像は袋の幅・高さに引き伸ばして表示し、商品がぶつかるふちは見えない壁になる")]
    public Sprite bagSprite;
    [Tooltip("棚の商品の金額(左上から順に Product_1, 2, 3 …)")]
    public int[] productPrices = { 100, 150, 200, 250, 300, 500 };
    [Tooltip("袋に入ったときに出る金額の表示時間(秒)")]
    public float pricePopupTime = 1f;

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
        public readonly List<GameObject> dropped = new List<GameObject>();   // 自分の画面に落とした商品(まだ袋に入っていない)
        public Bag bag;
        public int total;                // 袋に入った商品の合計金額
    }

    // レジ袋(左右のふち + 底 + 背景 + 合計の表示)
    class Bag
    {
        public Transform back, left, right, bottom;
        public TextMesh totalLabel;
    }

    // 棚の1か所ぶん(どこに・どんな商品を置くか)
    class ShelfSlot
    {
        public Vector2 pos;
        public bool circle;
        public Color color;
        public int count;   // これまでに置いた個数(名前の番号に使う)
    }

    Player p1, p2;
    readonly List<ShelfSlot> slots = new List<ShelfSlot>();
    readonly Dictionary<GameObject, ShelfSlot> slotOf = new Dictionary<GameObject, ShelfSlot>();
    readonly Dictionary<GameObject, int> priceOf = new Dictionary<GameObject, int>();   // 商品ごとの金額
    static Font labelFont;
    float appliedBagX, appliedBagWidth, appliedBagHeight, appliedBagWall, appliedBagFloorY;
    readonly Dictionary<Collider2D, GameObject> products = new Dictionary<Collider2D, GameObject>();
    readonly Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    static Sprite squareSprite, circleSprite, arrowSprite;
    string lastEvent = "";
    readonly List<Transform> floors = new List<Transform>();   // 1P/2P画面の床
    float appliedFloorY;   // 床に今反映している高さ
    int dropOrder = 0;   // 落とした順に手前へ重ねる
    Gamepad pad1, pad2;  // 参加済みのコントローラー(Aボタンで割り当て)

    // ---------- 初期化 ----------

    void Start()
    {
        if (switcher == null) switcher = FindFirstObjectByType<DisplayTestSwitcher>();
        if (switcher == null) { Debug.LogError("[Shop2D] DisplayTestSwitcher が見つかりません"); return; }

        SetupPhysicsLayers();
        CreateProducts();
        if (arrangeCameras) ArrangeCameras();

        p1 = CreatePlayer("1P", screenP1, layerP1, p1Color, p1CursorSprite, new Vector2(-4f, -3f));
        p2 = CreatePlayer("2P", screenP2, layerP2, p2Color, p2CursorSprite, new Vector2(4f, -3f));

        SetupCullingMasks();

        if (createFloor)
        {
            CreateFloor(layerP1);
            CreateFloor(layerP2);
        }

        if (createBag)
        {
            p1.bag = CreateBag(p1);
            p2.bag = CreateBag(p2);
            LayoutBags();
        }

        // 最初は両プレイヤーとも棚を操作中
        foreach (var p in new[] { p1, p2 })
        {
            p.cursorShop.gameObject.SetActive(true);
            p.cursorOwn.gameObject.SetActive(false);
        }
    }

    // 3つの画面は同じワールド座標を使っているので、
    // 画面(レイヤー)が違う物同士は物理的にぶつからないようにする
    void SetupPhysicsLayers()
    {
        Physics2D.IgnoreLayerCollision(layerP1, layerP2, true);
        Physics2D.IgnoreLayerCollision(layerP1, layerShop, true);
        Physics2D.IgnoreLayerCollision(layerP2, layerShop, true);
        // 同じ画面の中(1P同士、2P同士)はぶつかる
        Physics2D.IgnoreLayerCollision(layerP1, layerP1, false);
        Physics2D.IgnoreLayerCollision(layerP2, layerP2, false);
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
            var slot = new ShelfSlot { pos = new Vector2(x, y), circle = i % 2 == 1, color = cols[i] };
            slots.Add(slot);
            PlaceProduct(slot);
        }
    }

    // 棚の場所に商品を1個置く
    void PlaceProduct(ShelfSlot slot)
    {
        slot.count++;
        int index = slots.IndexOf(slot) + 1;
        string objName = slot.count == 1 ? $"Product_{index}" : $"Product_{index}_{slot.count}";
        int si = slots.IndexOf(slot);
        Sprite image = productSprites != null && si < productSprites.Length ? productSprites[si] : null;
        var go = MakeSprite(objName, slot.pos, slot.circle, slot.color, layerShop, image);
        RegisterProduct(go);
        slotOf[go] = slot;
        int i = index - 1;
        priceOf[go] = productPrices != null && i < productPrices.Length ? productPrices[i] : 0;
    }

    System.Collections.IEnumerator RefillLater(ShelfSlot slot)
    {
        yield return new WaitForSeconds(refillDelay);
        PlaceProduct(slot);
    }

    GameObject MakeSprite(string objName, Vector2 pos, bool circle, Color color, int layer, Sprite image = null)
    {
        var go = new GameObject(objName);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(2f, 2f, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        if (image != null)
        {
            // 用意した画像: 色はそのまま、当たり判定は画像の形に合わせる
            sr.sprite = image;
            sr.color = Color.white;
            Vector2 size = image.bounds.size;
            float scale = productImageSize / Mathf.Max(size.x, size.y, 0.01f);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            go.AddComponent<PolygonCollider2D>();
        }
        else
        {
            sr.sprite = circle ? GetCircleSprite() : GetSquareSprite();
            sr.color = color;
            if (circle) go.AddComponent<CircleCollider2D>();
            else go.AddComponent<BoxCollider2D>();
        }

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

    // 指定レイヤー(=その画面だけ)に映る床と左右の壁を作る
    void CreateFloor(int layer)
    {
        var root = new GameObject($"Floor_Layer{layer}");
        root.layer = layer;

        // 床(見える)
        var floor = new GameObject("Floor");
        floor.transform.SetParent(root.transform);
        floor.layer = layer;
        floor.transform.position = new Vector3(0f, floorY - 0.25f, 0f);
        floor.transform.localScale = new Vector3(wallX * 2f + 2f, 0.5f, 1f);
        floors.Add(floor.transform);
        appliedFloorY = floorY;
        var sr = floor.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        sr.sortingOrder = -10;
        floor.AddComponent<BoxCollider2D>();

        // 左右の壁(見えない)
        for (int i = 0; i < 2; i++)
        {
            float sign = i == 0 ? -1f : 1f;
            var wall = new GameObject(i == 0 ? "WallLeft" : "WallRight");
            wall.transform.SetParent(root.transform);
            wall.layer = layer;
            wall.transform.position = new Vector3(sign * (wallX + 0.5f), 0f, 0f);
            wall.transform.localScale = new Vector3(1f, 40f, 1f);
            wall.AddComponent<BoxCollider2D>().size = Vector2.one;
        }
    }

    // ---------- メインループ ----------

    void Update()
    {
        if (switcher == null || p1 == null) return;

        if (floorY != appliedFloorY) ApplyFloorY();
        if (createBag && p1.bag != null)
        {
            if (bagX != appliedBagX || bagWidth != appliedBagWidth || bagHeight != appliedBagHeight ||
                bagWallThickness != appliedBagWall || floorY != appliedBagFloorY) LayoutBags();
            CheckBag(p1);
            CheckBag(p2);
        }

        // 参加(Aボタンを押したコントローラーを1P→2Pの順に割り当てる)
        TryAssignPads(out bool joined1, out bool joined2);

        MoveCursor(p1, GetMove1P());
        MoveCursor(p2, GetMove2P());

        // 参加したフレームのAボタンは「掴む」として扱わない
        if (!joined1 && Grab1PPressed()) PressAction(p1);
        if (!joined2 && Grab2PPressed()) PressAction(p2);
    }

    // Inspector で床の高さを変えたら、床を動かす
    void ApplyFloorY()
    {
        appliedFloorY = floorY;
        foreach (var f in floors)
            f.position = new Vector3(f.position.x, floorY - 0.25f, f.position.z);

        // 床の上で止まっている商品を起こす(床を下げたとき宙に浮いたままにならないように)
        foreach (var go in originalColors.Keys)
        {
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) rb.WakeUp();
        }
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
        else p.ownPos = new Vector2(Mathf.Clamp(p.ownPos.x + delta.x, -8.5f, 8.5f), dropHeight);   // 自分の画面では横移動だけ(高さは制限しない)

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

    // Aボタン: 棚なら「掴む」、自分の画面なら「離す」
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
        var sr = go.GetComponent<SpriteRenderer>();
        sr.color = originalColors[go];
        sr.sortingOrder = 999;   // 持ち運び中は手前に

        // 持ち運び中は物理を止める(他の商品を押しのけない)
        foreach (var c in go.GetComponentsInChildren<Collider2D>()) c.isTrigger = true;
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        go.transform.rotation = Quaternion.identity;

        // 棚から取ったので、その場所に補充する(1回だけ)
        if (refillShelf && slotOf.TryGetValue(go, out var slot))
        {
            slotOf.Remove(go);
            StartCoroutine(RefillLater(slot));
        }

        p.held = go;
        p.ownPos = new Vector2(0f, dropHeight);   // 自分の画面の上のほう(dropHeight)から始める
        p.inShop = false;
        go.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);

        lastEvent = $"{p.name}が {go.name} を掴んだ → 画面{p.screen + 1}へ移動(Aで離す)";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    void Drop(Player p)
    {
        if (p.held == null) { p.inShop = true; return; }

        var go = p.held;

        // 今のカーソル位置から自由落下させる
        go.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);
        go.GetComponent<SpriteRenderer>().sortingOrder = ++dropOrder;

        foreach (var c in go.GetComponentsInChildren<Collider2D>()) c.isTrigger = false;
        var rb = go.GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = gravityScale;
        rb.linearVelocity = Vector2.zero;
        rb.freezeRotation = freezeRotation;

        if (!p.dropped.Contains(go)) p.dropped.Add(go);

        lastEvent = $"{p.name}が {go.name} を離した → 落下 / カーソルは棚へ戻る";
        Debug.Log($"[Shop2D] {lastEvent}");

        p.held = null;
        p.inShop = true;
    }

    // ---------- レジ袋 ----------

    Bag CreateBag(Player p)
    {
        var root = new GameObject($"Bag_{p.name}");
        root.layer = p.layer;
        var bag = new Bag();

        // 背景(商品より奥。当たり判定なし)
        bag.back = MakeBagPart("Back", root.transform, p.layer, new Color(bagColor.r, bagColor.g, bagColor.b, bagColor.a * 0.25f), -20, false);
        // ふちと底(商品がぶつかって積み重なる)
        bag.left = MakeBagPart("Left", root.transform, p.layer, bagColor, 500, true);
        bag.right = MakeBagPart("Right", root.transform, p.layer, bagColor, 500, true);
        bag.bottom = MakeBagPart("Bottom", root.transform, p.layer, bagColor, 500, true);

        if (bagSprite != null)
        {
            // 袋の画像を背景に使い、ふちと底は見えない壁にする
            var backSr = bag.back.GetComponent<SpriteRenderer>();
            backSr.sprite = bagSprite;
            backSr.color = Color.white;
            foreach (var wall in new[] { bag.left, bag.right, bag.bottom })
                wall.GetComponent<SpriteRenderer>().enabled = false;
        }

        bag.totalLabel = MakeText($"BagTotal_{p.name}", root.transform, p.layer, "¥0", Color.white, 1001);
        return bag;
    }

    Transform MakeBagPart(string objName, Transform parent, int layer, Color color, int order, bool collider)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent);
        go.layer = layer;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = color;
        sr.sortingOrder = order;
        if (collider) go.AddComponent<BoxCollider2D>();
        return go.transform;
    }

    // Inspector の袋の位置・大きさを反映する(再生中に変えてもすぐ反映)
    void LayoutBags()
    {
        appliedBagX = bagX; appliedBagWidth = bagWidth; appliedBagHeight = bagHeight;
        appliedBagWall = bagWallThickness; appliedBagFloorY = floorY;

        float t = bagWallThickness;
        foreach (var p in new[] { p1, p2 })
        {
            var b = p.bag;
            if (b == null) continue;
            SetRect(b.back, bagX, floorY + bagHeight / 2f, bagWidth, bagHeight);
            // 画像は大きさがまちまちなので、袋の幅・高さぴったりに引き伸ばす
            Vector2 backSize = b.back.GetComponent<SpriteRenderer>().sprite.bounds.size;
            b.back.localScale = new Vector3(bagWidth / Mathf.Max(backSize.x, 0.01f), bagHeight / Mathf.Max(backSize.y, 0.01f), 1f);
            SetRect(b.left, bagX - bagWidth / 2f + t / 2f, floorY + bagHeight / 2f, t, bagHeight);
            SetRect(b.right, bagX + bagWidth / 2f - t / 2f, floorY + bagHeight / 2f, t, bagHeight);
            SetRect(b.bottom, bagX, floorY + t / 2f, bagWidth, t);
            b.totalLabel.transform.position = new Vector3(bagX, floorY + bagHeight + 0.6f, 0f);
        }
    }

    static void SetRect(Transform tr, float cx, float cy, float w, float h)
    {
        tr.position = new Vector3(cx, cy, 0f);
        tr.localScale = new Vector3(w, h, 1f);
    }

    // 落とした商品の中心が袋の内側に入ったら、金額を出して合計に足す(1個につき1回だけ)
    void CheckBag(Player p)
    {
        float t = bagWallThickness;
        float left = bagX - bagWidth / 2f + t, right = bagX + bagWidth / 2f - t;
        float bottom = floorY + t, top = floorY + bagHeight;

        for (int i = p.dropped.Count - 1; i >= 0; i--)
        {
            var go = p.dropped[i];
            if (go == null) { p.dropped.RemoveAt(i); continue; }

            Vector2 c = go.transform.position;
            if (c.x < left || c.x > right || c.y < bottom || c.y > top) continue;

            p.dropped.RemoveAt(i);
            priceOf.TryGetValue(go, out int price);
            p.total += price;
            p.bag.totalLabel.text = $"¥{p.total}";
            StartCoroutine(PricePopup(p, c, price));

            lastEvent = $"{p.name}の袋に {go.name} が入った +¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }
    }

    // 金額を表示して、上へ浮かびながら消える
    System.Collections.IEnumerator PricePopup(Player p, Vector2 pos, int price)
    {
        var label = MakeText("PricePopup", null, p.layer, $"+¥{price}", new Color(1f, 0.95f, 0.3f), 1002);
        var color = label.color;
        float time = 0f;
        while (time < pricePopupTime)
        {
            float k = time / pricePopupTime;
            label.transform.position = new Vector3(pos.x, pos.y + 1f + k, 0f);
            label.color = new Color(color.r, color.g, color.b, 1f - k);
            time += Time.deltaTime;
            yield return null;
        }
        Destroy(label.gameObject);
    }

    static TextMesh MakeText(string objName, Transform parent, int layer, string text, Color color, int order)
    {
        if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject(objName);
        if (parent != null) go.transform.SetParent(parent);
        go.layer = layer;
        var tm = go.AddComponent<TextMesh>();
        tm.font = labelFont;
        tm.text = text;
        tm.color = color;
        tm.fontSize = 64;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = labelFont.material;
        mr.sortingOrder = order;
        return tm;
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