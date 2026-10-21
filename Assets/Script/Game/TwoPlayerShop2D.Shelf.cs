using UnityEngine;

// 棚の画面(画面2)の棚(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「棚の段と列・棚の見た目」)
//  - 段と列の数から、商品を置く場所(マス)を決める。商品の数 = 段 x 列
//  - 仮の棚(背板・柱・段の板)を描き、商品を板の上に乗せる(showShelf が OFF なら、今までどおりマスの真ん中に浮かせる)
//  - 棚の画像(shelfSprite)か 3Dモデル(shelfModel)を入れたら、仮の棚の代わりにそれを出す
//  - 再生中に段・列・見た目を変えたら、LateUpdate で作り直す
//  - スクロール(shelfSlide): 棚は横に長くつながっていて、ゆっくり左から右へ流れ続ける(shelfScrollSpeed)。画面に映っているのはその一部。
//    棚は「1画面ぶんの棚(段 x 列)」を2つ輪のようにつないだもの。右の画面の外へ出た棚は、左の画面の外へ回って、また入ってくる。
//    回るとき(画面の外)に、商品を入れ替える(ランダム補充なら山札から引き直す。TwoPlayerShop2D.Deck.cs)
//  - 商品は、全部が画面に映っていないと取れない(CanGrabOnShelf)
public partial class TwoPlayerShop2D
{
    Transform shelfRoot;   // 棚の見た目(仮の棚 / 画像 / 3Dモデル)をまとめる親
    (int cols, int rows, bool show, float board, Color back, Color boardColor, Sprite sprite, GameObject model,
     Vector3 offset, Vector3 rotation, float scale, ShopOrientation orientation, float aspect, float width, float top) appliedShelf;

    int ShelfColumns => Mathf.Max(1, IsPortrait ? portraitColumns : landscapeColumns);
    int ShelfRows => Mathf.Max(1, IsPortrait ? portraitRows : landscapeRows);
    int ShelfCount => ShelfColumns * ShelfRows;                 // 1画面ぶんの棚に置く数
    int RingSections => shelfSlide ? 2 : 1;                    // 輪にする棚の数(流れるときは2つ)
    int TotalSlotCount => ShelfCount * RingSections;            // 全部の棚に置く数

    // 1画面ぶんの棚の横幅(隣の棚は、この分だけ横にある。画面の外に出るように、画面の横幅以上にする)
    float ShelfSectionWidth => Mathf.Max(ShelfArea().width, ShopViewSize().x) + 0.4f;

    float shelfScroll;                 // 今どれだけ右へ流れたか(ゲーム内の長さ)
    int appliedPhaseCount;             // 作ったときの RingSections
    readonly System.Collections.Generic.List<Transform> shelfSections = new System.Collections.Generic.List<Transform>();
    float[] sectionPrevOffset = new float[0];

    // k 番目の棚の、今の横の位置(流れた分をずらし、輪の端を超えたら反対側へ回す)
    float SectionOffset(int k)
    {
        float ring = ShelfSectionWidth * RingSections;
        return Mathf.Repeat(k * ShelfSectionWidth + shelfScroll + ring / 2f, ring) - ring / 2f;
    }

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

    // 棚の i 番目(0始まり。1画面ぶんの棚ごとに、左上から右へ)の場所。今流れている分もずらす
    // 棚を出すときは「板の上の面」の高さ、出さないときはマスの真ん中
    Vector2 ShelfSlotPosition(int i)
    {
        Rect a = ShelfArea();
        int cols = ShelfColumns, rows = ShelfRows;
        float cellW = a.width / cols, cellH = a.height / rows;
        int section = i / ShelfCount, j = i % ShelfCount;   // どの棚の、何番目か
        float x = a.xMin + cellW * (j % cols + 0.5f) + SectionOffset(section);
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
        foreach (var go in new System.Collections.Generic.List<GameObject>(slotOf.Keys)) ForgetShelfProduct(go);
        slotOf.Clear();
        slots.Clear();   // 補充待ちの古い場所は RefillLater で無視される
        CreateProducts();
    }

