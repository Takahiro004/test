using System.Collections;
using UnityEngine;

// 袋あらしのおばあさん(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋あらしのおばあさん」)
// 試合中、残り時間が grannyTimes の秒数になったら、ランダムに選んだ1人の袋の画面に、いじわるなおばあさんが出てくる。
//  1. 画面の右から、上半身(袋は近いので大きく映る)が出てくる
//  2. 袋に手を突っ込んで、中の商品をぐちゃぐちゃにかき回す(商品をはじく・袋を揺らす。grannyStirTime 秒)
//  3. 袋の中の商品をランダムに1つつかんで、手を引き抜き、そのまま右へ去る。取られた商品の金額は合計から引く(-¥ を出す)
//  - 袋が空なら、かき回すだけで何も取らずに去る
//  - 絵は grannySprite(体)・grannyHandSprite(そでと手。右へ伸ばした形)。空なら Assets/Resources/Granny の Claude が描いた絵を読む
//    (Resources に置いたので、シーンの欄に入れなくても出る)
//  - 大きさ grannyHeight・頭のてっぺんの高さ grannyHeadTop(どちらも画面の高さの割合)・止まる位置 grannyStandX(画面の真ん中から右へ。画面の幅の割合)
//  - grannyPreview を ON にすると、1P・2P の画面に止めて出す(大きさ・位置を見ながら合わせる)
//  - タイムアップ・袋持ち上げが始まったら、途中でもやめる
public partial class TwoPlayerShop2D
{
    readonly System.Collections.Generic.HashSet<int> grannyDone = new System.Collections.Generic.HashSet<int>();
    bool grannyRunning;
    SpriteRenderer[] grannyPreviews;

    void UpdateGranny()
    {
        UpdateGrannyPreview();
        if (!grannyEnabled || grannyTimes == null || InEntry || timeLeft < 0f || IsTimeUp || grannyRunning || p1 == null || p2 == null) return;
        for (int i = 0; i < grannyTimes.Length; i++)
        {
            if (grannyDone.Contains(i) || timeLeft > grannyTimes[i]) continue;
            grannyDone.Add(i);
            StartCoroutine(GrannyRun(Random.value < 0.5f ? p1 : p2));
            return;
        }
    }

    bool GrannyShouldStop => IsTimeUp || liftStarted;

    static Sprite grannyBodyDefault, grannyHandDefault;
    Sprite GrannyBodySprite => grannySprite != null ? grannySprite : (grannyBodyDefault != null ? grannyBodyDefault : grannyBodyDefault = Resources.Load<Sprite>("Granny/Granny_body"));
    Sprite GrannyHandSprite => grannyHandSprite != null ? grannyHandSprite : (grannyHandDefault != null ? grannyHandDefault : grannyHandDefault = Resources.Load<Sprite>("Granny/Granny_hand"));

    float GrannyHeight => PlayerViewSize().y * Mathf.Max(0.05f, grannyHeight);
    float GrannyStandX => PlayerViewSize().x * grannyStandX;
    float GrannyCenterY => -PlayerViewSize().y / 2f + PlayerViewSize().y * grannyHeadTop - GrannyHeight / 2f + CameraFollowY;

    SpriteRenderer MakeGrannyPart(string objName, Sprite sprite, int layer, int order)
    {
        var sr = new GameObject(objName).AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        SetLayer(sr.gameObject, layer);
        sr.sortingOrder = order;
        return sr;
    }

    void PlaceGrannyBody(SpriteRenderer body, float x, float t)
    {
        float h = GrannyHeight;
        float s = h / Mathf.Max(0.01f, body.sprite.bounds.size.y);
        body.transform.localScale = new Vector3(s, s, 1f);
        body.transform.position = new Vector3(x, GrannyCenterY + Mathf.Sin(t * 6f) * h * 0.01f, 0f);
    }

