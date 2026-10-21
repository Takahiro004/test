using UnityEngine;

// 棚の画面(画面2)の棚(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「棚の段と列・棚の見た目」)
//  - 段と列の数から、商品を置く場所(マス)を決める。商品の数 = 段 x 列
//  - 仮の棚(背板・柱・段の板)を描き、商品を板の上に乗せる(showShelf が OFF なら、今までどおりマスの真ん中に浮かせる)
//  - 棚の画像(shelfSprite)か 3Dモデル(shelfModel)を入れたら、仮の棚の代わりにそれを出す
//  - 再生中に段・列・見た目を変えたら、LateUpdate で作り直す
//  - フェーズ(shelfSlide): 棚は横に長く、画面に映っているのはその一部(1フェーズぶん = 段 x 列)。
//    shelfPhaseTime 秒ごとに次のフェーズになり、棚が商品ごと横にスライドして、右にあった棚が映る。
//    棚の i 番目の商品は、フェーズ i / (段 x 列) の棚に置く(商品の一覧の順。1フェーズ目は一覧の最初の 段 x 列 個)
public partial class TwoPlayerShop2D
{
    Transform shelfRoot;   // 棚の見た目(仮の棚 / 画像 / 3Dモデル)をまとめる親
    (int cols, int rows, bool show, float board, Color back, Color boardColor, Sprite sprite, GameObject model,
     Vector3 offset, Vector3 rotation, float scale, ShopOrientation orientation, float aspect, float width, float top) appliedShelf;

    int ShelfColumns => Mathf.Max(1, IsPortrait ? portraitColumns : landscapeColumns);
    int ShelfRows => Mathf.Max(1, IsPortrait ? portraitRows : landscapeRows);
    int ShelfCount => ShelfColumns * ShelfRows;                 // 1フェーズの棚に置く数
    int TotalSlotCount => ShelfCount * PhaseCount;              // 全部のフェーズの棚に置く数

    // フェーズの数(0 なら商品の一覧の数から: 一覧を全部並べられるだけ)
    int PhaseCount
    {
        get
        {
            if (!shelfSlide) return 1;
            if (shelfPhaseCount > 0) return shelfPhaseCount;
            int n = productList != null && productList.Length > 0 ? productList.Length : ShelfCount;
            return Mathf.Max(1, Mathf.CeilToInt(n / (float)ShelfCount));
        }
    }

    // 1フェーズぶんの棚の横幅(隣のフェーズの棚は、この分だけ右にある。画面の外に出るように、画面の横幅以上にする)
    float ShelfSectionWidth => Mathf.Max(ShelfArea().width, ShopViewSize().x) + 0.4f;

    float shelfScroll;                 // 今どれだけ左へスライドしているか(ゲーム内の長さ)
    float slideFrom, slideTo, slideTime;
    int shelfPhase, appliedPhaseCount;

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

    // 棚の i 番目(0始まり。フェーズの棚ごとに、左上から右へ)の場所。今のスライドの分もずらす
    // 棚を出すときは「板の上の面」の高さ、出さないときはマスの真ん中
    Vector2 ShelfSlotPosition(int i)
    {
        Rect a = ShelfArea();
        int cols = ShelfColumns, rows = ShelfRows;
        float cellW = a.width / cols, cellH = a.height / rows;
        int section = i / ShelfCount, j = i % ShelfCount;   // どのフェーズの棚の、何番目か
        float x = a.xMin + cellW * (j % cols + 0.5f) + section * ShelfSectionWidth - shelfScroll;
        float rowTop = a.yMax - cellH * (j / cols);
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
            infoOf.Remove(go);
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
        if (now == appliedShelf && PhaseCount == appliedPhaseCount) return;
        ApplyShopLayout();   // 商品を並べ直して(数が変わったら作り直して)、棚の見た目を作り直す
    }

    (int, int, bool, float, Color, Color, Sprite, GameObject, Vector3, Vector3, float, ShopOrientation, float, float, float) CurrentShelfSettings()
        => (ShelfColumns, ShelfRows, showShelf, shelfBoardThickness, shelfBackColor, shelfBoardColor, shelfSprite, shelfModel,
            shelfModelOffset, shelfModelRotation, shelfModelScale, shopOrientation, portraitAspect, portraitViewWidth, portraitTopSpace);

    // 棚の見た目を作り直す(ApplyShopLayout から呼ぶ)
    void BuildShelfVisual()
    {
        appliedShelf = CurrentShelfSettings();
        appliedPhaseCount = PhaseCount;
        if (shelfRoot != null) Destroy(shelfRoot.gameObject);
        shelfRoot = null;
        if (!showShelf) return;

        shelfRoot = new GameObject("Shelf").transform;
        shelfRoot.gameObject.layer = layerShop;
        Rect a = ShelfArea();
        const float frame = 0.2f;   // 枠の太さ(棚の範囲の外側にはみ出す分)
        // フェーズの数だけ、右へ並べて作る(作るときは shelfRoot を原点に置き、あとでスライドの分だけ動かす)
        for (int k = 0; k < PhaseCount; k++)
        {
            float dx = k * ShelfSectionWidth;
            Rect ak = new Rect(a.x + dx, a.y, a.width, a.height);
            Rect outer = new Rect(ak.xMin - frame, ak.yMin - frame, ak.width + frame * 2f, ak.height + frame * 2f);

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
            else BuildPlaceholderShelf(ak, outer, frame);
        }
        BuildPriceTags(a);   // 値札(TwoPlayerShop2D.Products.cs)
        shelfRoot.position = new Vector3(-shelfScroll, 0f, 0f);
    }

    // ---------- フェーズ(棚が横にスライドする) ----------

    // 今のフェーズ(0始まり)。ゲーム開始からの時間で決まる(エントリー中は 0)
    int CurrentPhase()
    {
        if (!shelfSlide || InEntry || entryPhase == -1 || timeLeft < 0f) return 0;
        float elapsed = Mathf.Max(0f, gameTime) - timeLeft;
        return Mathf.Clamp(Mathf.FloorToInt(elapsed / Mathf.Max(1f, shelfPhaseTime)), 0, PhaseCount - 1);
    }

    // LateUpdate から毎フレーム: フェーズが変わったら、棚を商品ごと横にスライドさせる
    void UpdateShelfScroll()
    {
        int phase = CurrentPhase();
        if (phase != shelfPhase)
        {
            shelfPhase = phase;
            slideFrom = shelfScroll;
            slideTime = 0f;
            lastEvent = $"フェーズ{phase + 1}！ 棚がスライド";
        }
        slideTo = shelfPhase * ShelfSectionWidth;
        if (Mathf.Approximately(shelfScroll, slideTo)) return;

        slideTime += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, slideTime / Mathf.Max(0.05f, shelfSlideTime));   // ゆっくり動き出して、ゆっくり止まる
        shelfScroll = k >= 1f ? slideTo : Mathf.Lerp(slideFrom, slideTo, k);
        ApplyShelfScroll();
    }

    // 今のスライドの分だけ、棚の見た目と棚の商品を動かす
    void ApplyShelfScroll()
    {
        if (shelfRoot != null) shelfRoot.position = new Vector3(-shelfScroll, 0f, 0f);
        for (int i = 0; i < slots.Count; i++) slots[i].pos = ShelfSlotPosition(i);
        foreach (var kv in slotOf)
            if (kv.Key != null) PutOnShelf(kv.Key, kv.Value);
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