    // 棚に置いてある商品を消す(覚えている情報も消す)
    void ForgetShelfProduct(GameObject go)
    {
        if (go == null) return;
        if (slotOf.TryGetValue(go, out var slot) && slot.go == go) slot.go = null;
        slotOf.Remove(go);
        foreach (var c in go.GetComponentsInChildren<Collider2D>()) products.Remove(c);
        originalColors.Remove(go);
        priceOf.Remove(go);
        infoOf.Remove(go);
        Destroy(go);
    }

    // 再生中に棚の設定が変わったか(LateUpdate から毎フレーム)
    void UpdateShelfIfChanged()
    {
        var now = CurrentShelfSettings();
        if (now == appliedShelf && RingSections == appliedPhaseCount) return;
        ApplyShopLayout();   // 商品を並べ直して(数が変わったら作り直して)、棚の見た目を作り直す
    }

    (int, int, bool, float, Color, Color, Sprite, GameObject, Vector3, Vector3, float, ShopOrientation, float, float, float) CurrentShelfSettings()
        => (ShelfColumns, ShelfRows, showShelf, shelfBoardThickness, shelfBackColor, shelfBoardColor, shelfSprite, shelfModel,
            shelfModelOffset, shelfModelRotation, shelfModelScale, shopOrientation, portraitAspect, portraitViewWidth, portraitTopSpace);

    // 棚の見た目を作り直す(ApplyShopLayout から呼ぶ)
    void BuildShelfVisual()
    {
        appliedShelf = CurrentShelfSettings();
        appliedPhaseCount = RingSections;
        if (shelfRoot != null) Destroy(shelfRoot.gameObject);
        shelfRoot = null;
        shelfSections.Clear();
        if (!showShelf) return;

        shelfRoot = new GameObject("Shelf").transform;
        shelfRoot.gameObject.layer = layerShop;
        Rect a = ShelfArea();
        const float frame = 0.2f;   // 枠の太さ(棚の範囲の外側にはみ出す分)
        Rect outer = new Rect(a.xMin - frame, a.yMin - frame, a.width + frame * 2f, a.height + frame * 2f);
        // 1画面ぶんの棚を RingSections 個作る(それぞれ原点で作り、あとで SectionOffset の位置へ動かす)
        for (int k = 0; k < RingSections; k++)
        {
            var section = new GameObject($"Section{k + 1}").transform;
            section.gameObject.layer = layerShop;
            section.SetParent(shelfRoot);
            shelfSections.Add(section);

            if (shelfModel != null) BuildShelfModel(section, outer);
            else if (shelfSprite != null)
            {
                // 棚の画像を、棚の範囲いっぱいに引き伸ばす
                var tr = MakeBagPart("ShelfImage", section, layerShop, Color.white, -20, false);
                var sr = tr.GetComponent<SpriteRenderer>();
                sr.sprite = shelfSprite;
                Vector2 size = shelfSprite.bounds.size;
                tr.position = new Vector3(outer.center.x, outer.center.y, 0f);
                tr.localScale = new Vector3(outer.width / Mathf.Max(0.01f, size.x), outer.height / Mathf.Max(0.01f, size.y), 1f);
            }
            else BuildPlaceholderShelf(section, a, outer, frame);
        }
        BuildPriceTags(a);   // 値札(TwoPlayerShop2D.Products.cs)
        for (int k = 0; k < shelfSections.Count; k++) shelfSections[k].position = new Vector3(SectionOffset(k), 0f, 0f);
    }

    // ---------- スクロール(棚がゆっくり左から右へ流れる) ----------

    // LateUpdate から毎フレーム。エントリー中・タイムアップのあとは止まる
    void UpdateShelfScroll()
    {
        if (!shelfSlide || InEntry || entryPhase == -1 || IsTimeUp) return;
        shelfScroll += shelfScrollSpeed * Time.deltaTime;
        ApplyShelfScroll();
    }

