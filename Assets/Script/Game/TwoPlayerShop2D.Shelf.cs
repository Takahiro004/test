using System.Collections.Generic;
using UnityEngine;

// 棚の画面(画面2)の棚(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「棚の段と列・棚の見た目」)
//  - 段と列の数から、商品を置く場所(マス)を決める。商品の数 = 段 x 列
//  - 仮の棚(背板・柱・段の板)を描き、商品を板の上に乗せる(showShelf が OFF なら、今までどおりマスの真ん中に浮かせる)
//  - 棚の画像(shelfSprite)か 3Dモデル(shelfModel)を入れたら、仮の棚の代わりにそれを出す
//  - 再生中に段・列・見た目を変えたら、LateUpdate で作り直す
//  - スライド(shelfSlide): 棚は横に長くつながっていて、画面に映っているのはその一部。
//    shelfSlideInterval 秒ごとに、左から次の棚が shelfSlideTime 秒でスライドしてくる(今の棚は右へ出ていく)。
//    棚は「1画面ぶんの棚(段 x 列)」を2つ輪のようにつないだもの。右の画面の外へ出た棚は、左の画面の外へ回って、また入ってくる。
//    回るとき(画面の外)に、商品を入れ替える(ランダム補充なら山札から引き直す。TwoPlayerShop2D.Deck.cs)
//  - スライドしている間は商品を取れない(TryPickUp)。掴み合い中はスライドを止める
//  - 商品は、全部が画面に映っていないと取れない(CanGrabOnShelf)。
//    ただし棚の1マスより大きい商品(大きいかになど)は、はみ出すのが当たり前なので、真ん中が画面に映っていれば取れる
public partial class TwoPlayerShop2D
{
    Transform shelfRoot;   // 棚の見た目(仮の棚 / 画像 / 3Dモデル)をまとめる親
    (int cols, int rows, bool show, float board, Color back, Color boardColor, Sprite sprite, GameObject model,
     Vector3 offset, Vector3 rotation, Vector3 scale, ShopOrientation orientation, float aspect, float width, float top,
     Vector2 productOffset, bool tags, float tagSize, float tagOutline, Color tagColor, Color tagOutlineColor) appliedShelf;

    int ShelfColumns => Mathf.Max(1, IsPortrait ? portraitColumns : landscapeColumns);
    int ShelfRows => Mathf.Max(1, IsPortrait ? portraitRows : landscapeRows);
    int ShelfCount => ShelfColumns * ShelfRows;                 // 1画面ぶんの棚に置く数
    int RingSections => shelfSlide ? 2 : 1;                    // 輪にする棚の数(流れるときは2つ)
    int TotalSlotCount => ShelfCount * RingSections;            // 全部の棚に置く数

    // 1画面ぶんの棚の横幅(隣の棚は、この分だけ横にある。画面の外に出るように、画面の横幅以上にする)
    float ShelfSectionWidth => Mathf.Max(ShelfArea().width, ShopViewSize().x) + 0.4f;

