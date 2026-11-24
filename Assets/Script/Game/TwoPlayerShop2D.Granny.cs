using System.Collections;
using UnityEngine;

// 袋あらしのおばあさん(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋あらしのおばあさん」)
// 試合中、残り時間が grannyTimes の秒数になったら、ランダムに選んだ1人の袋の画面に、いじわるなおばあさんが出てくる。
//  1. 袋の画面の外側(1P は左・2P は右)に、近くに立った大きなおばあさん(正面を向く。口もとから下が映る。上げた腕は袋の側)が入ってくる
//  2. クレーンゲームのクレーンのように、真上(画面の上の外)から手を下ろし、袋の口から中へ入れて、中の商品をぐちゃぐちゃにかき回す
//     (商品をはじく・袋を揺らす。grannyStirTime 秒)。腕は袋の手前の絵より奥に描くので、袋の中に手を入れているように見える
//  3. 袋の中の商品をランダムに1つつかんで、真上へ引き上げ、画面の上へ持っていき、外側へ去る。取られた商品の金額は合計から引く(-¥ を出す)
//  - 袋が空なら、かき回すだけで何も取らずに去る
//  - 絵は grannySprite(体)・grannyHandSprite(そでと手。右へ伸ばした形)。空なら Assets/Resources/Granny の Claude が描いた絵を読む
//    (Resources に置いたので、シーンの欄に入れなくても出る)
//  - 大きさ grannyHeight・絵の上のはしの高さ grannyTop(どちらも画面の高さの割合)・体の真ん中の位置 grannyEdgeX(画面の真ん中から外側へ。画面の幅の割合)・腕の太さ grannyHandWidth
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
    Sprite GrannyBodySprite => grannySprite != null ? grannySprite : (grannyBodyDefault != null ? grannyBodyDefault : grannyBodyDefault = Resources.Load<Sprite>("Granny/Granny_front"));
    Sprite GrannyHandSprite => grannyHandSprite != null ? grannyHandSprite : (grannyHandDefault != null ? grannyHandDefault : grannyHandDefault = Resources.Load<Sprite>("Granny/Granny_hand"));

    float GrannyHeight => PlayerViewSize().y * Mathf.Max(0.05f, grannyHeight);

    SpriteRenderer MakeGrannyPart(string objName, Sprite sprite, int layer, int order)
    {
        var sr = new GameObject(objName).AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        SetLayer(sr.gameObject, layer);
        sr.sortingOrder = order;
        return sr;
    }

    // 体を置く: 袋の画面の外側(1P は左・2P は右)に、近くに立った大きなおばあさん(正面。上げた腕が袋の側になるように反転)。
    // side = -1(左)/ 1(右)。x0 = 体の真ん中の、画面の真ん中から外側への距離(画面の幅の割合 grannyEdgeX)
    void PlaceGrannyBody(SpriteRenderer body, float side, float edge, float t)
    {
        Vector2 view = PlayerViewSize();
        float h = GrannyHeight;
        float s = h / Mathf.Max(0.01f, body.sprite.bounds.size.y);
        body.transform.localScale = new Vector3(s, s, 1f);
        body.flipX = (side > 0f) == grannySpriteFacesRight;   // 上げた腕が袋(画面の内側)の側になるように
        body.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 5f) * 1f);
        float top = -view.y / 2f + view.y * grannyTop + CameraFollowY;
        body.transform.position = new Vector3(side * view.x * edge, top - h / 2f, 0f);
    }

    // 腕(クレーンのように真上から): 画面の上の外から、指先 tip まで、まっすぐ下へ伸ばす。腕の絵は、左のはし = 肩・右のはし = 指先
    void PlaceGrannyHand(SpriteRenderer hand, Vector2 tip)
    {
        Vector2 view = PlayerViewSize();
        Vector2 top = new Vector2(tip.x, view.y / 2f + CameraFollowY + GrannyHeight * 0.1f);
        Vector2 dir = tip - top;
        float len = Mathf.Max(0.1f, dir.magnitude);
        float s = len / Mathf.Max(0.01f, hand.sprite.bounds.size.x);
        float thick = view.x * Mathf.Max(0.02f, grannyHandWidth) / Mathf.Max(0.01f, hand.sprite.bounds.size.y);
        hand.transform.localScale = new Vector3(s, thick, 1f);
        hand.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        hand.transform.position = (Vector3)((top + tip) / 2f);
    }

    // 手を入れる横の位置(袋の真ん中あたりで少しずらす)・袋の口の少し上・袋の中
    float grannyHandX;
    Vector2 GrannyAboveBag => new Vector2(grannyHandX, BagFloorY + bagHeight + PlayerViewSize().y * 0.05f + CameraFollowY);
    Vector2 GrannyInBag => new Vector2(grannyHandX, BagFloorY + bagHeight * 0.35f + CameraFollowY);
    Vector2 GrannyOutTop => new Vector2(grannyHandX, PlayerViewSize().y / 2f + CameraFollowY + 0.5f);   // 画面の上の外(手が見えない)

    IEnumerator GrannyRun(Player p)
    {
        grannyRunning = true;
        float side = p == p1 ? -1f : 1f;   // 1P は左・2P は右から
        var body = MakeGrannyPart("Granny", GrannyBodySprite, p.layer, 1800);
        var hand = MakeGrannyPart("GrannyHand", GrannyHandSprite, p.layer, GrannyHandOrder);
        hand.enabled = false;
        if (body.sprite == null || hand.sprite == null) { Destroy(body.gameObject); Destroy(hand.gameObject); grannyRunning = false; yield break; }
        float outEdge = 0.5f + GrannyHeight * 0.5f / Mathf.Max(0.1f, PlayerViewSize().x);   // 画面の外
        grannyHandX = bagX + Random.Range(-0.15f, 0.15f) * bagWidth;
        GameObject stolen = null;

        // 1. 外側から、大きなおばあさんが入ってくる
        for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            float k = 1f - (1f - t / grannyEnterTime) * (1f - t / grannyEnterTime);
            PlaceGrannyBody(body, side, Mathf.Lerp(outEdge, grannyEdgeX, k), Time.time);
            yield return null;
        }
        // 2. クレーンのように、真上から手を下ろして、袋の口から中へ入れる
        hand.enabled = !GrannyShouldStop;
        yield return GrannyHandMove(body, side, hand, null, GrannyOutTop, GrannyAboveBag, 0.4f);
        yield return GrannyHandMove(body, side, hand, null, GrannyAboveBag, GrannyInBag, 0.25f);
        // 3. 袋の中をかき回す
        if (!GrannyShouldStop && kickShakeBag) ShakeBag(p, -side, grannyShake, false);   // 袋がぐらぐら揺れる(キックと同じ揺れ方)
        float nextKick = 0f;
        for (float t = 0f; t < grannyStirTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, side, grannyEdgeX, Time.time);
            Vector2 tip = GrannyInBag + new Vector2(Mathf.Sin(t * 14f) * bagWidth * 0.25f, Mathf.Cos(t * 11f) * bagHeight * 0.12f);
            PlaceGrannyHand(hand, tip);
            if (t >= nextKick) { nextKick = t + 0.12f; GrannyStir(p, tip); }
            yield return null;
        }
        // 4. 袋の中からランダムに1つつかんで(金額を引く)、真上へ引き上げて、画面の上へ持っていく
        if (!GrannyShouldStop) stolen = GrannyGrab(p);
        yield return GrannyHandMove(body, side, hand, stolen, GrannyInBag, GrannyAboveBag, 0.3f);
        yield return GrannyHandMove(body, side, hand, stolen, GrannyAboveBag, GrannyOutTop, 0.4f);
        hand.enabled = false;
        if (stolen != null) { ForgetStolen(stolen); stolen = null; }
        // 5. 外側へ去る
        for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, side, Mathf.Lerp(grannyEdgeX, outEdge, (t / grannyEnterTime) * (t / grannyEnterTime)), Time.time);
            yield return null;
        }
        if (stolen != null) ForgetStolen(stolen);
        Destroy(body.gameObject);
        Destroy(hand.gameObject);
        grannyRunning = false;
    }

    // 腕の描く順番: 袋の手前の絵(900)より奥・袋の中の商品より手前(袋の中に手を入れているように見える)
    const int GrannyHandOrder = 880;

    // 手を from から to へ動かす。持っている商品があれば一緒に
    IEnumerator GrannyHandMove(SpriteRenderer body, float side, SpriteRenderer hand, GameObject carry, Vector2 from, Vector2 to, float time)
    {
        for (float t = 0f; t < time && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, side, grannyEdgeX, Time.time);
            Vector2 tip = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time));
            PlaceGrannyHand(hand, tip);
            if (carry != null) carry.transform.position = new Vector3(tip.x, tip.y, carry.transform.position.z);
            yield return null;
        }
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
        take.GetComponent<SpriteRenderer>().sortingOrder = GrannyHandOrder + 1;   // 手より手前(袋の手前の絵よりは奥)
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

    // 見え方の確認: 1P(左)・2P(右)の画面に、立っている所と、真上から袋へ入れた手を出しっぱなしにする
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
                MakeGrannyPart("GrannyPreview_1P", GrannyBodySprite, p1.layer, 1800), MakeGrannyPart("GrannyHandPreview_1P", GrannyHandSprite, p1.layer, GrannyHandOrder),
                MakeGrannyPart("GrannyPreview_2P", GrannyBodySprite, p2.layer, 1800), MakeGrannyPart("GrannyHandPreview_2P", GrannyHandSprite, p2.layer, GrannyHandOrder),
            };
        }
        grannyHandX = bagX;
        PlaceGrannyBody(grannyPreviews[0], -1f, grannyEdgeX, Time.time);
        PlaceGrannyHand(grannyPreviews[1], GrannyInBag);
        PlaceGrannyBody(grannyPreviews[2], 1f, grannyEdgeX, Time.time);
        PlaceGrannyHand(grannyPreviews[3], GrannyInBag);
    }
}