    // 今流れている分だけ、棚の見た目と棚の商品を動かす。右の端から左へ回った棚は、商品を入れ替える
    void ApplyShelfScroll()
    {
        int sections = RingSections;
        if (sectionPrevOffset.Length != sections) ResetSectionOffsets();
        float ring = ShelfSectionWidth * sections;
        for (int k = 0; k < sections; k++)
        {
            float off = SectionOffset(k);
            bool wrapped = off < sectionPrevOffset[k] - ring / 2f;   // 右の端から左の端へ回った(画面の外)
            sectionPrevOffset[k] = off;
            if (k < shelfSections.Count) shelfSections[k].position = new Vector3(off, 0f, 0f);
            if (wrapped) RecycleSection(k);
        }
        for (int i = 0; i < slots.Count; i++) slots[i].pos = ShelfSlotPosition(i);
        foreach (var kv in slotOf)
            if (kv.Key != null) PutOnShelf(kv.Key, kv.Value);
    }

    void ResetSectionOffsets()
    {
        sectionPrevOffset = new float[RingSections];
        for (int k = 0; k < sectionPrevOffset.Length; k++) sectionPrevOffset[k] = SectionOffset(k);
    }

    // 画面の外で左へ回った棚の商品を入れ替える(ランダム補充のときだけ。山札へ戻して、引き直す)
    void RecycleSection(int k)
    {
        if (!UseDeck) return;
        for (int i = k * ShelfCount; i < (k + 1) * ShelfCount && i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.go != null)
            {
                ReturnCard(slot.product);
                ForgetShelfProduct(slot.go);
            }
            slot.product = DrawCard();
            if (slot.product >= 0) PlaceProduct(slot);
            else SetSlotTag(slot);
        }
    }

    // 商品が全部画面に映っているか(映っていないと取れない)
    bool CanGrabOnShelf(GameObject go)
    {
        Vector2 half = ShopViewSize() / 2f;
        bool any = false;
        Bounds b = default;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is SpriteRenderer sr && sr.sprite == null) continue;   // 3Dモデルの商品の、絵の無い SpriteRenderer
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return true;
        return b.min.x >= -half.x && b.max.x <= half.x && b.min.y >= -half.y && b.max.y <= half.y;
    }

    // 仮の棚: 背板 → 左右の柱・天板 → 段の板
    void BuildPlaceholderShelf(Transform parent, Rect a, Rect outer, float frame)
    {
        var back = MakeBagPart("Back", parent, layerShop, shelfBackColor, -20, false);
        SetRect(back, outer.center.x, outer.center.y, outer.width, outer.height);
        var left = MakeBagPart("Left", parent, layerShop, shelfBoardColor, -18, false);
        SetRect(left, a.xMin - frame / 2f, outer.center.y, frame, outer.height);
        var right = MakeBagPart("Right", parent, layerShop, shelfBoardColor, -18, false);
        SetRect(right, a.xMax + frame / 2f, outer.center.y, frame, outer.height);
        var topBoard = MakeBagPart("Top", parent, layerShop, shelfBoardColor, -18, false);
        SetRect(topBoard, outer.center.x, a.yMax + frame / 2f, outer.width, frame);

        int rows = ShelfRows;
        float cellH = a.height / rows;
        for (int r = 0; r < rows; r++)
        {
            float bottom = a.yMax - cellH * (r + 1);   // この段の下の端(板の下の面)
            var board = MakeBagPart($"Board{r + 1}", parent, layerShop, shelfBoardColor, -18, false);
            SetRect(board, a.center.x, bottom + shelfBoardThickness / 2f, a.width, shelfBoardThickness);
            // 板の手前のふちを少し明るくして、板らしく見せる
            var edge = MakeBagPart($"Board{r + 1}Edge", parent, layerShop, Color.Lerp(shelfBoardColor, Color.white, 0.3f), -17, false);
            SetRect(edge, a.center.x, bottom + shelfBoardThickness - 0.03f, a.width, 0.06f);
        }
    }

    // 3Dモデルの棚: 棚の範囲に収まる大きさにして、商品より奥に置く
    void BuildShelfModel(Transform parent, Rect outer)
    {
        var model = Instantiate(shelfModel, parent);
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