    // 手(そでの根元 = 肩)を from から to へ伸ばした形に置く。手の絵は、左のはし = 肩・右のはし = 指先
    void PlaceGrannyHand(SpriteRenderer hand, Vector2 shoulder, Vector2 tip)
    {
        Vector2 dir = tip - shoulder;
        float len = Mathf.Max(0.1f, dir.magnitude);
        float w = hand.sprite.bounds.size.x;
        float s = len / Mathf.Max(0.01f, w);
        float thick = GrannyHeight * 0.22f / Mathf.Max(0.01f, hand.sprite.bounds.size.y);   // 太さは体に合わせる
        hand.transform.localScale = new Vector3(s, thick, 1f);
        hand.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        hand.transform.position = (Vector3)((shoulder + tip) / 2f);
    }

    Vector2 GrannyShoulder(SpriteRenderer body) =>
        (Vector2)body.transform.position + new Vector2(-GrannyHeight * 0.24f, -GrannyHeight * 0.18f);

    IEnumerator GrannyRun(Player p)
    {
        grannyRunning = true;
        var body = MakeGrannyPart("Granny", GrannyBodySprite, p.layer, 1800);
        var hand = MakeGrannyPart("GrannyHand", GrannyHandSprite, p.layer, 1802);
        hand.enabled = false;
        if (body.sprite == null || hand.sprite == null) { Destroy(body.gameObject); Destroy(hand.gameObject); grannyRunning = false; yield break; }
        Vector2 view = PlayerViewSize();
        float outX = view.x / 2f + GrannyHeight * 0.6f;   // 画面の外
        GameObject stolen = null;

        // 1. 右から出てくる
        for (float t = 0f; t < grannyEnterTime; t += Time.deltaTime)
        {
            if (GrannyShouldStop) break;
            float k = 1f - (1f - t / grannyEnterTime) * (1f - t / grannyEnterTime);
            PlaceGrannyBody(body, Mathf.Lerp(outX, GrannyStandX, k), Time.time);
            yield return null;
        }
        // 2. 手を袋に突っ込んで、かき回す
        Vector2 inBag = new Vector2(bagX, BagFloorY + bagHeight * 0.35f + CameraFollowY);
        hand.enabled = !GrannyShouldStop;
        for (float t = 0f; t < 0.35f && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, GrannyStandX, Time.time);
            Vector2 sh = GrannyShoulder(body);
            PlaceGrannyHand(hand, sh, Vector2.Lerp(sh + Vector2.left * 0.5f, inBag, t / 0.35f));
            yield return null;
        }
        if (!GrannyShouldStop && kickShakeBag) ShakeBag(p, Random.value < 0.5f ? -1f : 1f, grannyShake, false);   // 袋がぐらぐら揺れる(キックと同じ揺れ方)
        float nextKick = 0f;
        for (float t = 0f; t < grannyStirTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, GrannyStandX, Time.time);
            Vector2 tip = inBag + new Vector2(Mathf.Sin(t * 14f) * bagWidth * 0.3f, Mathf.Cos(t * 11f) * bagHeight * 0.15f);
            PlaceGrannyHand(hand, GrannyShoulder(body), tip);
            if (t >= nextKick)
            {
                nextKick = t + 0.12f;
                GrannyStir(p, tip);
            }
            yield return null;
        }
        // 3. 袋の中からランダムに1つつかむ(金額を引く)
        if (!GrannyShouldStop) stolen = GrannyGrab(p);
        // 手を引き抜いて(つかんだ商品も一緒に)、右へ去る
        Vector2 from = hand.enabled ? (Vector2)hand.transform.position + (Vector2)(hand.transform.right * hand.sprite.bounds.size.x * hand.transform.localScale.x / 2f) : inBag;
        for (float t = 0f; t < 0.4f && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, GrannyStandX, Time.time);
            Vector2 sh = GrannyShoulder(body);
            Vector2 tip = Vector2.Lerp(from, sh + new Vector2(-GrannyHeight * 0.15f, GrannyHeight * 0.1f), t / 0.4f);
            PlaceGrannyHand(hand, sh, tip);
            if (stolen != null) stolen.transform.position = new Vector3(tip.x, tip.y, stolen.transform.position.z);
            yield return null;
        }
        for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            float x = Mathf.Lerp(GrannyStandX, outX, (t / grannyEnterTime) * (t / grannyEnterTime));
            PlaceGrannyBody(body, x, Time.time);
            Vector2 sh = GrannyShoulder(body);
            Vector2 tip = sh + new Vector2(-GrannyHeight * 0.15f, GrannyHeight * 0.1f);
            PlaceGrannyHand(hand, sh, tip);
            if (stolen != null) stolen.transform.position = new Vector3(tip.x, tip.y, stolen.transform.position.z);
            yield return null;
        }
        if (stolen != null) ForgetStolen(stolen);
        Destroy(body.gameObject);
        Destroy(hand.gameObject);
        grannyRunning = false;
    }

    // かき回す: 手の近くの袋の中の商品をはじき、袋を少し揺らす
    void GrannyStir(Player p, Vector2 tip)
    {
        foreach (var go in p.inBag)
        {
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
            Vector2 away = (Vector2)go.transform.position - tip;
            float near = Mathf.Clamp01(1f - away.magnitude / Mathf.Max(0.5f, bagWidth * 0.6f));
            if (near <= 0f) continue;
            rb.linearVelocity += (away.normalized + Random.insideUnitCircle * 0.8f + Vector2.up * 0.6f) * grannyStirPower * near;
            rb.angularVelocity += Random.Range(-360f, 360f) * near;
        }
    }

    // 袋の中からランダムに1つ取る: 袋の商品の一覧から外して、金額を引き、物理を止めて手に持たせる
    GameObject GrannyGrab(Player p)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var go in p.inBag) if (go != null) list.Add(go);
        if (list.Count == 0) return null;
        var take = list[Random.Range(0, list.Count)];
        p.inBag.Remove(take);
        priceOf.TryGetValue(take, out int price);
        p.total -= price;
        p.bag.totalLabel.text = $"¥{p.total}";
        StartCoroutine(MinusPopup(p, price));
        foreach (var c in take.GetComponentsInChildren<Collider2D>()) { products.Remove(c); c.enabled = false; }
        var rb = take.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        take.GetComponent<SpriteRenderer>().sortingOrder = 1803;   // 手より手前
        lastEvent = $"袋あらしのおばあさんが {p.name}の袋から {take.name} を取った -¥{price}(合計 ¥{p.total})";
        Debug.Log($"[Shop2D] {lastEvent}");
        return take;
    }

    // 取られた商品を消す(覚えている情報も消す)
    void ForgetStolen(GameObject go)
    {
        originalColors.Remove(go);
        priceOf.Remove(go);
        infoOf.Remove(go);
        overloadTime.Remove(go);
        stoppedTime.Remove(go);
        Destroy(go);
    }

    // 見え方の確認: 1P・2P の画面に、止まる位置で手を袋へ伸ばした形を出しっぱなしにする
    void UpdateGrannyPreview()
    {
        bool show = grannyPreview && p1 != null && p2 != null && GrannyBodySprite != null && GrannyHandSprite != null;
        if (!show)
        {
            if (grannyPreviews != null) foreach (var g in grannyPreviews) if (g != null) Destroy(g.gameObject);
            grannyPreviews = null;
            return;
        }
        if (grannyPreviews == null)
        {
            grannyPreviews = new[]
            {
                MakeGrannyPart("GrannyPreview_1P", GrannyBodySprite, p1.layer, 1800), MakeGrannyPart("GrannyHandPreview_1P", GrannyHandSprite, p1.layer, 1802),
                MakeGrannyPart("GrannyPreview_2P", GrannyBodySprite, p2.layer, 1800), MakeGrannyPart("GrannyHandPreview_2P", GrannyHandSprite, p2.layer, 1802),
            };
        }
        Vector2 inBag = new Vector2(bagX, BagFloorY + bagHeight * 0.35f + CameraFollowY);
        for (int i = 0; i < 4; i += 2)
        {
            PlaceGrannyBody(grannyPreviews[i], GrannyStandX, Time.time);
            PlaceGrannyHand(grannyPreviews[i + 1], GrannyShoulder(grannyPreviews[i]), inBag);
        }
    }
}
