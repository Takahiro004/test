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
/// 袋の外に落ちた商品(床に着いた / 袋の外で止まった)は消える。
/// 1人が商品を掴んでから grabWindow 秒以内に、もう1人が同じ商品を掴むと「掴み合い」になり、
/// 棚の画面にゲージが出る。ゲージが最大に近いときにAで止めた方が商品を勝ち取る(チャージインパクト風)。
/// 2人ともAを押すまで掴み合いは終わらない。引き分け(表示の数字が同じ)ならやり直す。
/// コントローラーは「最初にAボタンを押した方が1P、次が2P」で割り当てる。
/// キーボードは予備(1P: WASD+Space / 2P: 矢印+Enter)。
/// 棚の商品は取られると、少し待ってから同じ場所に同じ商品が補充される。
/// DisplayTestSwitcher と併用する。
///
/// 棚の画面(画面2)は 横 / 縦 / 縦(モニターだけ回す) を shopOrientation で切り替えられる。
/// ファイルの分け方(すべて同じ1つのスクリプト。partial class):
///   TwoPlayerShop2D.cs        設定の欄・初期化・棚・移動・掴む/離す・画面表示
///   TwoPlayerShop2D.Clash.cs  掴み合い
///   TwoPlayerShop2D.Bag.cs    レジ袋・金額・袋の外の商品を消す
///   TwoPlayerShop2D.Sprites.cs 四角・丸・矢印・ゲージの形の自動生成
///   TwoPlayerShop2D.Input.cs  コントローラー/キーボードの入力
///   TwoPlayerShop2D.ShopView.cs 棚の画面の向き(横/縦)・棚の並べ方・カメラ
///   Inspector の欄の順番が変わらないように、public の設定はすべて本体に置く
/// </summary>
public partial class TwoPlayerShop2D : MonoBehaviour
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
    [Tooltip("ONなら、袋の外に落ちた商品を消す(床に着いたとき / 袋の外で止まったとき)")]
    public bool removeOutsideBag = true;
    [Tooltip("袋の外で止まってから消えるまでの秒数(袋のふちや他の商品の上に乗ったとき)")]
    public float removeAfterStopTime = 0.5f;

    [Header("掴み合い(2人が同じ商品を掴んだとき)")]
    [Tooltip("1人が掴んでからこの秒数以内に、もう1人が同じ場所でAを押すと掴み合いになる。0なら掴み合いなし")]
    public float grabWindow = 0.5f;
    [Tooltip("ゲージが 0→最大→0 と往復する速さ(1秒あたりの往復回数)")]
    public float clashGaugeSpeed = 1.2f;
    [Tooltip("ゲージがこれ以上で止めたら「パーフェクト!」(1が最大)")]
    [Range(0f, 1f)] public float clashPerfect = 0.95f;
    [Tooltip("勝ち負けを表示しておく秒数")]
    public float clashResultTime = 1.2f;
    [Tooltip("ゲージの高さ(棚の画面。中心が0、下の端が-5)")]
    public float clashGaugeY = -3.6f;

    [Header("棚の画面(画面2)の向き")]
    [Tooltip("横: 今までどおり / 縦: 縦長の配置(横のモニターでは真ん中に縦長で表示) / 縦(右回り・左回り): Windows は横のままモニターだけ90度回したとき用(逆さに見えたらもう一方)。再生中に変えてもすぐ反映")]
    public ShopOrientation shopOrientation = ShopOrientation.Landscape;
    [Tooltip("縦のときの画面の横:縦の比率(9:16なら0.5625)。モニターが決まったら合わせる")]
    public float portraitAspect = 9f / 16f;
    [Tooltip("縦のときに映る横幅。小さいほど棚が大きく映る")]
    public float portraitViewWidth = 8f;
    [Tooltip("縦のときの棚の列の数")]
    public int portraitColumns = 2;
    [Tooltip("縦のとき、上を空ける割合(タイマー用。0.2なら上の2割)")]
    [Range(0f, 0.5f)] public float portraitTopSpace = 0.2f;
    [Tooltip("ONなら、空けた場所に「TIMER」の目印を出す(タイマーができたら消す)")]
    public bool showTimerPlaceholder = true;

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
        public readonly List<GameObject> inBag = new List<GameObject>();     // 袋に入った商品
        public Bag bag;
        public int total;                // 袋に入った商品の合計金額
        public GameObject lastTaken;     // 最後に棚から取った商品(掴み合いの判定用)
        public float lastTakenTime;
        public Bounds lastTakenBounds;   // 取ったときの棚での範囲
        public Vector3 lastTakenShelfPos;
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
    static Sprite gaugeSprite;
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
        ApplyShopLayout();

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
            var slot = new ShelfSlot { pos = ShelfSlotPosition(i, cols.Length), circle = i % 2 == 1, color = cols[i] };
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

    System.Collections.IEnumerator RefillLater(ShelfSlot slot, GameObject taken)
    {
        yield return new WaitForSeconds(refillDelay);
        // 掴み合い中は、取り合っている商品が棚に戻っているので補充を待つ
        while (clash != null && clash.product == taken) yield return null;
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

        bool press1 = !joined1 && Grab1PPressed();   // 参加したフレームのAボタンは「掴む」として扱わない
        bool press2 = !joined2 && Grab2PPressed();

        if (clash != null)
        {
            // 掴み合い中: カーソルは止めて、Aボタンはゲージを止めるのに使う
            MoveCursor(p1, Vector2.zero);
            MoveCursor(p2, Vector2.zero);
            UpdateClash(press1, press2);
            return;
        }

        MoveCursor(p1, GetMove1P());
        MoveCursor(p2, GetMove2P());

        if (press1) PressAction(p1);
        if (press2) PressAction(p2);
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
        if (p.inShop) p.shopPos = ClampToShopView(p.shopPos + delta);
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
        // もう1人が grabWindow 秒以内に取った商品の場所なら、掴み合い
        var other = p == p1 ? p2 : p1;
        if (grabWindow > 0f && other.lastTaken != null && other.held == other.lastTaken &&
            Time.time - other.lastTakenTime <= grabWindow &&
            other.lastTakenBounds.Contains(new Vector3(p.shopPos.x, p.shopPos.y, other.lastTakenBounds.center.z)))
        {
            var taken = other.lastTaken;
            // 取った人の画面から棚へ戻す
            other.held = null;
            other.inShop = true;
            other.lastTaken = null;
            SetLayer(taken, layerShop);
            taken.transform.position = other.lastTakenShelfPos;
            StartClash(taken);
            return;
        }

        Collider2D col = Physics2D.OverlapPoint(p.shopPos, 1 << layerShop);
        if (col == null || !products.TryGetValue(col, out var go)) return;
        TakeProduct(p, go);
    }

    // 商品を自分の画面へ移し、カーソルも自分の画面へ
    void TakeProduct(Player p, GameObject go)
    {
        // 掴み合いの判定用に、棚での場所を覚えておく
        p.lastTaken = go;
        p.lastTakenTime = Time.time;
        p.lastTakenShelfPos = go.transform.position;
        var cols = go.GetComponentsInChildren<Collider2D>();
        p.lastTakenBounds = cols.Length > 0 ? cols[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var c in cols) p.lastTakenBounds.Encapsulate(c.bounds);

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
            StartCoroutine(RefillLater(slot, go));
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