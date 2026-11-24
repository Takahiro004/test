using System.Collections;
using UnityEngine;

// 袋あらしのおばあさん(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋あらしのおばあさん」)
// 試合中、残り時間が grannyTimes の秒数になったら、ランダムに選んだ1人の袋の画面に、おばあさんが出てくる。
//  1. 画面の外側(1P は左・2P は右)から歩いてきて、袋の前に立つ。大きな背中で、レジ袋が隠れて見えない
//  2. その場でもぞもぞ動きながら、「ガサゴソ」という音(grannyRustleClip。空ならスクリプトで作った音)と文字を出す。
//     見えないところで袋が揺れ、中の商品がかき回される(grannyStirTime 秒)
//  3. 外側へ去ると、袋の中の商品がランダムに1つ無くなっている(金額は合計から引いて -¥ を出す)。袋が空なら何も取らない
//  - 絵は grannySprite(空なら Assets/Resources/Granny/Granny_back。Claude が描いた後ろ姿。Resources に置いたので、シーンの欄に入れなくても出る)
//  - 大きさ grannyHeight・絵の下のはしの高さ grannyBottom(どちらも画面の高さの割合)。横は袋の真ん中
//  - grannyPreview を ON にすると、1P・2P の画面の袋の前に止めて出す(大きさ・高さを見ながら合わせる)
//  - タイムアップ・袋持ち上げが始まったら、途中でもやめる
public partial class TwoPlayerShop2D
{
    readonly System.Collections.Generic.HashSet<int> grannyDone = new System.Collections.Generic.HashSet<int>();
    bool grannyRunning;
    SpriteRenderer[] grannyPreviews;
    static Sprite grannyBackDefault;
    static AudioClip grannyRustleDefault;
    AudioSource grannyAudio;

    Sprite GrannyBodySprite => grannySprite != null ? grannySprite : (grannyBackDefault != null ? grannyBackDefault : grannyBackDefault = Resources.Load<Sprite>("Granny/Granny_back"));

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
    float GrannyHeight => PlayerViewSize().y * Mathf.Max(0.05f, grannyHeight);

    SpriteRenderer MakeGrannyBody(string objName, int layer)
    {
        var sr = new GameObject(objName).AddComponent<SpriteRenderer>();
        sr.sprite = GrannyBodySprite;
        SetLayer(sr.gameObject, layer);
        sr.sortingOrder = 1800;   // 袋・商品・袋の手前の絵より手前(袋が隠れる)
        return sr;
    }