    float shelfScroll;                 // 今どれだけ右へ流れたか(ゲーム内の長さ)
    float slideWait, slideElapsed, slideFrom;   // 次のスライドまで待った秒数 / スライド中の経過 / スライドを始めた位置
    bool sliding;
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
        // shelfProductOffset だけ棚の商品をまとめてずらし、さらに段ごとに shelfRowOffsets だけずらす(X 右・Y 上)
        Vector2 shift = ProductShift(j / cols);
        float x = a.xMin + cellW * (j % cols + 0.5f) + SectionOffset(section) + shift.x;
        float rowTop = a.yMax - cellH * (j / cols) + shift.y;
        if (!showShelf) return new Vector2(x, rowTop - cellH / 2f);
        return new Vector2(x, rowTop - cellH + shelfBoardThickness);
    }

    // 棚の商品のずらし: 全部まとめて(shelfProductOffset) + その段だけ(shelfRowOffsets。0 が一番上の段)
    Vector2 ProductShift(int row)
    {
        Vector2 shift = shelfProductOffset;
        if (shelfRowOffsets != null && row >= 0 && row < shelfRowOffsets.Length) shift += shelfRowOffsets[row];
        return shift;
    }

    // 段ごとのずらしを再生中に変えたか(配列は中身を比べる)
    Vector2[] appliedRowOffsets = new Vector2[0];
    bool RowOffsetsChanged()
    {
        var now = shelfRowOffsets ?? new Vector2[0];
        if (now.Length != appliedRowOffsets.Length) return true;
        for (int i = 0; i < now.Length; i++) if (now[i] != appliedRowOffsets[i]) return true;
        return false;
    }

    // 棚に置いてある商品を、その場所へ動かす(棚を出すときは、商品の一番下を板の上に合わせる)
    // 乱雑に置く: 商品ごとの向きと横のずれ(初めて置くときに1回だけ決めて覚える。毎フレーム置き直しても変わらない。
    // 補充で新しく置いた商品は、また別の向き)。向きは、その向きの幅が1マスに入るものを選ぶので、隣の商品と重ならない。
    // 横の位置も、1マスの中の空いている所だけで動かす(列にそろって見えないように)。掴んでも向きはそのまま
    class ShelfPose { public float angle, dx, cellW; public bool messy, scatter; }
    readonly Dictionary<GameObject, ShelfPose> shelfPoses = new Dictionary<GameObject, ShelfPose>();

    void PutOnShelf(GameObject go, ShelfSlot slot)
    {
        float cellW = ShelfArea().width / ShelfColumns;
        if (!shelfPoses.TryGetValue(go, out var pose) || Mathf.Abs(pose.cellW - cellW) > 0.001f || pose.messy != shelfMessy || pose.scatter != shelfMessyScatter)
        {
            if (shelfPoses.Count > 300) CleanShelfPoses();
            pose = ChooseShelfPose(go, slot, cellW);
            shelfPoses[go] = pose;
        }
        go.transform.rotation = Quaternion.Euler(0f, 0f, pose.angle);
        go.transform.position = (Vector3)slot.pos + new Vector3(pose.dx, 0f, 0f);
        if (showShelf) go.transform.position += new Vector3(0f, slot.pos.y - LowestY(go), 0f);
        PlaceTagNear(go, slot);
    }

    // 値札を商品のすぐ上(横は商品の真ん中)に置く。上の段にかからないように、その段の上のはし(slot.tagTop)より上には出さない
    void PlaceTagNear(GameObject go, ShelfSlot slot)
    {
        if (slot.tag == null || go == null || !ShapeRect(go, out Rect r)) return;
        float y = Mathf.Min(r.yMax + priceTagGap, slot.tagTop);
        slot.tag.transform.position = new Vector3(r.center.x, y, slot.tag.transform.position.z);
    }

    // 商品の向きと横のずれを決める(何回か向きを試して、幅が1マスに入る向きにする)
    ShelfPose ChooseShelfPose(GameObject go, ShelfSlot slot, float cellW)
    {
        if (!shelfMessy && !shelfMessyScatter) return new ShelfPose { angle = 0f, dx = 0f, cellW = cellW };   // 両方 OFF: まっすぐ・マスの真ん中
        int id = go.GetInstanceID();
        float room = cellW * 0.96f;   // 隣とのあいだに少しだけすき間
        // 高さ: 1段の高さから板の厚さを引いた分(上の段の商品にぶつからないように)
        float roomH = (ShelfArea().height / ShelfRows - (showShelf ? shelfBoardThickness : 0f)) * 0.96f;
        float bestAngle = 0f, bestW = float.MaxValue, bestC = 0f, bestOver = float.MaxValue;
        const int tries = 24;
        for (int t = 0; t < tries; t++)
        {
            float angle = shelfMessy ? (Hash01(id, 10 + t) * 2f - 1f) * shelfMessyAngleMax : 0f;   // OFF: まっすぐ
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.position = slot.pos;
            if (!ShapeRect(go, out Rect r)) { bestAngle = angle; bestW = 0f; bestC = 0f; break; }
            // はみ出し具合(横も縦も入れば 1 以下)。いちばん小さい向きを選ぶ
            float over = Mathf.Max(r.width / Mathf.Max(0.01f, room), r.height / Mathf.Max(0.01f, roomH));
            if (over < bestOver) { bestOver = over; bestAngle = angle; bestW = r.width; bestC = r.center.x - slot.pos.x; }
            if (over <= 1f || !shelfMessy) break;   // 横(隣)にも縦(上の段)にもぶつからない向きが見つかった(まっすぐなら1回だけ)
        }
        if (bestOver > 1f && shelfMessy && warnedBigProducts.Add(go.name))
            Debug.Log($"[Shop2D] {go.name} はどの向きでも棚の1マスに入りきらない(いちばん小さい向きで {bestOver:0.00} 倍)。隣や上の段に少しかかる。表の大きさを小さくするか、段・列を減らす");
        float free = Mathf.Max(0f, room - bestW);
        float jitter = (Hash01(id, 2) * 2f - 1f) * free / 2f * Mathf.Clamp01(shelfMessySpread);
        // マスにそろえるときは、ずらさない(前と同じ置き方)
        float dx = shelfMessyScatter ? jitter - bestC : 0f;
        return new ShelfPose { angle = bestAngle, dx = dx, cellW = cellW, messy = shelfMessy, scatter = shelfMessyScatter };
    }

    readonly HashSet<string> warnedBigProducts = new HashSet<string>();   // 1マスに入らないと知らせた商品(1回だけ)

    // 消えた商品の向きを忘れる
    void CleanShelfPoses()
    {
        var dead = new List<GameObject>();
        foreach (var kv in shelfPoses) if (kv.Key == null || !slotOf.ContainsKey(kv.Key)) dead.Add(kv.Key);
        foreach (var k in dead) shelfPoses.Remove(k);
    }

    // 0〜1 の決まった乱数(同じ id と salt なら同じ値)
    static float Hash01(int id, int salt)
    {
        unchecked
        {
            uint x = (uint)id * 2654435761u ^ (uint)salt * 40503u;
            x ^= x >> 15; x *= 0x2c1b3c6du; x ^= x >> 12; x *= 0x297a2d39u; x ^= x >> 15;
            return (x & 0xFFFFFF) / (float)0xFFFFFF;
        }
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
    bool appliedMessy = true, appliedScatter = true;
    void UpdateShelfIfChanged()
    {
        // 乱雑に置くの ON/OFF を変えたら、棚の商品を置き直す(棚が流れていないときも)
        if (shelfMessy != appliedMessy || shelfMessyScatter != appliedScatter)
        {
            appliedMessy = shelfMessy;
            appliedScatter = shelfMessyScatter;
            foreach (var kv in slotOf) if (kv.Key != null) PutOnShelf(kv.Key, kv.Value);
        }
        var now = CurrentShelfSettings();
        if (now == appliedShelf && RingSections == appliedPhaseCount && !RowOffsetsChanged()) return;
        ApplyShopLayout();   // 商品を並べ直して(数が変わったら作り直して)、棚の見た目を作り直す
    }

    (int, int, bool, float, Color, Color, Sprite, GameObject, Vector3, Vector3, Vector3, ShopOrientation, float, float, float, Vector2, bool, float, float, Color, Color) CurrentShelfSettings()
        => (ShelfColumns, ShelfRows, showShelf, shelfBoardThickness, shelfBackColor, shelfBoardColor, shelfSprite, shelfModel,
            shelfModelOffset, shelfModelRotation, shelfModelScale, shopOrientation, portraitAspect, portraitViewWidth, portraitTopSpace,
            shelfProductOffset, showPriceTags, priceTagSize, priceTagBorderWidth, priceTagFontColor, priceTagBorderColor);

    // 棚の見た目を作り直す(ApplyShopLayout から呼ぶ)
    void BuildShelfVisual()
    {
        appliedShelf = CurrentShelfSettings();
        appliedRowOffsets = (Vector2[])(shelfRowOffsets ?? new Vector2[0]).Clone();
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

    // LateUpdate から毎フレーム。shelfScrollContinuous(初め ON)なら止めずにゆっくり流し続ける(棚1つぶん shelfScrollSectionTime 秒)。
    // OFF なら前の動き: shelfSlideInterval 秒待つ → 棚1つぶん右へスライド → また待つ。エントリー中・タイムアップのあとは止まる
    void UpdateShelfScroll()
    {
        if (!shelfSlide || InEntry || entryPhase == -1 || IsTimeUp) return;
        if (clash != null) return;   // 掴み合い中は止める(取り合っている商品が棚に戻っているので、置いていかれないように)
        // ずっと流す: 止まらずに同じ速さで右へ(区切りなし。sliding にしないので、流れている間も取れる)
        if (shelfScrollContinuous)
        {
            sliding = false;
            shelfScroll += ShelfSectionWidth / Mathf.Max(0.5f, shelfScrollSectionTime) * Time.deltaTime;
            ApplyShelfScroll();
            return;
        }
        if (!sliding)
        {
            slideWait += Time.deltaTime;
            if (slideWait < Mathf.Max(0.1f, shelfSlideInterval)) return;
            sliding = true;
            slideElapsed = 0f;
            slideFrom = shelfScroll;
        }
        slideElapsed += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, slideElapsed / Mathf.Max(0.05f, shelfSlideTime));   // ゆっくり動き出して、ゆっくり止まる
        shelfScroll = slideFrom + ShelfSectionWidth * k;
        if (k >= 1f) { sliding = false; slideWait = 0f; }
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
    // 見た目の絵の四角(透明なふちも入る)ではなく、当たり判定の形で調べる。
    // 絵の四角で調べると、長方形・三角など絵の上下に透明な所がある形や、余白のある画像が、
    // 一番下の段(板の下がすぐ画面の端)で「はみ出している」ことになって取れなかった
    bool CanGrabOnShelf(GameObject go)
    {
        Vector2 half = ShopViewSize() / 2f + new Vector2(0.02f, 0.02f);   // 少しだけゆとり
        Rect r;
        if (!ShapeRect(go, out r)) return true;
        // 1マスより大きい商品は例外: 真ん中が画面に映っていれば取れる(全部は映りきらないため。2026-11-04 プログラマー)
        Rect a = ShelfArea();
        if (r.width > a.width / ShelfColumns || r.height > a.height / ShelfRows)
            return Mathf.Abs(r.center.x) <= half.x && Mathf.Abs(r.center.y) <= half.y;
        return r.xMin >= -half.x && r.xMax <= half.x && r.yMin >= -half.y && r.yMax <= half.y;
    }

    // 商品の当たり判定の形が入る四角(今の位置・向き・大きさで計算する。物理の更新を待たない)
    static bool ShapeRect(GameObject go, out Rect rect)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        void Add(Vector3 w) { minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x); minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y); }

        foreach (var col in go.GetComponentsInChildren<Collider2D>())
        {
            var t = col.transform;
            if (col is PolygonCollider2D poly)
            {
                for (int p = 0; p < poly.pathCount; p++)
                    foreach (var pt in poly.GetPath(p)) Add(t.TransformPoint(pt + poly.offset));
            }
            else if (col is BoxCollider2D box)
            {
                Vector2 h = box.size / 2f;
                Add(t.TransformPoint(box.offset + new Vector2(-h.x, -h.y)));
                Add(t.TransformPoint(box.offset + new Vector2(h.x, -h.y)));
                Add(t.TransformPoint(box.offset + new Vector2(h.x, h.y)));
                Add(t.TransformPoint(box.offset + new Vector2(-h.x, h.y)));
            }
            else if (col is CircleCollider2D circle)
            {
                Vector3 c = t.TransformPoint(circle.offset);
                float rad = circle.radius * Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
                Add(c - new Vector3(rad, rad)); Add(c + new Vector3(rad, rad));
            }
            else { Add(col.bounds.min); Add(col.bounds.max); }
        }
        rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        return minX <= maxX;
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

    // 3Dモデルの棚: 棚の範囲に収まる大きさにして、商品より奥に置く。
    // 大きさは入れ物(ShelfModel)で変え、モデルはその中で向きだけ変える(向きを変えても、横・縦・奥行きは画面の向きのまま)
    void BuildShelfModel(Transform parent, Rect outer)
    {
        var holder = new GameObject("ShelfModel").transform;
        holder.gameObject.layer = layerShop;
        holder.SetParent(parent);
        holder.position = Vector3.zero;
        var model = Instantiate(shelfModel, holder);
        model.name = "Model";
        SetLayer(model, layerShop);
        // プレハブで付けた向きはそのまま残し、その上に shelfModelRotation を足す(商品の3Dモデルと同じ)
        model.transform.localRotation = Quaternion.Euler(shelfModelRotation) * shelfModel.transform.localRotation;
        model.transform.localScale = Vector3.one;
        model.transform.localPosition = Vector3.zero;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.LogWarning("[Shop2D] 棚の3Dモデルに見た目(Renderer)がありません"); return; }
        // 商品の3Dモデルと同じく、画像(スプライト)と同じ順番の決め方で描く(背景の画像に隠れないように)。商品・カーソルより奥
        UseSpriteOrder(renderers);
        foreach (var r in renderers) r.sortingOrder = -20;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        // 縦横の比率は変えずに棚の範囲に収まる大きさにしてから、shelfModelScale で横(X)・縦(Y)・奥行き(Z)を別々にかける
        float fit = Mathf.Min(outer.width / Mathf.Max(0.01f, b.size.x), outer.height / Mathf.Max(0.01f, b.size.y));
        Vector3 scale = fit * new Vector3(Mathf.Max(0.01f, shelfModelScale.x), Mathf.Max(0.01f, shelfModelScale.y), Mathf.Max(0.01f, shelfModelScale.z));
        holder.localScale = scale;

        // 真ん中を棚の範囲の真ん中に合わせ、手前の面が商品(z=0)より奥になるようにする
        Vector3 center = Vector3.Scale(b.center, scale);
        float frontZ = (b.center.z - b.extents.z) * scale.z;
        holder.position = new Vector3(outer.center.x - center.x, outer.center.y - center.y, 1f - frontZ) + shelfModelOffset;
    }
}
