using System.Collections;
using UnityEngine;

// 袋あらしのおばあさん(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋あらしのおばあさん」)
// 試合中、残り時間が grannyTimes の秒数になったら、ランダムに選んだ1人の袋の画面に、おばあさんが出てくる。
//  1. 出てくる: grannyGlitch(初め ON)なら、その人の画面全体にノイズが走り、ときどき砂嵐が出て、収まると目の前(袋の前)にいる。
//     吹き出し(grannyBubble。文字 grannyBubbleText「アビダ」)を出す。OFF なら、外側(1P は左・2P は右)から歩いてくる。
//     大きな背中で、レジ袋が隠れて見えない
//  2. その場でもぞもぞ動きながら、「ガサゴソ」という音(grannyRustleClip。空ならスクリプトで作った音)と文字を出す。
//     見えないところで袋が揺れ、中の商品がかき回される(grannyStirTime 秒)
//  3. 去る: ノイズの演出なら、またノイズが走って消える(OFF なら外側へ歩いて去る)。袋の中の商品がランダムに1つ無くなっている(金額は合計から引いて -¥ を出す)。袋が空なら何も取らない
//  - 絵は grannySprite(空なら Assets/Resources/Granny/Granny_back。Claude が描いた後ろ姿。Resources に置いたので、シーンの欄に入れなくても出る)
//  - 大きさ grannyHeight・絵の下のはしの高さ grannyBottom(どちらも画面の高さの割合)。横は袋の真ん中
//  - grannyTestNow を再生中に ON にすると、すぐ 1P の画面に出す(演出を確かめる用)
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
        // 試しに出す(grannyTestNow を再生中に ON にすると、すぐ 1P の画面に出る。出したら OFF に戻る)
        if (grannyTestNow)
        {
            grannyTestNow = false;
            if (!grannyRunning && p1 != null && p2 != null && !InEntry) StartCoroutine(GrannyRun(p1));
        }
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
        float side = p == p1 ? -1f : 1f;   // 1P は左・2P は右から来る(歩いて出てくるとき)
        var body = MakeGrannyBody("Granny", p.layer);
        if (body.sprite == null) { Destroy(body.gameObject); grannyRunning = false; yield break; }
        Vector2 view = PlayerViewSize();
        float w = GrannyHeight * body.sprite.bounds.size.x / Mathf.Max(0.01f, body.sprite.bounds.size.y);
        float outX = side * (view.x / 2f + w / 2f + 0.5f);   // 画面の外
        float standX = bagX;

        // 1. 出てくる: ノイズの演出(grannyGlitch)なら、画面にノイズ・ときどき砂嵐 → 収まると目の前にいる。OFF なら外側から歩いてくる
        if (grannyGlitch)
        {
            body.enabled = false;
            PlaceGrannyBody(body, standX, Time.time, 0f);
            yield return GrannyNoise(p, body, Mathf.Max(0.1f, grannyNoiseInTime), true);
        }
        else
        {
            for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
            {
                float k = 1f - (1f - t / grannyEnterTime) * (1f - t / grannyEnterTime);
                PlaceGrannyBody(body, Mathf.Lerp(outX, standX, k), Time.time, 0.4f);   // 歩いて弾む
                yield return null;
            }
        }
        body.enabled = true;
        // 吹き出し(grannyBubble。「アビダ」など)
        if (grannyBubble && !GrannyShouldStop) StartCoroutine(GrannyBubble(p));
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
        // 4. 去る: ノイズの演出なら、またノイズが走って消える。OFF なら外側へ歩いて去る
        if (grannyGlitch)
            yield return GrannyNoise(p, body, Mathf.Max(0.1f, grannyNoiseOutTime), false);
        else
            for (float t = 0f; t < grannyEnterTime && !GrannyShouldStop; t += Time.deltaTime)
            {
                PlaceGrannyBody(body, Mathf.Lerp(standX, outX, (t / grannyEnterTime) * (t / grannyEnterTime)), Time.time, 0.4f);
                yield return null;
            }
        if (price >= 0) StartCoroutine(MinusPopup(p, price));   // 袋が見えたところで -¥
        Destroy(body.gameObject);
        grannyRunning = false;
    }

    // ---------- ノイズ・砂嵐の演出 ----------
    // その人の画面全体に、横に走るノイズの線と、ときどき砂嵐(白黒のざらざら)を出す。
    // appear = true: はじめは強く、だんだん収まる。途中(grannyNoiseShowAt)からおばあさんがチラチラ見え始め、収まると目の前にいる
    // appear = false: だんだん強くなり、途中でおばあさんがチラチラして消え、また収まる
    static Sprite[] staticSprites;

    IEnumerator GrannyNoise(Player p, SpriteRenderer body, float time, bool appear)
    {
        Vector2 view = PlayerViewSize();
        var root = new GameObject("GrannyNoise").transform;
        SetLayer(root.gameObject, p.layer);
        var snow = new GameObject("Static").AddComponent<SpriteRenderer>();
        snow.transform.SetParent(root, false);
        snow.gameObject.layer = p.layer;
        snow.sortingOrder = 4500;   // いちばん手前(画面全体をおおう)
        var lines = new SpriteRenderer[Mathf.Clamp(grannyNoiseLineCount, 0, 40)];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new GameObject("NoiseLine").AddComponent<SpriteRenderer>();
            lines[i].transform.SetParent(root, false);
            lines[i].gameObject.layer = p.layer;
            lines[i].sprite = GetSquareSprite();
            lines[i].sortingOrder = 4501;
        }
        var statics = GetStaticSprites();
        if (grannyNoiseSound && grannyRustleVolume > 0f) GrannyNoiseSound(true);
        float nextFlash = 0f, flashUntil = -1f;
        for (float t = 0f; t < time && !GrannyShouldStop; t += Time.deltaTime)
        {
            float k = t / time;
            // ノイズの強さ: 出てくるときは 1 → 0 へ収まる / 消えるときは 0 → 1 → 0(真ん中でいちばん強い)
            float power = appear ? 1f - k * k : Mathf.Sin(k * Mathf.PI);
            // 砂嵐: ときどき(grannyStaticChance)、ぱっと出る。強いときほど出やすい
            if (t >= nextFlash)
            {
                nextFlash = t + 0.08f;
                if (Random.value < grannyStaticChance * power) flashUntil = t + Random.Range(0.05f, 0.18f);
            }
            bool flashing = t < flashUntil;
            float cy = CameraFollowY;
            snow.sprite = statics[Random.Range(0, statics.Length)];
            Vector2 ss = snow.sprite.bounds.size;
            snow.transform.localScale = new Vector3(view.x / ss.x, view.y / ss.y, 1f);
            snow.transform.position = new Vector3(0f, cy, 0f);
            snow.color = new Color(1f, 1f, 1f, (flashing ? grannyStaticAlpha : grannyStaticAlpha * 0.15f * power));
            // 横に走るノイズの線(高さ・太さ・ずれがばらばら。毎フレーム置き直す)
            foreach (var l in lines)
            {
                bool on = Random.value < 0.35f + 0.6f * power;
                l.enabled = on;
                if (!on) continue;
                float hgt = view.y * Random.Range(0.004f, 0.03f);
                float wid = view.x * Random.Range(0.4f, 1.2f);
                l.transform.localScale = new Vector3(wid, hgt, 1f);
                l.transform.position = new Vector3(Random.Range(-0.3f, 0.3f) * view.x, cy + Random.Range(-0.5f, 0.5f) * view.y, 0f);
                float g = Random.Range(0.6f, 1f);
                l.color = new Color(g, g, g, grannyNoiseLineAlpha * power * Random.Range(0.4f, 1f));
            }
            // おばあさん: 出てくるときは途中からチラチラ見え始める / 消えるときは途中までチラチラして消える
            float show = Mathf.Clamp01(grannyNoiseShowAt);
            bool visible = appear ? (k >= show && (k > 0.9f || Random.value < 0.5f + 0.5f * k))
                                  : (k < show && (k < 0.2f || Random.value < 0.6f));
            body.enabled = visible;
            PlaceGrannyBody(body, bagX + (visible ? Random.Range(-0.04f, 0.04f) * view.x * power : 0f), Time.time, 0.3f);   // ノイズで少しずれる
            yield return null;
        }
        if (grannyNoiseSound && grannyAudio != null) grannyAudio.Stop();
        body.enabled = appear;
        Destroy(root.gameObject);
    }

    // 砂嵐の絵(白黒のざらざら。4枚を毎フレーム入れかえる)
    static Sprite[] GetStaticSprites()
    {
        if (staticSprites != null) return staticSprites;
        staticSprites = new Sprite[4];
        var rnd = new System.Random(11);
        const int w = 90, h = 160;
        for (int f = 0; f < 4; f++)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)rnd.Next(256);
                px[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            staticSprites[f] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
        }
        return staticSprites;
    }

    // ノイズの音(ザーッという雑音)。ガサゴソと同じ音の出し口を使う
    static AudioClip noiseClipDefault;
    void GrannyNoiseSound(bool on)
    {
        if (!on) { if (grannyAudio != null) grannyAudio.Stop(); return; }
        if (noiseClipDefault == null)
        {
            const int rate = 22050;
            int n = rate;
            var data = new float[n];
            var rnd = new System.Random(5);
            for (int i = 0; i < n; i++) data[i] = (float)(rnd.NextDouble() * 2.0 - 1.0) * 0.35f;
            noiseClipDefault = AudioClip.Create("GrannyNoise", n, 1, rate, false);
            noiseClipDefault.SetData(data, 0);
        }
        if (grannyAudio == null) grannyAudio = gameObject.AddComponent<AudioSource>();
        grannyAudio.playOnAwake = false;
        grannyAudio.clip = noiseClipDefault;
        grannyAudio.loop = true;
        grannyAudio.volume = Mathf.Clamp01(grannyRustleVolume) * 0.6f;
        grannyAudio.Play();
    }

    // ---------- 吹き出し ----------
    static Sprite bubbleSprite;

    IEnumerator GrannyBubble(Player p)
    {
        Vector2 view = PlayerViewSize();
        var bub = new GameObject("GrannyBubble").AddComponent<SpriteRenderer>();
        bub.sprite = GetBubbleSprite();
        bub.gameObject.layer = p.layer;
        bub.sortingOrder = 4000;
        bub.flipX = p == p2;   // しっぽの向き(2P は左右反転)
        var tm = MakeText("GrannyBubbleText", null, p.layer, grannyBubbleText ?? "", grannyBubbleTextColor, 4001);
        float size = Mathf.Max(0.1f, grannyBubbleSize);
        float bw = view.x * 0.42f * size;   // 吹き出しの幅
        Vector2 pos = new Vector2(view.x * grannyBubblePosition.x * (p == p2 ? -1f : 1f), view.y * grannyBubblePosition.y);
        float life = Mathf.Max(0.2f, grannyBubbleTime);
        for (float t = 0f; t < life && !GrannyShouldStop; t += Time.deltaTime)
        {
            float pop = t < 0.15f ? Mathf.Lerp(0.5f, 1.08f, t / 0.15f) : (t < 0.25f ? Mathf.Lerp(1.08f, 1f, (t - 0.15f) / 0.1f) : 1f);   // ぽんと出る
            float fade = Mathf.Clamp01((life - t) / 0.2f);
            Vector2 bs = bub.sprite.bounds.size;
            float sc = bw / Mathf.Max(0.01f, bs.x) * pop;
            bub.transform.localScale = new Vector3(sc, sc, 1f);
            Vector3 at = new Vector3(pos.x, pos.y + CameraFollowY, 0f);
            bub.transform.position = at;
            bub.color = new Color(1f, 1f, 1f, fade);
            SetTextSize(tm, HudTextSize * 1.6f * size * pop);
            tm.transform.position = at + new Vector3(0f, bw * 0.06f, 0f);
            var c = grannyBubbleTextColor; c.a *= fade; tm.color = c;
            yield return null;
        }
        Destroy(bub.gameObject);
        Destroy(tm.gameObject);
    }

    // 吹き出しの絵(白い丸に黒いふち。左下にしっぽ)
    static Sprite GetBubbleSprite()
    {
        if (bubbleSprite != null) return bubbleSprite;
        const int w = 256, h = 180;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 楕円(真ん中 128,105・半径 118,68)としっぽ(左下へのびる三角)
                float ex = (x - 128f) / 118f, ey = (y - 105f) / 68f;
                float e = ex * ex + ey * ey;
                bool tail = y < 60 && y > 4 && x > 40 && x < 110 && (x - 40) > (60 - y) * 0.2f && (x - 40) < 18 + (y - 4) * 0.9f;
                bool inside = e <= 1f || tail;
                bool inner = e <= 0.86f || (tail && y > 10 && (x - 46) > (60 - y) * 0.2f && (x - 46) < 10 + (y - 10) * 0.9f) ;
                px[y * w + x] = !inside ? new Color32(0, 0, 0, 0) : inner ? new Color32(255, 255, 255, 255) : new Color32(30, 30, 30, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        bubbleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        return bubbleSprite;
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
