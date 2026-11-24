using System.Collections;
using UnityEngine;

// 袋あらしのおばあさん(妨害。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋あらしのおばあさん」)
// 試合中、残り時間が grannyTimes の秒数になったら、ランダムに選んだ1人の袋の画面に、おばあさんが出てくる。
//  1. 出てくる: grannyGlitch(初め ON)なら、その人の画面全体にノイズが走り、ときどき砂嵐が出て、収まると目の前(袋の前)にいる。
//     叫び(grannyBubble。文字 grannyBubbleText「アビダ」)。grannyShoutStyle(初め ON)なら、マンガの「ドン」のような大きな文字を
//     ひとつ、叩きつけるように出して、いる間ずっと出しておく(GrannyShout)。OFF なら吹き出し(何回も叫ぶ grannyBubbleRepeat)。
//     いる間も弱いノイズ(grannyIdleNoise)を出し続ける。
//     ノイズはハッキングされたパソコン画面のよう(色のブロック・横に走る線・走査線・画面が一瞬その色に・緑の文字・カラーの砂嵐)。OFF なら、外側(1P は左・2P は右)から歩いてくる。
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
        // 叫び(grannyBubble。「アビダ」)。grannyShoutStyle なら、マンガの「ドン」のような大きな文字をひとつ、勢いよく
        if (grannyBubble && !GrannyShouldStop)
            StartCoroutine(grannyShoutStyle ? GrannyShout(p, grannyStirTime + 0.2f) : GrannyBubble(p, true));
        // いる間も、弱いノイズ(grannyIdleNoise)を出し続ける
        GlitchFx idle = grannyGlitch && grannyIdleNoise > 0f ? MakeGlitch(p) : null;
        // 2. その場でガサゴソ(音・文字・袋が揺れる・中の商品がかき回される)
        if (!GrannyShouldStop) GrannyRustleSound(p);
        if (!GrannyShouldStop && kickShakeBag) ShakeBag(p, -side, grannyShake, false);
        float nextStir = 0f, nextWord = 0f, nextShout = Mathf.Max(0.1f, grannyBubbleInterval);
        for (float t = 0f; t < grannyStirTime && !GrannyShouldStop; t += Time.deltaTime)
        {
            PlaceGrannyBody(body, standX, Time.time, 1f);
            if (idle != null)
            {
                StepGlitch(idle, Mathf.Clamp01(grannyIdleNoise), t);
                body.color = Random.value < 0.06f ? glitchColors[Random.Range(0, 3)] : Color.white;   // ときどき色がおかしくなる
            }
            // 「アビダ」を何回も叫ぶ(grannyBubbleInterval 秒ごとに、場所・大きさ・傾きを少し変えて)
            if (grannyBubble && grannyBubbleRepeat && !grannyShoutStyle && t >= nextShout) { nextShout = t + Mathf.Max(0.1f, grannyBubbleInterval); StartCoroutine(GrannyBubble(p, false)); }
            if (t >= nextStir) { nextStir = t + 0.15f; GrannyStir(p); }
            if (grannyRustleText && t >= nextWord) { nextWord = t + 0.45f; StartCoroutine(GrannyWord(p, body)); }
            yield return null;
        }
        if (idle != null) Destroy(idle.root.gameObject);
        body.color = Color.white;
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

    // ハッキングされたパソコン画面のようなノイズ: 色のついた四角いブロック(シアン・マゼンタ・緑・白・黒)がバラバラに出て、
    // 横に走る線・走査線・画面全体が一瞬その色になる・緑の文字(ERROR など)がチカチカ。ときどきカラーの砂嵐
    static readonly Color[] glitchColors =
    {
        new Color(0f, 1f, 1f), new Color(1f, 0f, 1f), new Color(0.2f, 1f, 0.3f), Color.white, Color.black, new Color(1f, 0.15f, 0.15f),
    };
    static readonly string[] hackWords =
    {
        "ERROR", "SYSTEM HACKED", "ACCESS DENIED", "0x7F3A", "WARNING", "!!!!", "?? ?? ??", "01101001", "FATAL", "#&%$@", "NO SIGNAL", "0xDEAD",
    };

    // ノイズの部品(画面全体の砂嵐・色が一瞬変わる・走査線・色のブロック・緑の文字)
    class GlitchFx
    {
        public Transform root;
        public SpriteRenderer snow, flash, scan;
        public SpriteRenderer[] blocks;
        public TextMesh[] words;
        public float nextFlash, flashUntil = -1f, nextColor, colorUntil = -1f;
        public Color flashColor = Color.white;
    }

    GlitchFx MakeGlitch(Player p)
    {
        var g = new GlitchFx();
        g.root = new GameObject("GrannyNoise").transform;
        SetLayer(g.root.gameObject, p.layer);
        SpriteRenderer Part(string n, Sprite sp, int order)
        {
            var r = new GameObject(n).AddComponent<SpriteRenderer>();
            r.transform.SetParent(g.root, false);
            r.gameObject.layer = p.layer;
            r.sprite = sp;
            r.sortingOrder = order;
            return r;
        }
        g.snow = Part("Static", null, 4500);                         // 砂嵐(画面全体)
        g.flash = Part("ColorFlash", GetSquareSprite(), 4502);       // 画面全体が一瞬その色に
        g.scan = Part("Scanlines", GetScanlineSprite(), 4503);       // 走査線
        g.blocks = new SpriteRenderer[Mathf.Clamp(grannyNoiseLineCount, 0, 60)];
        for (int i = 0; i < g.blocks.Length; i++) g.blocks[i] = Part("GlitchBlock", GetSquareSprite(), 4501);
        g.words = new TextMesh[Mathf.Clamp(grannyHackTextCount, 0, 20)];
        for (int i = 0; i < g.words.Length; i++)
        {
            g.words[i] = MakeText("HackText", g.root, p.layer, "", new Color(0.2f, 1f, 0.3f), 4504);
            g.words[i].fontStyle = FontStyle.Bold;
        }
        return g;
    }

    // ノイズを1フレームぶん動かす(power = 強さ 0〜1)
    void StepGlitch(GlitchFx g, float power, float t)
    {
        Vector2 view = PlayerViewSize();
        float cy = CameraFollowY;
        var statics = GetStaticSprites();
        // 砂嵐: ときどき(grannyStaticChance)、ぱっと出る。強いときほど出やすい
        if (t >= g.nextFlash)
        {
            g.nextFlash = t + 0.08f;
            if (Random.value < grannyStaticChance * power) g.flashUntil = t + Random.Range(0.05f, 0.18f);
        }
        bool flashing = t < g.flashUntil;
        g.snow.sprite = statics[Random.Range(0, statics.Length)];
        Vector2 ss = g.snow.sprite.bounds.size;
        g.snow.transform.localScale = new Vector3(view.x / ss.x, view.y / ss.y, 1f);
        g.snow.transform.position = new Vector3(0f, cy, 0f);
        g.snow.color = new Color(1f, 1f, 1f, flashing ? grannyStaticAlpha : grannyStaticAlpha * 0.12f * power);
        // 画面全体が一瞬シアン・マゼンタなどに染まる
        if (t >= g.nextColor)
        {
            g.nextColor = t + 0.1f;
            if (Random.value < 0.25f * power) { g.colorUntil = t + 0.05f; g.flashColor = glitchColors[Random.Range(0, 3)]; }
        }
        g.flash.transform.localScale = new Vector3(view.x, view.y, 1f);
        g.flash.transform.position = new Vector3(0f, cy, 0f);
        g.flash.color = new Color(g.flashColor.r, g.flashColor.g, g.flashColor.b, t < g.colorUntil ? 0.35f * power : 0f);
        // 走査線
        Vector2 sc = g.scan.sprite.bounds.size;
        g.scan.transform.localScale = new Vector3(view.x / sc.x, view.y / sc.y, 1f);
        g.scan.transform.position = new Vector3(0f, cy + Random.Range(0f, view.y * 0.01f), 0f);
        g.scan.color = new Color(1f, 1f, 1f, 0.5f * power);
        // 色のついた四角いブロック・横に走る線(毎フレーム置き直す)
        foreach (var b in g.blocks)
        {
            bool on = Random.value < (0.2f + 0.7f * power) * Mathf.Min(1f, power * 3f);
            b.enabled = on;
            if (!on) continue;
            bool bar = Random.value < 0.4f;   // 横に長い線
            float wid = bar ? view.x * Random.Range(0.5f, 1.2f) : view.x * Random.Range(0.04f, 0.3f);
            float hgt = bar ? view.y * Random.Range(0.003f, 0.015f) : view.y * Random.Range(0.01f, 0.06f);
            b.transform.localScale = new Vector3(wid, hgt, 1f);
            b.transform.position = new Vector3(Random.Range(-0.5f, 0.5f) * view.x, cy + Random.Range(-0.5f, 0.5f) * view.y, 0f);
            var c = glitchColors[Random.Range(0, glitchColors.Length)];
            b.color = new Color(c.r, c.g, c.b, grannyNoiseLineAlpha * Random.Range(0.5f, 1f) * (0.3f + 0.7f * power));
        }
        // 緑の文字(ERROR など)がチカチカ
        foreach (var w in g.words)
        {
            bool on = Random.value < 0.5f * power;
            w.gameObject.SetActive(on);
            if (!on) continue;
            if (Random.value < 0.3f || w.text.Length == 0) w.text = hackWords[Random.Range(0, hackWords.Length)];
            SetTextSize(w, HudTextSize * Random.Range(0.8f, 1.6f));
            w.transform.position = new Vector3(Random.Range(-0.35f, 0.35f) * view.x, cy + Random.Range(-0.45f, 0.45f) * view.y, 0f);
            w.color = Random.value < 0.8f ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.9f);
        }
    }

    IEnumerator GrannyNoise(Player p, SpriteRenderer body, float time, bool appear)
    {
        Vector2 view = PlayerViewSize();
        var g = MakeGlitch(p);
        if (grannyNoiseSound && grannyRustleVolume > 0f) GrannyNoiseSound(true);
        for (float t = 0f; t < time && !GrannyShouldStop; t += Time.deltaTime)
        {
            float k = t / time;
            // ノイズの強さ: 出てくるときは 1 → 0 へ収まる / 消えるときは 0 → 1 → 0(真ん中でいちばん強い)。
            // ただし、いる間の弱いノイズ(grannyIdleNoise)より弱くはしない(つながって見えるように)
            float power = appear ? 1f - k * k : Mathf.Sin(k * Mathf.PI);
            power = Mathf.Max(power, appear ? Mathf.Clamp01(grannyIdleNoise) * k : Mathf.Clamp01(grannyIdleNoise) * (1f - k));
            StepGlitch(g, power, t);
            // おばあさん: 出てくるときは途中からチラチラ見え始める / 消えるときは途中までチラチラして消える
            float show = Mathf.Clamp01(grannyNoiseShowAt);
            bool visible = appear ? (k >= show && (k > 0.9f || Random.value < 0.5f + 0.5f * k))
                                  : (k < show && (k < 0.2f || Random.value < 0.6f));
            body.enabled = visible;
            // ノイズで横にずれる(デジタルにカクッと)
            float jump = visible && Random.value < 0.3f * power ? Random.Range(-0.06f, 0.06f) * view.x : 0f;
            PlaceGrannyBody(body, bagX + jump, Time.time, 0.3f);
            body.color = visible && Random.value < 0.2f * power ? glitchColors[Random.Range(0, 3)] : Color.white;   // 一瞬色がおかしくなる
            yield return null;
        }
        if (grannyNoiseSound && grannyAudio != null) grannyAudio.Stop();
        body.enabled = appear;
        body.color = Color.white;
        Destroy(g.root.gameObject);
    }

    // 走査線の絵(横じま。2ピクセルおきに暗い線)
    static Sprite scanlineSprite;
    static Sprite GetScanlineSprite()
    {
        if (scanlineSprite != null) return scanlineSprite;
        const int w = 4, h = 240;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[y * w + x] = (y % 3 == 0) ? new Color32(0, 0, 0, 160) : new Color32(0, 0, 0, 0);
        tex.SetPixels32(px);
        tex.Apply();
        scanlineSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
        return scanlineSprite;
    }

    // 砂嵐の絵(色のついた細かい四角のざらざら。4枚を毎フレーム入れかえる)
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
            // パソコンの画面が壊れたような、色のついた細かい四角(3ピクセルずつ)の砂嵐
            for (int y = 0; y < h; y += 3)
                for (int x = 0; x < w; x += 3)
                {
                    int r = rnd.Next(6);
                    byte v = (byte)rnd.Next(256);
                    var c = r == 0 ? new Color32(0, v, v, 255) : r == 1 ? new Color32(v, 0, v, 255) : r == 2 ? new Color32(0, v, 0, 255) : new Color32(v, v, v, 255);
                    for (int yy = y; yy < Mathf.Min(h, y + 3); yy++)
                        for (int xx = x; xx < Mathf.Min(w, x + 3); xx++) px[yy * w + xx] = c;
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

    // 吹き出し(「アビダ」)。first = 最初の1回(決めた位置・大きさ)。2回目からは、場所・大きさ・傾き・「！」の数を少しずつ変える
    IEnumerator GrannyBubble(Player p, bool first)
    {
        Vector2 view = PlayerViewSize();
        var bub = new GameObject("GrannyBubble").AddComponent<SpriteRenderer>();
        bub.sprite = GetBubbleSprite();
        bub.gameObject.layer = p.layer;
        bub.sortingOrder = 4000 + Random.Range(0, 50) * 2;
        float mirror = p == p2 ? -1f : 1f;
        bool flip = first ? p == p2 : Random.value < 0.5f;
        bub.flipX = flip;   // しっぽの向き
        string text = grannyBubbleText ?? "";
        if (!first) text += new string('！', Random.Range(0, 3));
        var tm = MakeText("GrannyBubbleText", null, p.layer, text, grannyBubbleTextColor, bub.sortingOrder + 1);
        float size = Mathf.Max(0.1f, grannyBubbleSize) * (first ? 1f : Random.Range(0.7f, 1.2f));
        float bw = view.x * 0.42f * size;   // 吹き出しの幅
        Vector2 basePos = new Vector2(view.x * grannyBubblePosition.x * mirror, view.y * grannyBubblePosition.y);
        Vector2 pos = first ? basePos : basePos + new Vector2(Random.Range(-1f, 1f) * view.x * grannyBubbleSpread, Random.Range(-1f, 1f) * view.y * grannyBubbleSpread * 0.6f);
        float tilt = first ? 0f : Random.Range(-15f, 15f);
        float life = Mathf.Max(0.2f, first ? grannyBubbleTime : grannyBubbleTime * 0.6f);
        for (float t = 0f; t < life && !GrannyShouldStop; t += Time.deltaTime)
        {
            float pop = t < 0.12f ? Mathf.Lerp(0.5f, 1.12f, t / 0.12f) : (t < 0.22f ? Mathf.Lerp(1.12f, 1f, (t - 0.12f) / 0.1f) : 1f);   // ぽんと出る
            float shake = Mathf.Sin(t * 60f) * bw * 0.01f;   // 叫んでぶるぶる
            float fade = Mathf.Clamp01((life - t) / 0.15f);
            Vector2 bs = bub.sprite.bounds.size;
            float sc = bw / Mathf.Max(0.01f, bs.x) * pop;
            bub.transform.localScale = new Vector3(sc, sc, 1f);
            Quaternion rot = Quaternion.Euler(0f, 0f, tilt);
            Vector3 at = new Vector3(pos.x + shake, pos.y + CameraFollowY, 0f);
            bub.transform.SetPositionAndRotation(at, rot);
            bub.color = new Color(1f, 1f, 1f, fade);
            SetTextSize(tm, HudTextSize * 1.6f * size * pop);
            tm.transform.SetPositionAndRotation(at + rot * new Vector3(0f, bw * 0.06f, 0f), rot);
            var c = grannyBubbleTextColor; c.a *= fade; tm.color = c;
            yield return null;
        }
        Destroy(bub.gameObject);
        Destroy(tm.gameObject);
    }

    // マンガの「ドン」のような叫び: ギザギザのはじける形(白・黒いふち)の上に、太い文字(黒・白いふち)をひとつ、大きく。
    // 画面の外から叩きつけるように大きく出て(grannyShoutSize の 2.5倍 → 1倍)、少し傾いて、ぶるぶる震えながら life 秒そのまま
    static Sprite burstSprite;

    IEnumerator GrannyShout(Player p, float life)
    {
        Vector2 view = PlayerViewSize();
        var burst = new GameObject("GrannyShoutBurst").AddComponent<SpriteRenderer>();
        burst.sprite = GetBurstSprite();
        burst.gameObject.layer = p.layer;
        burst.sortingOrder = 4100;
        string text = grannyBubbleText ?? "";
        // 文字のふち(白)は、少しずらした白い文字を8つ後ろに重ねて作る
        var outline = new TextMesh[8];
        for (int i = 0; i < 8; i++) { outline[i] = MakeText("GrannyShoutEdge", null, p.layer, text, Color.white, 4101); outline[i].fontStyle = FontStyle.Bold; }
        var tm = MakeText("GrannyShout", null, p.layer, text, grannyBubbleTextColor, 4102);
        tm.fontStyle = FontStyle.Bold;
        float size = Mathf.Max(0.1f, grannyBubbleSize);
        float mirror = p == p2 ? -1f : 1f;
        Vector2 pos = new Vector2(view.x * grannyBubblePosition.x * mirror, view.y * grannyBubblePosition.y);
        float tilt = -8f * mirror;
        life = Mathf.Max(0.3f, life);
        for (float t = 0f; t < life && !GrannyShouldStop; t += Time.deltaTime)
        {
            // 叩きつける: 2.5倍 → 0.9倍(0.12秒)→ 1倍。そのあとぶるぶる
            float slam = t < 0.12f ? Mathf.Lerp(2.5f, 0.9f, t / 0.12f) : (t < 0.2f ? Mathf.Lerp(0.9f, 1f, (t - 0.12f) / 0.08f) : 1f);
            float fade = Mathf.Clamp01((life - t) / 0.15f);
            float shakeK = t < 0.35f ? 1f : 0.35f;
            Vector3 shake = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * view.x * 0.008f * shakeK;
            Quaternion rot = Quaternion.Euler(0f, 0f, tilt + Mathf.Sin(t * 40f) * 1.5f);
            Vector3 at = new Vector3(pos.x, pos.y + CameraFollowY, 0f) + shake;
            float bw = view.x * 0.8f * size * slam;   // はじける形の幅
            Vector2 bs = burst.sprite.bounds.size;
            burst.transform.localScale = new Vector3(bw / bs.x, bw / bs.x, 1f);
            burst.transform.SetPositionAndRotation(at, rot);
            burst.color = new Color(1f, 1f, 1f, fade);
            float ts = HudTextSize * 3.2f * size * slam;
            SetTextSize(tm, ts);
            tm.transform.SetPositionAndRotation(at, rot);
            var c = grannyBubbleTextColor; c.a *= fade; tm.color = c;
            float o = ts * 0.9f;   // ふちの太さ
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                SetTextSize(outline[i], ts);
                outline[i].transform.SetPositionAndRotation(at + rot * new Vector3(Mathf.Cos(a) * o, Mathf.Sin(a) * o, 0f), rot);
                outline[i].color = new Color(1f, 1f, 1f, fade);
            }
            yield return null;
        }
        Destroy(burst.gameObject);
        Destroy(tm.gameObject);
        foreach (var e in outline) Destroy(e.gameObject);
    }

    // はじける形(ギザギザの星。白に黒いふち)
    static Sprite GetBurstSprite()
    {
        if (burstSprite != null) return burstSprite;
        const int size = 256;
        var rnd = new System.Random(9);
        const int spikes = 18;
        var radius = new float[spikes * 2];
        for (int i = 0; i < radius.Length; i++) radius[i] = i % 2 == 0 ? 0.92f + (float)rnd.NextDouble() * 0.08f : 0.6f + (float)rnd.NextDouble() * 0.1f;
        float R(float ang)   // 角度 ang の向きの、ふちまでの長さ(0〜1)
        {
            float f = (ang / (Mathf.PI * 2f) + 1f) % 1f * radius.Length;
            int i0 = (int)f % radius.Length, i1 = (i0 + 1) % radius.Length;
            return Mathf.Lerp(radius[i0], radius[i1], f - Mathf.Floor(f));
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = ((y + 0.5f) / size * 2f - 1f) * 1.35f;   // 少し横長
                float r = Mathf.Sqrt(u * u + v * v), edge = R(Mathf.Atan2(v, u));
                px[y * size + x] = r > edge ? new Color32(0, 0, 0, 0) : r > edge - 0.07f ? new Color32(25, 25, 25, 255) : new Color32(255, 255, 255, 255);
            }
        tex.SetPixels32(px);
        tex.Apply();
        burstSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return burstSprite;
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
