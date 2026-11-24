using System.Collections;
using UnityEngine;

// おじゃまキッズ(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「おじゃまキッズ」)
// 試合中、残り時間が kidTimes の秒数になったら、小さい子どもが出てきて、2人の袋に勝手に商品を1つずつ入れていく。
//  1. 2P の画面: 右のふちから出てきて、左へ歩いて抜ける(棚へ向かう)
//  2. 棚の画面: 右から真ん中まで歩き、立ち止まってランダムな商品を取り(頭の上に持つ)、右へ戻る(2P の画面へ)
//  3. 2P の画面: 左から出てきて、袋の上のランダムな位置まで歩き、商品を袋へ落とし、左へ戻る
//  4. 棚の画面: 右から左へ横切りながら、真ん中で立ち止まってもう1つランダムな商品を取る
//  5. 1P の画面: 右から袋の上のランダムな位置まで歩き、立ち止まって商品を袋へ落とし、左へ抜ける
//  - 入れる商品は、商品の一覧(productList)の全部からランダム。落とした商品は、ふつうに落とした商品と同じ(袋に入ったら金額・重さに数える)
//  - 子どもの絵は kidSprite(空なら自動生成の子ども)。大きさ kidSize・色 kidColor・1つの画面を横切る秒数 kidCrossTime・歩く高さ kidWalkY(画面の高さの割合)
//  - タイムアップ・袋持ち上げが始まったら、途中でもやめる
public partial class TwoPlayerShop2D
{
    readonly System.Collections.Generic.HashSet<int> kidDone = new System.Collections.Generic.HashSet<int>();   // もう出た回(kidTimes の番号)
    bool kidRunning;
    static Sprite kidDefaultSprite;

    // LateUpdate から毎フレーム
    void UpdateKid()
    {
        if (!kidEnabled || kidTimes == null || InEntry || timeLeft < 0f || IsTimeUp || kidRunning || p1 == null || p2 == null) return;
        for (int i = 0; i < kidTimes.Length; i++)
        {
            if (kidDone.Contains(i) || timeLeft > kidTimes[i]) continue;
            kidDone.Add(i);
            StartCoroutine(KidRun());
            return;
        }
    }

    bool KidShouldStop => IsTimeUp || liftStarted;

    IEnumerator KidRun()
    {
        kidRunning = true;
        var kid = MakeKid();
        var carried = new GameObject("KidCarry").AddComponent<SpriteRenderer>();   // 頭の上に持っている商品
        carried.enabled = false;
        Vector2 pv = PlayerViewSize(), sv = ShopViewSize();
        float pEdge = pv.x / 2f + kidSize, sEdge = sv.x / 2f + kidSize;   // 画面の外(子どもが見えなくなる所)

        // 1. 2P の画面を右から左へ
        yield return KidMove(kid, carried, p2.layer, pEdge, -pEdge, pv.x);
        // 2. 棚の画面: 真ん中まで行って商品を取り、右へ戻る
        if (!KidShouldStop) yield return KidMove(kid, carried, layerShop, sEdge, 0f, sv.x);
        if (!KidShouldStop) { KidGrab(carried); yield return KidPause(kid, carried); }
        if (!KidShouldStop) yield return KidMove(kid, carried, layerShop, 0f, sEdge, sv.x);
        // 3. 2P の画面: 左から袋の上のランダムな位置まで行って落とし、左へ戻る
        float drop2 = KidDropX();
        if (!KidShouldStop) yield return KidMove(kid, carried, p2.layer, -pEdge, drop2, pv.x);
        if (!KidShouldStop) { KidDrop(p2, carried); yield return KidPause(kid, carried); }
        if (!KidShouldStop) yield return KidMove(kid, carried, p2.layer, drop2, -pEdge, pv.x);
        // 4. 棚の画面: 横切りながら、真ん中でもう1つ取る
        if (!KidShouldStop) yield return KidMove(kid, carried, layerShop, sEdge, 0f, sv.x);
        if (!KidShouldStop) { KidGrab(carried); yield return KidPause(kid, carried); }
        if (!KidShouldStop) yield return KidMove(kid, carried, layerShop, 0f, -sEdge, sv.x);
        // 5. 1P の画面: 袋の上のランダムな位置まで行って落とし、左へ抜ける
        float drop1 = KidDropX();
        if (!KidShouldStop) yield return KidMove(kid, carried, p1.layer, pEdge, drop1, pv.x);
        if (!KidShouldStop) { KidDrop(p1, carried); yield return KidPause(kid, carried); }
        if (!KidShouldStop) yield return KidMove(kid, carried, p1.layer, drop1, -pEdge, pv.x);

        Destroy(kid.gameObject);
        Destroy(carried.gameObject);
        kidCarry = null;
        kidRunning = false;
    }

