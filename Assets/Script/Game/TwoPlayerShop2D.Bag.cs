using System.Collections.Generic;
using UnityEngine;

// レジ袋・金額・袋の外の商品を消す(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
public partial class TwoPlayerShop2D
{
    // レジ袋(左右のふち + 底 + 背景 + 合計の表示)。
    // 見た目は3枚の重なり: 一番奥に袋の絵(back。bagSprite)・まん中に袋の中の商品・一番手前に薄い袋の絵(front。bagFrontSprite)
    class Bag
    {
        public Transform root;
        public Rigidbody2D body;   // 揺らすとき用(Kinematic。動かすと中の商品を押す)
        public Transform back, left, right, bottom;
        public Transform front;        // 手前の薄い袋の絵(商品より手前。当たり判定なし)
        public TextMesh totalLabel;
        public bool torn;              // 最後に持ち上げたとき破れた(TwoPlayerShop2D.Weight.cs)
        public BagLook look;           // 3Dモデル・伸び・裂けるアニメーション(TwoPlayerShop2D.BagModel.cs)
    }

    readonly Dictionary<GameObject, float> stoppedTime = new Dictionary<GameObject, float>();   // 袋の外で止まっている時間
    float appliedBagX, appliedBagWidth, appliedBagHeight, appliedBagWall, appliedBagFloorY, appliedBagOuter;

    // ---------- レジ袋 ----------

