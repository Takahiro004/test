using System.Collections;
using UnityEngine;

// おじゃまキッズ(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「おじゃまキッズ」)
// 試合中、残り時間が kidTimes の秒数になったら、小さい子どもが出てきて、2人の袋に勝手に商品を1つずつ入れていく。
//  1. 2P の画面: 右のふちから出てきて、左へ歩いて抜ける(棚へ向かう)
//  2. 棚の画面: 右から出てきて、真ん中で立ち止まって商品を取る(このとき 2P の袋に、ランダムな商品を1つ上から落とす)。
//     取った商品を頭の上に持って、左へ抜ける
//  3. 1P の画面: 右から出てきて、袋の上を横切るときに、持っていた商品を 1P の袋へ落として、左へ抜ける
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

        // 1. 2P の画面を右から左へ
        yield return KidWalk(kid, p2.layer, PlayerViewSize(), -1f, float.NaN, null);
        // 2. 棚の画面: 真ん中で商品を取って、2P の袋に1つ入れる
        if (!KidShouldStop)
            yield return KidWalk(kid, layerShop, ShopViewSize(), 0.5f, float.NaN, () =>
            {
                DropRandomProduct(p2, null);
                var info = RandomProduct();
                kidCarry = info;
                carried.sprite = ProductIconSprite(info);
                carried.enabled = carried.sprite != null;
            }, carried);
        // 3. 1P の画面: 袋の上で、持っていた商品を落とす
        if (!KidShouldStop)
            yield return KidWalk(kid, p1.layer, PlayerViewSize(), -1f, bagX, () =>
            {
                if (kidCarry != null) DropRandomProduct(p1, kidCarry);
                kidCarry = null;
                carried.enabled = false;
            }, carried);

        Destroy(kid.gameObject);
        Destroy(carried.gameObject);
        kidRunning = false;
    }

    ProductInfo kidCarry;

    // 1つの画面を、右のふちの外から左のふちの外へ歩く。
    // pauseAt(0〜1。画面を横切る割合)で立ち止まって action をする / dropX(その画面の x)を通るときに action をする(どちらか。使わないほうは負の数・NaN)
    IEnumerator KidWalk(SpriteRenderer kid, int layer, Vector2 view, float pauseAt, float dropX, System.Action action, SpriteRenderer carried = null)
    {
        SetLayer(kid.gameObject, layer);
        kid.sortingOrder = layer == layerShop ? 1500 : 1800;   // 袋の画面では袋・商品より手前
        if (carried != null) { SetLayer(carried.gameObject, layer); carried.sortingOrder = kid.sortingOrder + 1; }
        float h = Mathf.Max(0.1f, kidSize);
        float w = h * kid.sprite.bounds.size.x / Mathf.Max(0.01f, kid.sprite.bounds.size.y);
        float startX = view.x / 2f + w, endX = -view.x / 2f - w;
        float y = layer == layerShop ? view.y * (kidShopWalkY - 0.5f) : BagFloorY + bagHeight + h * 0.5f + kidWalkY + CameraFollowY;
        float time = Mathf.Max(0.3f, kidCrossTime);
        bool acted = false;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (KidShouldStop) yield break;
            float k = t / time;
            float x = Mathf.Lerp(startX, endX, k);
            // 立ち止まる所
            if (!acted && pauseAt >= 0f && k >= pauseAt)
            {
                acted = true;
                action?.Invoke();
                for (float s = 0f; s < kidPauseTime; s += Time.deltaTime)
                {
                    if (KidShouldStop) yield break;
                    PlaceKid(kid, carried, x, y, h, s * 3f);
                    yield return null;
                }
            }
            // 袋の上を通る所
            if (!acted && pauseAt < 0f && action != null && x <= dropX)
            {
                acted = true;
                action.Invoke();
            }
            PlaceKid(kid, carried, x, y, h, t);
            yield return null;
        }
    }

    // 子どもを置く(歩いて上下に弾む・少し左右に揺れる)。持っている商品は頭の上
    void PlaceKid(SpriteRenderer kid, SpriteRenderer carried, float x, float y, float h, float t)
    {
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
        sr.flipX = kidSpriteFacesRight;   // 左へ歩くので、右向きの絵なら反転
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

    // p の袋の上から、商品を1つ落とす(info が null ならランダム)。ふつうに落とした商品と同じ扱い(袋に入ったら数える)
    void DropRandomProduct(Player p, ProductInfo info)
    {
        if (p == null || p.bag == null) return;
        if (info == null) info = RandomProduct();
        if (info == null) return;
        Vector2 pos = new Vector2(bagX + Random.Range(-0.25f, 0.25f) * bagWidth, BagFloorY + bagHeight + 1f + CameraFollowY);
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
