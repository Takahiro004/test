using UnityEngine;

// 棚の画面(画面2)の棚(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「棚の段と列・棚の見た目」)
//  - 段と列の数から、商品を置く場所(マス)を決める。商品の数 = 段 x 列
//  - 仮の棚(背板・柱・段の板)を描き、商品を板の上に乗せる(showShelf が OFF なら、今までどおりマスの真ん中に浮かせる)
//  - 棚の画像(shelfSprite)か 3Dモデル(shelfModel)を入れたら、仮の棚の代わりにそれを出す
//  - 再生中に段・列・見た目を変えたら、LateUpdate で作り直す
public partial class TwoPlayerShop2D
{
    Transform shelfRoot;   // 棚の見た目(仮の棚 / 画像 / 3Dモデル)をまとめる親
    (int cols, int rows, bool show, float board, Color back, Color boardColor, Sprite sprite, GameObject model,
     Vector3 offset, Vector3 rotation, float scale, ShopOrientation orientation, float aspect, float width, float top) appliedShelf;

    int ShelfColumns => Mathf.Max(1, IsPortrait ? portraitColumns : landscapeColumns);
    int ShelfRows => Mathf.Max(1, IsPortrait ? portraitRows : landscapeRows);
    int ShelfCount => ShelfColumns * ShelfRows;

    // 棚を置く範囲(ゲーム内の座標)
    Rect ShelfArea()
    {
        Vector2 view = ShopViewSize();
        if (!IsPortrait)
        {
            // 横: 1マス 横4 x 縦3(今までの 3列 x 2段 と同じ大きさ)。画面に入らなければ縮める
            float w = Mathf.Min(ShelfColumns * 4f, view.x - 1f);
            float h = Mathf.Min(ShelfRows * 3f, view.y - 1f);
            return new Rect(-w / 2f, -h / 2f, w, h);
        }
        // 縦: 上(タイマー用)を空けて、残り全部
        float top = view.y / 2f - view.y * portraitTopSpace;
        return new Rect(-view.x / 2f, -view.y / 2f, view.x, top + view.y / 2f);
    }

    // 棚の i 番目(0始まり。左上から右へ)の場所
    // 棚を出すときは「板の上の面」の高さ、出さないときはマスの真ん中
    Vector2 ShelfSlotPosition(int i)
    {
        Rect a = ShelfArea();
        int cols = ShelfColumns, rows = ShelfRows;
        float cellW = a.width / cols, cellH = a.height / rows;
        float x = a.xMin + cellW * (i % cols + 0.5f);
        float rowTop = a.yMax - cellH * (i / cols);
        if (!showShelf) return new Vector2(x, rowTop - cellH / 2f);
        return new Vector2(x, rowTop - cellH + shelfBoardThickness);
    }

    // 棚に置いてある商品を、その場所へ動かす(棚を出すときは、商品の一番下を板の上に合わせる)
    void PutOnShelf(GameObject go, ShelfSlot slot)
    {
        go.transform.position = slot.pos;
        if (!showShelf) return;
        go.transform.position += new Vector3(0f, slot.pos.y - LowestY(go), 0f);
    }