    ProductInfo kidCarry;   // 頭の上に持っている商品

    // 袋の上のランダムな位置(袋の幅の内側)
    float KidDropX() => bagX + Random.Range(-0.35f, 0.35f) * bagWidth;

    // 商品を取る(ランダム。頭の上に持つ)
    void KidGrab(SpriteRenderer carried)
    {
        kidCarry = RandomProduct();
        carried.sprite = ProductIconSprite(kidCarry);
        carried.enabled = carried.sprite != null;
    }

    // 持っている商品を、p の袋の、子どもの真下へ落とす
    void KidDrop(Player p, SpriteRenderer carried)
    {
        if (kidCarry != null) DropRandomProduct(p, kidCarry, carried.transform.position.x);
        kidCarry = null;
        carried.enabled = false;
    }

    // 1つの画面の中を、fromX から toX へ歩く(画面の幅 width を Kid Cross Time 秒で歩く速さ)。歩く向きに絵の向きを合わせる
    IEnumerator KidMove(SpriteRenderer kid, SpriteRenderer carried, int layer, float fromX, float toX, float width)
    {
        SetLayer(kid.gameObject, layer);
        kid.sortingOrder = layer == layerShop ? 1500 : 1800;   // 袋の画面では袋・商品より手前
        SetLayer(carried.gameObject, layer);
        carried.sortingOrder = kid.sortingOrder + 1;
        bool left = toX < fromX;
        kid.flipX = left == kidSpriteFacesRight;   // 絵の向き(右向きの絵を左へ歩かせるときなど)は反転
        float time = Mathf.Max(0.05f, Mathf.Max(0.3f, kidCrossTime) * Mathf.Abs(toX - fromX) / Mathf.Max(0.1f, width + kidSize * 2f));
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (KidShouldStop) yield break;
            PlaceKid(kid, carried, layer, Mathf.Lerp(fromX, toX, t / time), Time.time);
            yield return null;
        }
        PlaceKid(kid, carried, layer, toX, Time.time);
    }

    // 立ち止まる(Kid Pause Time 秒。その場で小さく弾む)
    IEnumerator KidPause(SpriteRenderer kid, SpriteRenderer carried)
    {
        float x = kid.transform.position.x;
        int layer = kid.gameObject.layer;
        for (float s = 0f; s < kidPauseTime; s += Time.deltaTime)
        {
            if (KidShouldStop) yield break;
            PlaceKid(kid, carried, layer, x, Time.time * 0.5f);
            yield return null;
        }
    }

    // 歩く高さ: 袋の画面は袋の上のふちのすぐ上(+ Kid Walk Y)、棚の画面は Kid Shop Walk Y
    float KidY(int layer)
    {
        float h = Mathf.Max(0.1f, kidSize);
        return layer == layerShop ? ShopViewSize().y * (kidShopWalkY - 0.5f)
                                  : BagFloorY + bagHeight + h * 0.5f + kidWalkY + CameraFollowY;
    }

    // 子どもを置く(歩いて上下に弾む・少し左右に揺れる)。持っている商品は頭の上
    void PlaceKid(SpriteRenderer kid, SpriteRenderer carried, int layer, float x, float t)
    {
        float h = Mathf.Max(0.1f, kidSize);
        float y = KidY(layer);
        float bob = Mathf.Abs(Mathf.Sin(t * 10f)) * h * 0.06f;
        kid.transform.position = new Vector3(x, y + bob, 0f);
        kid.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 10f) * 5f);
        float s = h / Mathf.Max(0.01f, kid.sprite.bounds.size.y);
        kid.transform.localScale = new Vector3(s, s, 1f);
        kid.color = kidColor;
        if (carried != null && carried.enabled && carried.sprite != null)
        {
            float cs = h * 0.45f / Mathf.Max(0.01f, Mathf.Max(carried.sprite.bounds.size.x, carried.sprite.bounds.size.y));
            carried.transform.localScale = new Vector3(cs, cs, 1f);
            carried.transform.position = new Vector3(x, y + bob + h * 0.65f, 0f);
        }
    }

    SpriteRenderer MakeKid()
    {
        var sr = new GameObject("Kid").AddComponent<SpriteRenderer>();
        sr.sprite = kidSprite != null ? kidSprite : GetKidSprite();
        return sr;
    }

    ProductInfo RandomProduct()
    {
        if (productList == null || productList.Length == 0) return null;
        for (int tries = 0; tries < 20; tries++)
        {
            var info = productList[Random.Range(0, productList.Length)];
            if (info != null && !string.IsNullOrEmpty(info.name)) return info;
        }
        return null;
    }

    // 商品の小さな絵(頭の上に持つ用): 画像 → 3Dモデルを撮った絵 → 仮の図形
    Sprite ProductIconSprite(ProductInfo info)
    {
        if (info == null) return null;
        if (info.sprite != null) return info.sprite;
        if (info.model != null) { var s = ModelIconSprite(info); if (s != null) return s; }
        return GetShapeSprite(info.shape);
    }

    // p の袋の上(横の位置 x)から、商品を1つ落とす(info が null ならランダム)。ふつうに落とした商品と同じ扱い(袋に入ったら数える)
    void DropRandomProduct(Player p, ProductInfo info, float x)
    {
        if (p == null || p.bag == null) return;
        if (info == null) info = RandomProduct();
        if (info == null) return;
        Vector2 pos = new Vector2(x, BagFloorY + bagHeight + 1f + CameraFollowY);
        var go = info.model != null ? MakeModelProduct(info.name, pos, info, p.layer)
                                    : MakeSprite(info.name, pos, info.shape, RankColor(info.rank), p.layer, info.sprite);
        float scale = info.model != null ? info.modelSize : info.size;
        if (scale > 0f) go.transform.localScale *= scale;
        SetLayer(go, p.layer);
        RegisterProduct(go);
        priceOf[go] = info.price;
        infoOf[go] = info;
        go.GetComponent<SpriteRenderer>().sortingOrder = ++dropOrder;
        foreach (var c in go.GetComponentsInChildren<Collider2D>()) c.isTrigger = false;
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = gravityScale;
        rb.linearVelocity = Vector2.zero;
        rb.freezeRotation = freezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.mass = weightAffectsPhysics ? Mathf.Max(0.01f, WeightOf(go)) : 1f;
        if (!p.dropped.Contains(go)) p.dropped.Add(go);
        lastEvent = $"おじゃまキッズが {p.name}の袋に {info.name} を入れた";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // 自動生成の子ども(白。頭・体・手足。kidColor をかける)
    static Sprite GetKidSprite()
    {
        if (kidDefaultSprite != null) return kidDefaultSprite;
        const int w = 64, h = 96;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - 32f, dy = y - 74f;
                bool head = dx * dx + dy * dy <= 16f * 16f;                              // 頭
                bool body = x >= 18 && x <= 46 && y >= 26 && y <= 58;                     // 体
                bool armL = x >= 10 && x <= 18 && y >= 34 && y <= 54;                     // 腕
                bool armR = x >= 46 && x <= 54 && y >= 34 && y <= 54;
                bool legL = x >= 20 && x <= 30 && y >= 2 && y <= 28;                      // 脚
                bool legR = x >= 34 && x <= 44 && y >= 2 && y <= 28;
                px[y * w + x] = (head || body || armL || armR || legL || legR) ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        kidDefaultSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
        return kidDefaultSprite;
    }
}