    // 体を置く: 横の位置 x、下のはしを grannyBottom の高さに。wiggle = もぞもぞ動く強さ(0〜1)
    void PlaceGrannyBody(SpriteRenderer body, float x, float t, float wiggle)
    {
        Vector2 view = PlayerViewSize();
        float h = GrannyHeight;
        float s = h / Mathf.Max(0.01f, body.sprite.bounds.size.y);
        body.transform.localScale = new Vector3(s, s, 1f);
        float bottom = -view.y / 2f + view.y * grannyBottom + CameraFollowY;
        float sway = Mathf.Sin(t * 18f) * h * 0.012f * wiggle;      // 手を動かしている揺れ
        float bob = Mathf.Abs(Mathf.Sin(t * 9f)) * h * 0.01f * wiggle;
        body.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 13f) * 2.5f * wiggle);
        body.transform.position = new Vector3(x + sway, bottom + h / 2f + bob, 0f);
    }

    IEnumerator GrannyRun(Player p)
    {
        grannyRunning = true;
        float side = p == p1 ? -1f : 1f;   // 1P は左・2P は右から来る
        var body = MakeGrannyBody("Granny", p.layer);
        if (body.sprite == null) { Destroy(body.gameObject); grannyRunning = false; yield break; }
        Vector2 view = PlayerViewSize();
        float w = GrannyHeight * body.sprite.bounds.size.x / Mathf.Max(0.01f, body.sprite.bounds.size.y);
        float outX = side * (view.x / 2f + w / 2f + 0.5f);   // 画面の外
        float standX = bagX;

        // 1. 外側から歩いてきて、袋の前に立つ
        for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            float k = 1f - (1f - t / grannyEnterTime) * (1f - t / grannyEnterTime);
            PlaceGrannyBody(body, Mathf.Lerp(outX, standX, k), Time.time, 0.4f);   // 歩いて弾む
            yield return null;
        }
        // 2. その場でガサゴソ(音・文字・袋が揺れる・中の商品がかき回される)
        if (!GrannyShouldStop) GrannyRustleSound(p);
        if (!GrannyShouldStop && kickShakeBag) ShakeBag(p, -side, grannyShake, false);
        float nextStir = 0f, nextWord = 0f;
        for (float t = 0f; t < grannyStirTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, standX, Time.time, 1f);
            if (t >= nextStir) { nextStir = t + 0.15f; GrannyStir(p); }
            if (grannyRustleText && t >= nextWord) { nextWord = t + 0.45f; StartCoroutine(GrannyWord(p, body)); }
            yield return null;
        }
        if (grannyAudio != null) grannyAudio.Stop();
        // 3. 袋の中からランダムに1つ抜き取る(隠れているうちに消す。金額は去るときに引く)
        int price = -1;
        if (!GrannyShouldStop) price = GrannySteal(p);
        // 外側へ去る
        for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, Mathf.Lerp(standX, outX, (t / grannyEnterTime) * (t / grannyEnterTime)), Time.time, 0.4f);
            yield return null;
        }
        if (price >= 0) StartCoroutine(MinusPopup(p, price));   // 袋が見えたところで -¥
        Destroy(body.gameObject);
        grannyRunning = false;
    }

    // ガサゴソ: 袋の中の商品を少しはじく(見えないけれど、去ったあと中身が動いている)
    void GrannyStir(Player p)
    {
        foreach (var go in p.inBag)
        {
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
            rb.linearVelocity += (Random.insideUnitCircle + Vector2.up * 0.5f) * grannyStirPower;
            rb.angularVelocity += Random.Range(-240f, 240f);
        }
    }

    // 袋の中からランダムに1つ抜き取って消す。合計から引いた金額を返す(取れなかったら -1)
    int GrannySteal(Player p)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var go in p.inBag) if (go != null) list.Add(go);
        if (list.Count == 0) return -1;
        var take = list[Random.Range(0, list.Count)];
        p.inBag.Remove(take);
        priceOf.TryGetValue(take, out int price);
        p.total -= price;
        p.bag.totalLabel.text = $"¥{p.total}";
        foreach (var c in take.GetComponentsInChildren<Collider2D>()) products.Remove(c);
        originalColors.Remove(take);
        priceOf.Remove(take);
        infoOf.Remove(take);
        overloadTime.Remove(take);
        stoppedTime.Remove(take);
        lastEvent = $"袋あらしのおばあさんが {p.name}の袋から {take.name} を抜き取った -¥{price}(合計 ¥{p.total})";
        Debug.Log($"[Shop2D] {lastEvent}");
        Destroy(take);
        return price;
    }

    // 「ガサゴソ」の文字(おばあさんの肩のあたりに、ぽんと出て消える)
    IEnumerator GrannyWord(Player p, SpriteRenderer body)
    {
        if (body == null) yield break;
        string[] words = { "ガサ", "ゴソ", "ガサゴソ", "ゴソゴソ" };
        var tm = MakeText("GrannyWord", null, p.layer, words[Random.Range(0, words.Length)], grannyRustleTextColor, 1850);
        float size = HudTextSize * Mathf.Max(0.1f, grannyRustleTextSize);
        float h = GrannyHeight;
        Vector3 at = body.transform.position + new Vector3(Random.Range(-0.35f, 0.35f) * h * 0.6f, h * Random.Range(0.05f, 0.3f), 0f);
        float rot = Random.Range(-15f, 15f);
        const float life = 0.6f;
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float k = t / life;
            SetTextSize(tm, size * (0.7f + 0.5f * Mathf.Min(1f, k * 4f)));
            tm.transform.position = at + Vector3.up * k * h * 0.03f;
            tm.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            var c = grannyRustleTextColor;
            c.a *= 1f - Mathf.Max(0f, k - 0.6f) / 0.4f;
            tm.color = c;
            yield return null;
        }
        Destroy(tm.gameObject);
    }

    // 「ガサゴソ」の音: grannyRustleClip を鳴らす(空ならスクリプトで作った、袋をこするような雑音)
    void GrannyRustleSound(Player p)
    {
        var clip = grannyRustleClip != null ? grannyRustleClip : GetGrannyRustleClip();
        if (clip == null || grannyRustleVolume <= 0f) return;
        if (grannyAudio == null) grannyAudio = gameObject.AddComponent<AudioSource>();
        grannyAudio.playOnAwake = false;
        grannyAudio.clip = clip;
        grannyAudio.loop = true;   // かき回している間くり返す
        grannyAudio.volume = Mathf.Clamp01(grannyRustleVolume);
        grannyAudio.Play();
    }

    // スクリプトで作る「ガサゴソ」の音(1.2秒。小さなこすれる音がランダムに重なる雑音)
    static AudioClip GetGrannyRustleClip()
    {
        if (grannyRustleDefault != null) return grannyRustleDefault;
        const int rate = 22050;
        int n = (int)(rate * 1.2f);
        var data = new float[n];
        var rnd = new System.Random(7);
        float lp = 0f, hpPrev = 0f, hpOut = 0f;
        // こすれる音の粒(0.03〜0.12秒)をランダムな位置に置く
        var env = new float[n];
        for (int g = 0; g < 26; g++)
        {
            int start = rnd.Next(n);
            int len = (int)(rate * (0.03 + rnd.NextDouble() * 0.09));
            float amp = 0.4f + (float)rnd.NextDouble() * 0.6f;
            for (int i = 0; i < len && start + i < n; i++)
            {
                float k = (float)i / len;
                env[start + i] = Mathf.Max(env[start + i], amp * Mathf.Sin(Mathf.PI * k));
            }
        }
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.35f;                 // 高すぎる音を少し落とす
            hpOut = 0.92f * (hpOut + lp - hpPrev);      // 低い音を落とす(シャカシャカした音)
            hpPrev = lp;
            data[i] = hpOut * env[i] * 0.8f;
        }
        grannyRustleDefault = AudioClip.Create("GrannyRustle", n, 1, rate, false);
        grannyRustleDefault.SetData(data, 0);
        return grannyRustleDefault;
    }

    // 見え方の確認: 1P・2P の画面の袋の前に、おばあさんを止めて出しっぱなしにする
    void UpdateGrannyPreview()
    {
        bool show = grannyPreview && p1 != null && p2 != null && GrannyBodySprite != null;
        if (!show)
        {
            if (grannyPreviews != null) foreach (var g in grannyPreviews) if (g != null) Destroy(g.gameObject);
            grannyPreviews = null;
            return;
        }
        if (grannyPreviews == null)
            grannyPreviews = new[] { MakeGrannyBody("GrannyPreview_1P", p1.layer), MakeGrannyBody("GrannyPreview_2P", p2.layer) };
        foreach (var g in grannyPreviews) PlaceGrannyBody(g, bagX, Time.time, 1f);
    }
}