    // 商品の一番下の高さ(画像は当たり判定の形から。透明なふちの分だけ浮かないように)
    static float LowestY(GameObject go)
    {
        var poly = go.GetComponent<PolygonCollider2D>();
        if (poly != null && poly.pathCount > 0)
        {
            float min = float.MaxValue;
            for (int p = 0; p < poly.pathCount; p++)
                foreach (var pt in poly.GetPath(p))
                    min = Mathf.Min(min, go.transform.TransformPoint(pt + poly.offset).y);
            return min;
        }
        var model = go.transform.Find("Model");   // 3Dモデルの商品(TwoPlayerShop2D.Products.cs)
        if (model != null)
        {
            float min = float.MaxValue;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) min = Mathf.Min(min, r.bounds.min.y);
            if (min < float.MaxValue) return min;
        }
        return go.GetComponent<SpriteRenderer>().bounds.min.y;
    }

    // 段・列の数が変わったら、棚の商品を作り直す(持っている商品・袋の商品はそのまま)
    void RebuildShelfProducts()
    {
        foreach (var kv in slotOf)
        {
            var go = kv.Key;
            if (go == null) continue;
            foreach (var c in go.GetComponentsInChildren<Collider2D>()) products.Remove(c);
            originalColors.Remove(go);
            priceOf.Remove(go);
            Destroy(go);
        }
        slotOf.Clear();
        slots.Clear();   // 補充待ちの古い場所は RefillLater で無視される
        CreateProducts();
    }

    // 再生中に棚の設定が変わったか(LateUpdate から毎フレーム)
    void UpdateShelfIfChanged()
    {
        var now = CurrentShelfSettings();
        if (now == appliedShelf) return;
        ApplyShopLayout();   // 商品を並べ直して(数が変わったら作り直して)、棚の見た目を作り直す
    }

    (int, int, bool, float, Color, Color, Sprite, GameObject, Vector3, Vector3, float, ShopOrientation, float, float, float) CurrentShelfSettings()
        => (ShelfColumns, ShelfRows, showShelf, shelfBoardThickness, shelfBackColor, shelfBoardColor, shelfSprite, shelfModel,
            shelfModelOffset, shelfModelRotation, shelfModelScale, shopOrientation, portraitAspect, portraitViewWidth, portraitTopSpace);

    // 棚の見た目を作り直す(ApplyShopLayout から呼ぶ)
    void BuildShelfVisual()
    {
        appliedShelf = CurrentShelfSettings();
        if (shelfRoot != null) Destroy(shelfRoot.gameObject);
        shelfRoot = null;
        if (!showShelf) return;

        shelfRoot = new GameObject("Shelf").transform;
        shelfRoot.gameObject.layer = layerShop;
        Rect a = ShelfArea();
        const float frame = 0.2f;   // 枠の太さ(棚の範囲の外側にはみ出す分)
        Rect outer = new Rect(a.xMin - frame, a.yMin - frame, a.width + frame * 2f, a.height + frame * 2f);

        if (shelfModel != null) BuildShelfModel(outer);
        else if (shelfSprite != null)
        {
            // 棚の画像を、棚の範囲いっぱいに引き伸ばす
            var tr = MakeBagPart("ShelfImage", shelfRoot, layerShop, Color.white, -20, false);
            var sr = tr.GetComponent<SpriteRenderer>();
            sr.sprite = shelfSprite;
            Vector2 size = shelfSprite.bounds.size;
            tr.position = new Vector3(outer.center.x, outer.center.y, 0f);
            tr.localScale = new Vector3(outer.width / Mathf.Max(0.01f, size.x), outer.height / Mathf.Max(0.01f, size.y), 1f);
        }
        else BuildPlaceholderShelf(a, outer, frame);
        BuildPriceTags(a);   // 値札(TwoPlayerShop2D.Products.cs)
    }

    // 仮の棚: 背板 → 左右の柱・天板 → 段の板
    void BuildPlaceholderShelf(Rect a, Rect outer, float frame)
    {
        var back = MakeBagPart("Back", shelfRoot, layerShop, shelfBackColor, -20, false);
        SetRect(back, outer.center.x, outer.center.y, outer.width, outer.height);
        var left = MakeBagPart("Left", shelfRoot, layerShop, shelfBoardColor, -18, false);
        SetRect(left, a.xMin - frame / 2f, outer.center.y, frame, outer.height);
        var right = MakeBagPart("Right", shelfRoot, layerShop, shelfBoardColor, -18, false);
        SetRect(right, a.xMax + frame / 2f, outer.center.y, frame, outer.height);
        var topBoard = MakeBagPart("Top", shelfRoot, layerShop, shelfBoardColor, -18, false);
        SetRect(topBoard, outer.center.x, a.yMax + frame / 2f, outer.width, frame);

        int rows = ShelfRows;
        float cellH = a.height / rows;
        for (int r = 0; r < rows; r++)
        {
            float bottom = a.yMax - cellH * (r + 1);   // この段の下の端(板の下の面)
            var board = MakeBagPart($"Board{r + 1}", shelfRoot, layerShop, shelfBoardColor, -18, false);
            SetRect(board, a.center.x, bottom + shelfBoardThickness / 2f, a.width, shelfBoardThickness);
            // 板の手前のふちを少し明るくして、板らしく見せる
            var edge = MakeBagPart($"Board{r + 1}Edge", shelfRoot, layerShop, Color.Lerp(shelfBoardColor, Color.white, 0.3f), -17, false);
            SetRect(edge, a.center.x, bottom + shelfBoardThickness - 0.03f, a.width, 0.06f);
        }
    }

    // 3Dモデルの棚: 棚の範囲に収まる大きさにして、商品より奥に置く
    void BuildShelfModel(Rect outer)
    {
        var model = Instantiate(shelfModel, shelfRoot);
        model.name = "ShelfModel";
        SetLayer(model, layerShop);
        model.transform.localRotation = Quaternion.Euler(shelfModelRotation);
        model.transform.localScale = Vector3.one;
        model.transform.position = Vector3.zero;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.LogWarning("[Shop2D] 棚の3Dモデルに見た目(Renderer)がありません"); return; }
        foreach (var r in renderers) r.sortingOrder = -20;   // 商品・カーソルより奥に描く

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        // 縦横の比率は変えずに、棚の範囲に収まる大きさにする
        float fit = Mathf.Min(outer.width / Mathf.Max(0.01f, b.size.x), outer.height / Mathf.Max(0.01f, b.size.y));
        float scale = fit * Mathf.Max(0.01f, shelfModelScale);
        model.transform.localScale = Vector3.one * scale;

        // 真ん中を棚の範囲の真ん中に合わせ、手前の面が商品(z=0)より奥になるようにする
        Vector3 center = b.center * scale;
        float frontZ = (b.center.z - b.extents.z) * scale;
        model.transform.position = new Vector3(outer.center.x - center.x, outer.center.y - center.y, 1f - frontZ) + shelfModelOffset;
    }
}