    Bag CreateBag(Player p)
    {
        var root = new GameObject($"Bag_{p.name}");
        root.layer = p.layer;
        var bag = new Bag { root = root.transform };
        bag.body = root.AddComponent<Rigidbody2D>();
        bag.body.bodyType = RigidbodyType2D.Kinematic;

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

        // 手前の薄い袋の絵(袋の中の商品より手前・持ち運び中の商品(999)より奥。絵は ApplyBagFrameVisibility で毎フレーム入れる)
        bag.front = MakeBagPart("Front", root.transform, p.layer, bagFrontColor, BagFrontOrder, false);
        bag.front.GetComponent<SpriteRenderer>().enabled = false;

        bag.totalLabel = MakeText($"BagTotal_{p.name}", root.transform, p.layer, "¥0", Color.white, 1001);
        SetTextSize(bag.totalLabel, HudTextSize);
        BuildBagModel(p, bag);   // 袋の3Dモデル(TwoPlayerShop2D.BagModel.cs)
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
        appliedBagWall = bagWallThickness; appliedBagFloorY = floorY; appliedBagOuter = bagWallOuterThickness;

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
            // 底の当たり判定だけ下へ厚くする(見た目はそのまま)。商品をたくさん積んでも底を突き抜けにくくする
            var bottomCol = b.bottom.GetComponent<BoxCollider2D>();
            if (bottomCol != null)
            {
                float extra = 0.8f / Mathf.Max(0.01f, t);   // 下へ 0.8 ぶん(ローカルの長さ)
                bottomCol.size = new Vector2(1f, 1f + extra);
                bottomCol.offset = new Vector2(0f, -extra / 2f);
            }
            // 左右のふちも、当たり判定だけ外側へ厚くする(見た目はそのまま)。
            // ふちは見た目どおりだと 0.3 と薄く、小さい商品(図形の商品など)を積むと、重さで押されてすり抜けることがあった
            float side = bagWallOuterThickness / Mathf.Max(0.01f, t);   // 外側へ厚くする分(ローカルの長さ)
            var leftCol = b.left.GetComponent<BoxCollider2D>();
            if (leftCol != null) { leftCol.size = new Vector2(1f + side, 1f); leftCol.offset = new Vector2(-side / 2f, 0f); }
            var rightCol = b.right.GetComponent<BoxCollider2D>();
            if (rightCol != null) { rightCol.size = new Vector2(1f + side, 1f); rightCol.offset = new Vector2(side / 2f, 0f); }
            SetTextSize(b.totalLabel, HudTextSize);
            b.totalLabel.transform.position = new Vector3(bagX, floorY + bagHeight + 0.2f + HudTextSize * 4f, 0f);
            LayoutBagModel(b);
        }
    }

    static void SetRect(Transform tr, float cx, float cy, float w, float h)
    {
        tr.position = new Vector3(cx, cy, 0f);
        tr.localScale = new Vector3(w, h, 1f);
    }

    // 袋の上の合計金額の表示の ON/OFF(エントリー画面のときも、ゲーム中と同じにする。Update から毎フレーム)
    void ApplyBagTotalVisibility()
    {
        foreach (var p in new[] { p1, p2 })
            if (p != null && p.bag != null) p.bag.totalLabel.gameObject.SetActive(showBagTotal);
    }

    const int BagFrontOrder = 900;   // 手前の袋の絵の描く順番(落とした商品は 1, 2, 3…・持ち運び中は 999)

    // 仮の袋(画像も3Dモデルも無い)の白いふちの見た目の ON/OFF・手前の袋の絵(Update から毎フレーム。当たり判定はそのまま)
    void ApplyBagFrameVisibility()
    {
        foreach (var p in new[] { p1, p2 })
        {
            if (p == null || p.bag == null) continue;
            ApplyBagBack(p.bag);
            ApplyBagFront(p.bag);
            bool show = bagFrameVisible && bagSprite == null && (p.bag.look == null || p.bag.look.holder == null);
            p.bag.left.GetComponent<SpriteRenderer>().enabled = show;
            p.bag.right.GetComponent<SpriteRenderer>().enabled = show;
            if (!p.bag.torn) p.bag.bottom.GetComponent<SpriteRenderer>().enabled = show;   // 破れた底は出さない
        }
    }

    // 奥の袋の絵: Bag Sprite(無ければ仮の袋の薄い背景)を、袋の幅・高さぴったりに引き伸ばす。
    // 毎フレーム入れるので、再生中に画像を入れ替えてもすぐ反映する(前は再生を始めたときだけ読んでいた)
    void ApplyBagBack(Bag b)
    {
        if (b.look != null && (b.look.holder != null || b.look.mesh != null)) return;   // 3Dモデル・網目のときは絵を出さない
        var sr = b.back.GetComponent<SpriteRenderer>();
        sr.sprite = bagSprite != null ? bagSprite : GetSquareSprite();
        sr.color = bagSprite != null ? Color.white : new Color(bagColor.r, bagColor.g, bagColor.b, bagColor.a * 0.25f);
        Vector2 size = sr.sprite.bounds.size;
        b.back.localPosition = new Vector3(bagX, floorY + bagHeight / 2f, 0f);
        b.back.localScale = new Vector3(bagWidth / Mathf.Max(size.x, 0.01f), bagHeight / Mathf.Max(size.y, 0.01f), 1f);
    }

    // 手前の薄い袋の絵: 奥の袋の絵と同じく、袋の幅・高さぴったりに引き伸ばす(再生中に絵・色を変えてもすぐ反映)。
    // 3Dモデルの袋のときと、伸びて網目になったあと(TwoPlayerShop2D.BagModel.cs)は、絵のほうは出さない
    void ApplyBagFront(Bag b)
    {
        var sr = b.front.GetComponent<SpriteRenderer>();
        bool meshed = b.look != null && (b.look.holder != null || b.look.mesh != null);
        sr.enabled = bagFrontSprite != null && !meshed;
        if (!sr.enabled) return;
        sr.sprite = bagFrontSprite;
        sr.color = bagFrontColor;
        Vector2 size = bagFrontSprite.bounds.size;
        b.front.localPosition = new Vector3(bagX, floorY + bagHeight / 2f, 0f);
        b.front.localScale = new Vector3(bagWidth / Mathf.Max(size.x, 0.01f), bagHeight / Mathf.Max(size.y, 0.01f), 1f);
    }

    // 落とした商品の中心が袋の内側に入ったら、金額を出して合計に足す(1個につき1回だけ)
    void CheckBag(Player p)
    {
        float t = bagWallThickness;
        float left = bagX - bagWidth / 2f + t, right = bagX + bagWidth / 2f - t;
        float top = floorY + bagHeight;

        for (int i = p.dropped.Count - 1; i >= 0; i--)
        {
            var go = p.dropped[i];
            if (go == null) { p.dropped.RemoveAt(i); continue; }

            // 袋の幅の中で、袋の口より下なら「入った」(底に少しめり込んでいても入ったことにする)
            Vector2 c = go.transform.position;
            if (c.x < left || c.x > right || c.y > top) continue;

            p.dropped.RemoveAt(i);
            p.inBag.Add(go);
            stoppedTime.Remove(go);
            priceOf.TryGetValue(go, out int price);
            price = BagPrice(p, price);   // 献立が完成していたら倍(TwoPlayerShop2D.Menu.cs)
            priceOf[go] = price;          // こぼれたときに引く金額も、袋に入ったときの金額にする
            p.total += price;
            p.bag.totalLabel.text = $"¥{p.total}";
            if (showPricePopup) StartCoroutine(PricePopup(p, c, price));
            CheckMenuMission(p);          // 献立の食材がそろったか

            lastEvent = $"{p.name}の袋に {go.name} が入った +¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }

        if (removeOutsideBag) RemoveOutsideBag(p);
        CheckFragile(p);   // 割れる商品(TwoPlayerShop2D.Fragile.cs)
    }

    // 袋の外に落ちた商品を消す
    //  - 床に着いた商品(袋に入らなかったもの、袋からこぼれたもの)
    //  - 袋に入らないまま、ふちや他の商品の上で止まった商品
    // 袋からこぼれても合計は減らさない
    void RemoveOutsideBag(Player p)
    {
        for (int i = p.dropped.Count - 1; i >= 0; i--)
        {
            var go = p.dropped[i];
            bool remove = TouchesFloor(go);
            if (!remove)
            {
                var rb = go.GetComponent<Rigidbody2D>();
                bool stopped = rb == null || rb.linearVelocity.sqrMagnitude < 0.01f;
                stoppedTime.TryGetValue(go, out float time);
                time = stopped ? time + Time.deltaTime : 0f;
                stoppedTime[go] = time;
                remove = time >= removeAfterStopTime;
            }
            if (!remove) continue;
            p.dropped.RemoveAt(i);
            RemoveProduct(p, go);
        }

        // 袋に入ったあと、袋から出て場外に落ちた商品(床に着いた / 袋の左右の外で止まった / 下へ抜けた)
        // → 消して、その金額を合計から引く。袋の上に積み上がっただけ(袋の幅の中)なら消さない
        float t = bagWallThickness;
        float left = bagX - bagWidth / 2f + t, right = bagX + bagWidth / 2f - t;
        for (int i = p.inBag.Count - 1; i >= 0; i--)
        {
            var go = p.inBag[i];
            if (go == null) { p.inBag.RemoveAt(i); continue; }

            Vector2 c = go.transform.position;
            bool outside = c.x < left || c.x > right;
            // 袋の幅の中にある商品は消さない(積み重なって底にめり込み、床に触れても、袋の中は袋の中)
            bool remove = (outside && TouchesFloor(go)) || c.y < floorY - 3f;
            if (!remove)
            {
                var rb = go.GetComponent<Rigidbody2D>();
                bool stopped = rb == null || rb.linearVelocity.sqrMagnitude < 0.01f;
                stoppedTime.TryGetValue(go, out float time);
                time = outside && stopped ? time + Time.deltaTime : 0f;
                stoppedTime[go] = time;
                remove = time >= removeAfterStopTime;
            }
            if (!remove) continue;

            p.inBag.RemoveAt(i);
            priceOf.TryGetValue(go, out int price);
            if (subtractSpilledFromTotal)
            {
                p.total -= price;
                p.bag.totalLabel.text = $"¥{p.total}";
                StartCoroutine(MinusPopup(p, price));   // 合計金額の近くに赤で出す
            }
            RemoveProduct(p, go);
            lastEvent = $"{p.name}の {go.name} が袋から落ちた -¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }
    }

    bool TouchesFloor(GameObject go)
    {
        foreach (var f in floors)
        {
            var floorCol = f.GetComponent<Collider2D>();
            if (floorCol == null || f.gameObject.layer != go.layer) continue;
            foreach (var c in go.GetComponentsInChildren<Collider2D>())
                if (c.IsTouching(floorCol)) return true;
        }
        return false;
    }

    void RemoveProduct(Player p, GameObject go)
    {
        lastEvent = $"{p.name}の {go.name} は袋の外に落ちたので消えた";
        Debug.Log($"[Shop2D] {lastEvent}");

        foreach (var c in go.GetComponentsInChildren<Collider2D>())
        {
            products.Remove(c);
            c.enabled = false;   // 消えている間に他の商品を支えない
        }
        originalColors.Remove(go);
        priceOf.Remove(go);
        infoOf.Remove(go);
        overloadTime.Remove(go);
        stoppedTime.Remove(go);
        StartCoroutine(ShrinkAndDestroy(go));
    }

    // 小さくなりながら消える
    System.Collections.IEnumerator ShrinkAndDestroy(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        Vector3 start = go.transform.localScale;
        const float duration = 0.25f;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            go.transform.localScale = start * (1f - time / duration);
            yield return null;
        }
        Destroy(go);
    }

    // 金額を表示して、上へ浮かびながら消える
    System.Collections.IEnumerator PricePopup(Player p, Vector2 pos, int price)
    {
        var label = MakeText("PricePopup", null, p.layer, $"+¥{price}", new Color(1f, 0.95f, 0.3f), 1002);
        SetTextSize(label, HudTextSize);
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

    // 合計金額が減ったとき、「-¥金額」を合計金額のすぐ右に赤で出して、上へ浮かびながら消す(袋が揺れても一緒に動く)
    System.Collections.IEnumerator MinusPopup(Player p, int price)
    {
        var total = p.bag.totalLabel;
        var label = MakeText("MinusPopup", total.transform.parent, p.layer, $"-¥{price}", new Color(1f, 0.25f, 0.2f), 1002);
        SetTextSize(label, HudTextSize);
        label.anchor = TextAnchor.MiddleLeft;
        float x = showBagTotal ? total.GetComponent<MeshRenderer>().bounds.max.x + HudTextSize * 2f   // 合計金額の文字の右の端の少し右
                               : total.transform.position.x;                                         // 合計を出していないときは、その場所
        Vector3 start = new Vector3(x, total.transform.position.y, 0f) - total.transform.parent.position;   // 袋の中の位置
        var color = label.color;
        for (float time = 0f; time < pricePopupTime; time += Time.deltaTime)
        {
            float k = time / Mathf.Max(0.05f, pricePopupTime);
            label.transform.localPosition = start + Vector3.up * (k * HudTextSize * 6f);
            label.color = new Color(color.r, color.g, color.b, 1f - k);
            yield return null;
        }
        Destroy(label.gameObject);
    }

    // 合計金額・「+¥」・「LB: キック」などの文字の大きさ(textScale で変える)
    float HudTextSize => 0.08f * Mathf.Max(0.1f, textScale);

    // ---------- 文字(フォント) ----------
    // ゲーム中の文字は全部 MakeText で作る。フォントは Inspector の Text Font(空なら Unity の標準フォント)。
    // 文字は「元の絵」を引き伸ばして表示するので、元の絵を textResolution 倍の大きさで作って、ぼやけにくくしている。
    // 文字の大きさは characterSize に直接入れず、SetTextSize で入れる(元の絵の大きさが変わっても、見た目の大きさが同じになる)
    static int labelFontSize = 64;

    // 再生を始めたときに呼ぶ(フォントと解像度を決める)
    void ApplyTextFont()
    {
        labelFont = textFont != null ? textFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelFontSize = 64 * Mathf.Clamp(textResolution, 1, 4);
    }

    // 文字の大きさを入れる(size は前と同じ意味。元の絵の大きさ 64 のときの characterSize)
    static void SetTextSize(TextMesh tm, float size)
    {
        tm.characterSize = size * 64f / Mathf.Max(1, tm.fontSize);
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
        tm.fontSize = labelFontSize;
        SetTextSize(tm, 0.08f);
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
}
