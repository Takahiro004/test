using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// キック(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 棚を操作中(商品を持っていないとき)に LB → 自分のレジ袋の画面の真ん中にゲージ → A で威力を決める
// → 棚の画面の下から相手側の上へ足が飛ぶ → 相手のレジ袋が威力に合わせて揺れる
// ゲージ中にもう一度 LB を押すとやめる
// 足の動き: kickFootExtend が ON(初め)なら、置き場所から相手の側へ足を伸ばし(kickExtendTime 秒。kickFootReachDistance だけ)、
//  (置き場所は kickFootRestPosition。Kick Foot Preview で見える場所)届いた瞬間に相手の袋を揺らし(KickImpact)、kickExtendHold 秒止めて、kickRetractTime 秒で引っ込める。
//  モーション kickFootClip は、kickFootClipFit なら伸ばし切ったときに終わる速さで再生する。OFF なら前のように画面を横切って飛ぶ
// 足の見た目: kickFootModel(3Dモデル)→ kickFootSprite(画像)→ 自動生成の靴 の順に、入っているものを使う(MakeFootVisual)。
//  - 3Dモデルは「つま先が右(+X)・足の裏が下」になるように kickFootModelRotation で回す。大きさは縦横の長いほうが kickFootSize。
//    位置のずれ kickFootModelOffset(足の大きさを1として)。飛ぶ間 kickFootClip を再生できる(速さ kickFootClipSpeed)
//  - 飛ぶ向きに合わせて全体を回す。左へ飛ぶとき(2P)は上下を反転して、足の裏が下のままになるようにする
// キックのため(kickCharge): 袋に商品を入れるたびにたまり、kickChargeItems 個で満タンになったら蹴れる。蹴ったら 0 に戻る(待ち時間 kickCooldown は使わない)。
//  - 自分の画面の左上の足のマーク(kickIconSprite。空なら自動生成の足)がゲージ: 縁取り(kickIconOutlineColor・太さ kickIconOutlineWidth)と
//    半透明の足(kickIconEmptyColor)の上に、たまった分だけ下から、色の段階(kickIconGradient。下 = 寒色 → 上 = 暖色)の足を重ねる。
//    満タンになったらゲージを消して、その場所に「LB: キック OK」を出す。蹴ったら(0 に戻ったら)ゲージをまた出す
//  - 大きさ kickIconSize・位置のずれ kickIconOffset
//  - 伸ばす動き(kickFootExtend)は、Kick Foot Preview と同じ置き場所(kickFootRestPosition)から、相手の側へ横に kickFootReachDistance だけ伸ばして戻す
//  - kickFootPreview を ON にすると、棚の画面の真ん中あたりに 1P 用・2P 用の足を止めて出す(再生中に向き・大きさを変えるとすぐ反映)
public partial class TwoPlayerShop2D
{
    class KickGauge
    {
        public GameObject root;
        public Transform fill;
        public float time, value, offset;
    }

    static Sprite footSprite;

    void ToggleKick(Player p)
    {
        if (p.kick != null) { EndKickGauge(p); lastEvent = $"{p.name}がキックをやめた"; return; }
        if (!kickEnabled || !p.inShop || p.held != null || p.kickCooldown > 0f) return;
        if (kickCharge && p.kickChargeCount < Mathf.Max(1, kickChargeItems)) return;   // まだたまっていない
        StartKickGauge(p);
    }

    // 自分のレジ袋の画面の真ん中にゲージを出す(掴み合いと同じ形を1本)
    void StartKickGauge(Player p)
    {
        var k = new KickGauge { offset = Random.value };
        k.root = new GameObject($"KickGauge_{p.name}");
        k.root.layer = p.layer;
        var t = k.root.transform;

        var panel = MakeBagPart("Panel", t, p.layer, new Color(0f, 0f, 0f, 0.6f), 2000, false);
        SetRect(panel, 0f, 0.5f, ClashBarWidth + 1.6f, 3.2f);

        var bar = new GameObject("Gauge").transform;
        bar.SetParent(t);
        bar.gameObject.layer = p.layer;
        bar.position = new Vector3(-ClashBarWidth / 2f, 0f, 0f);   // 細い方が左、最大が右

        MakeGaugeShape(bar, "Frame", -0.12f, ClashBarWidth + 0.24f, ClashBarHeight + 0.24f, new Color(0.9f, 1f, 1f), 2001);
        MakeGaugeShape(bar, "Back", 0f, ClashBarWidth, ClashBarHeight, new Color(0.12f, 0.12f, 0.15f), 2002);
        var mask = new GameObject("Mask");
        mask.layer = p.layer;
        mask.transform.SetParent(bar, false);
        mask.transform.localScale = new Vector3(ClashBarWidth, ClashBarHeight / GetGaugeSprite().bounds.size.y, 1f);
        mask.AddComponent<SpriteMask>().sprite = GetGaugeSprite();
        float perfectW = ClashBarWidth * (1f - kickPerfect);
        MakeGaugeFill(bar, "Perfect", ClashBarWidth - perfectW, perfectW, new Color(1f, 0.85f, 0.2f, 0.6f), 2003);
        k.fill = MakeGaugeFill(bar, "Fill", 0f, 0f, p.color, 2004).transform;

        MakeText("Title", t, p.layer, "キック！ 最大でA（LBでやめる）", Color.white, 2006).transform.position = new Vector3(0f, 1.4f, 0f);

        // 範囲の横幅に収まるように縮める(真ん中のまま)
        float fit = Mathf.Min(1f, PlayerViewSize().x / (ClashBarWidth + 2f));
        t.localScale = new Vector3(fit, fit, 1f);

        p.kick = k;
        lastEvent = $"{p.name}がキックのかまえ";
    }

    void EndKickGauge(Player p)
    {
        if (p.kick == null) return;
        Destroy(p.kick.root);
        p.kick = null;
    }

    void UpdateKick(Player p)
    {
        if (p.kickCooldown > 0f) p.kickCooldown -= Time.deltaTime;
        UpdateKickLabel(p);

        // 棚から離れたら(商品を掴んだなど)ゲージを消す
        if (p.kick != null && (!p.inShop || p.held != null)) EndKickGauge(p);
        if (p.kick == null) return;

        var k = p.kick;
        k.time += Time.deltaTime;
        k.value = Mathf.PingPong((k.time + k.offset) * kickGaugeSpeed * 2f, 1f);
        SetGaugeFill(k.fill, 0f, ClashBarWidth * k.value);
    }

    // Aで威力を決めて、足を飛ばす
    void LockKick(Player p)
    {
        float power = p.kick.value;
        EndKickGauge(p);
        if (kickCharge) p.kickChargeCount = 0;   // ためた分を使い切る(待ち時間は使わない)
        else p.kickCooldown = kickCooldown;

        int score = Mathf.RoundToInt(power * 100);
        bool perfect = power >= kickPerfect;
        StartCoroutine(Popup(p.layer, new Vector2(0f, 0.5f), perfect ? $"パーフェクト！！ {score}" : $"キック {score}",
            perfect ? new Color(1f, 0.85f, 0.2f) : Color.white, 0.12f));

        var target = p == p1 ? p2 : p1;
        StartCoroutine(FlyFoot(p, target, power));
        lastEvent = $"{p.name}のキック 威力 {score}" + (perfect ? " パーフェクト！！" : "");
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // 自分の画面に「LB: キック」/「キック あと○秒」を出す(キックのためが ON なら、足のマークのゲージと、満タンのときだけ右に「LB: キック OK」)
    void UpdateKickLabel(Player p)
    {
        if (!kickEnabled)
        {
            if (p.kickLabel != null) p.kickLabel.gameObject.SetActive(false);
            if (p.kickIcon != null) p.kickIcon.gameObject.SetActive(false);
            return;
        }
        if (p.kickLabel == null)
            p.kickLabel = MakeText($"KickLabel_{p.name}", null, p.layer, "", Color.white, 1500);
        if (kickCharge) { UpdateKickIcon(p); return; }
        if (p.kickIcon != null) p.kickIcon.gameObject.SetActive(false);
        p.kickLabel.gameObject.SetActive(p.kick == null);
        // 1P/2Pの画面の範囲の左上
        Vector2 view = PlayerViewSize();
        SetTextSize(p.kickLabel, HudTextSize);
        p.kickLabel.anchor = TextAnchor.UpperLeft;
        p.kickLabel.alignment = TextAlignment.Left;
        p.kickLabel.transform.position = new Vector3(-view.x / 2f + 0.3f, view.y / 2f - 0.3f, 0f);
        p.kickLabel.text = p.kickCooldown > 0f ? $"キック あと{Mathf.CeilToInt(p.kickCooldown)}秒" : "LB: キック OK";
        p.kickLabel.color = p.kickCooldown > 0f ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 0.95f, 0.3f);
    }

    // 足のマークのゲージ(左上)。縁取り(kickIconOutlineColor)・半透明の足(kickIconEmptyColor)の上に、
    // たまった分だけ下から、色が下 = 寒色 → 上 = 暖色(kickIconGradient)の足を重ねる。
    // 満タンになったらゲージを消して「LB: キック OK」を出す。蹴ったら(0 に戻ったら)ゲージをまた出す
    void UpdateKickIcon(Player p)
    {
        int need = Mathf.Max(1, kickChargeItems);
        int have = Mathf.Clamp(p.kickChargeCount, 0, need);
        bool full = have >= need;
        Vector2 view = PlayerViewSize();
        Vector3 topLeft = new Vector3(-view.x / 2f + 0.3f + kickIconOffset.x, view.y / 2f - 0.3f + kickIconOffset.y, 0f);

        // 満タン: ゲージを消して文字だけ
        p.kickLabel.gameObject.SetActive(full && p.kick == null);
        if (full)
        {
            if (p.kickIcon != null) p.kickIcon.gameObject.SetActive(false);
            SetTextSize(p.kickLabel, HudTextSize);
            p.kickLabel.anchor = TextAnchor.UpperLeft;
            p.kickLabel.alignment = TextAlignment.Left;
            p.kickLabel.text = "LB: キック OK";
            p.kickLabel.color = new Color(1f, 0.95f, 0.3f);
            p.kickLabel.transform.position = topLeft;
            return;
        }

        var look = GetKickIconLook(kickIconSprite != null ? kickIconSprite : GetFootSprite());
        if (look == null) return;
        if (p.kickIcon == null)
        {
            p.kickIcon = new GameObject($"KickIcon_{p.name}").transform;
            p.kickIcon.gameObject.layer = p.layer;
            p.kickIconOutline = MakeIconPart("Outline", p.kickIcon, p.layer, 1499);
            p.kickIconBack = MakeIconPart("Back", p.kickIcon, p.layer, 1500);
            p.kickIconFill = MakeIconPart("Fill", p.kickIcon, p.layer, 1501);
        }
        p.kickIcon.gameObject.SetActive(true);
        if (p.kickIcon.gameObject.layer != p.layer) SetLayer(p.kickIcon.gameObject, p.layer);

        // 大きさ: 足の縦が kickIconSize(絵は足の縦が 1 になるように作ってある)。足の下の真ん中を基準に置く
        float hgt = Mathf.Max(0.05f, kickIconSize);
        p.kickIcon.position = topLeft + new Vector3(look.aspect * hgt / 2f, -hgt, 0f);
        p.kickIcon.localScale = new Vector3(hgt, hgt, 1f);

        p.kickIconOutline.sprite = look.outline;
        p.kickIconOutline.color = kickIconOutlineColor;
        p.kickIconBack.sprite = look.back;
        p.kickIconBack.color = kickIconEmptyColor;
        float frac = (float)have / need;
        p.kickIconFill.enabled = frac > 0f;
        if (frac > 0f) p.kickIconFill.sprite = look.FillPart(frac);
        p.kickIconFill.color = Color.white;   // 色は絵に入っている(下 = 寒色 → 上 = 暖色)
    }

    static SpriteRenderer MakeIconPart(string objName, Transform parent, int layer, int order)
    {
        var go = new GameObject(objName);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    // 足のマークの絵(縁取り・半透明の足・色の付いた足)。元の絵・色の段階・縁取りの太さが変わったら作り直す
    class KickIconLook
    {
        public Sprite outline, back;
        public Texture2D fillTex;
        public int pad, footH, W, H;
        public float aspect;   // 足の 横 ÷ 縦
        readonly Dictionary<int, Sprite> parts = new Dictionary<int, Sprite>();

        // 下から frac(0〜1)の高さまでを切り出した、色の付いた足(ピボットは足の下の真ん中)
        public Sprite FillPart(float frac)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(frac * 100f), 1, 100);
            if (parts.TryGetValue(step, out var s) && s != null) return s;
            float h = pad + footH * step / 100f;
            s = Sprite.Create(fillTex, new Rect(0, 0, W, h), new Vector2(0.5f, pad / h), footH);
            parts[step] = s;
            return s;
        }
    }
    KickIconLook kickIconLook;
    string kickIconLookKey;

    KickIconLook GetKickIconLook(Sprite src)
    {
        var sb = new System.Text.StringBuilder($"{src.GetInstanceID()}_{kickIconOutlineWidth:0.###}");
        if (kickIconGradient != null)
        {
            foreach (var c in kickIconGradient.colorKeys) sb.Append($"_{c.color}{c.time:0.##}");
            foreach (var a in kickIconGradient.alphaKeys) sb.Append($"_{a.alpha:0.##}{a.time:0.##}");
        }
        string key = sb.ToString();
        if (kickIconLook != null && key == kickIconLookKey) return kickIconLook;
        kickIconLookKey = key;

        var px = ReadSpritePixels(src, 128, out int w, out int h, out float _);
        // 足の形(色の付いた所)の、上と下の端を探す(色の段階を足の下から上に合わせるため)
        int minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 60) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
        if (maxY < 0) { kickIconLook = null; return null; }
        int footH = maxY - minY + 1;
        int o = Mathf.Clamp(Mathf.RoundToInt(footH * Mathf.Max(0f, kickIconOutlineWidth)), 0, 20);   // 縁取りの太さ(ピクセル)
        int W = w + o * 2, H = footH + o * 2;
        var mask = new float[W * H];
        for (int y = 0; y < footH; y++)
            for (int x = 0; x < w; x++)
                mask[(y + o) * W + x + o] = px[(y + minY) * w + x].a / 255f;
        var grown = o > 0 ? MaxFilter(mask, W, H, o) : mask;

        var outPx = new Color32[W * H];
        var backPx = new Color32[W * H];
        var fillPx = new Color32[W * H];
        var grad = kickIconGradient ?? DefaultKickGradient();
        for (int y = 0; y < H; y++)
        {
            Color g = grad.Evaluate(Mathf.Clamp01((y - o) / (float)Mathf.Max(1, footH - 1)));   // 足の下 0 → 上 1
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                byte m = (byte)(Mathf.Clamp01(mask[i]) * 255f);
                byte ring = (byte)(Mathf.Clamp01(grown[i] - mask[i]) * 255f);   // 縁取りは、足のまわりだけ(半透明の足の下から透けないように)
                outPx[i] = new Color32(255, 255, 255, ring);
                backPx[i] = new Color32(255, 255, 255, m);
                fillPx[i] = new Color32((byte)(g.r * 255f), (byte)(g.g * 255f), (byte)(g.b * 255f), m);
            }
        }
        Texture2D Make(Color32[] data)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(data);
            t.Apply();
            return t;
        }
        var pivot = new Vector2(0.5f, o / (float)H);   // 足の下の真ん中
        kickIconLook = new KickIconLook
        {
            outline = Sprite.Create(Make(outPx), new Rect(0, 0, W, H), pivot, footH),
            back = Sprite.Create(Make(backPx), new Rect(0, 0, W, H), pivot, footH),
            fillTex = Make(fillPx),
            pad = o, footH = footH, W = W, H = H,
            aspect = w / (float)footH,
        };
        return kickIconLook;
    }

    // 下 = 寒色(青) → 緑 → 黄 → オレンジ → 上 = 暖色(赤)
    static Gradient DefaultKickGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.15f, 0.35f, 1f), 0f),
                new GradientColorKey(new Color(0.1f, 0.8f, 0.9f), 0.25f),
                new GradientColorKey(new Color(0.2f, 0.85f, 0.2f), 0.45f),
                new GradientColorKey(new Color(1f, 0.9f, 0.1f), 0.65f),
                new GradientColorKey(new Color(1f, 0.5f, 0.05f), 0.82f),
                new GradientColorKey(new Color(0.95f, 0.1f, 0.1f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // 棚の画面の下(自分の側)から、相手側の上へ足が飛んでいく。着いたら相手のレジ袋を揺らす
    IEnumerator FlyFoot(Player from, Player target, float power)
    {
        Vector2 view = ShopViewSize();
        float side = from == p1 ? -1f : 1f;   // 1P は左から右上へ、2P は右から左上へ
        Vector2 start = new Vector2(side * view.x * 0.25f, -view.y / 2f - 1.5f);
        Vector2 end = new Vector2(-side * (view.x / 2f + 1.5f), view.y / 2f + 1.5f);
        Vector2 dir = (end - start).normalized;

        if (kickFootExtend)
        {
            // 伸ばす動き: Kick Foot Preview と同じ置き場所(FootRestPosition)から、相手の側へ横に足をぐっと伸ばし(キックのモーションも一緒に)、
            // 届いたら少し止めて、元の置き場所へ引っ込める
            start = FootRestPosition(from == p1);
            float extendTime = Mathf.Max(0.05f, kickExtendTime);
            float clipSpeed = kickFootClip != null && kickFootClipFit ? kickFootClip.length / extendTime : -1f;   // 伸ばし切ったときにモーションも終わる速さ
            var leg = MakeFootVisual($"Foot_{from.name}", from.color, dir, true, out var legGraph, clipSpeed);
            leg.transform.position = start;
            // 相手の側(1P は右・2P は左)へ、まっすぐ横に伸ばす。足の向き(dir)は Preview で合わせたまま。
            // kickFootReachToEdge なら、足の先(相手の側のはし)が、相手の側の画面のふちにちょうど届く長さ
            float toward = from == p1 ? 1f : -1f;
            float distance = Mathf.Max(0f, kickFootReachDistance);
            if (kickFootReachToEdge)
            {
                var rs = leg.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    float tip = toward > 0f ? b.max.x : b.min.x;   // 足の先
                    float edge = toward * view.x / 2f;             // 相手の側の画面のふち
                    distance = Mathf.Max(0f, (edge - tip) * toward);
                }
            }
            Vector2 reach = start + new Vector2(toward, 0f) * distance;
            for (float time = 0f; time < extendTime; time += Time.deltaTime)
            {
                float k = time / extendTime;
                leg.transform.position = Vector2.Lerp(start, reach, 1f - (1f - k) * (1f - k));   // 最初は速く、届くところでゆっくり
                yield return null;
            }
            leg.transform.position = reach;
            KickImpact(from, target, power);   // 届いた瞬間に相手の袋を揺らす
            for (float time = 0f; time < kickExtendHold; time += Time.deltaTime) yield return null;
            float backTime = Mathf.Max(0.05f, kickRetractTime);
            for (float time = 0f; time < backTime; time += Time.deltaTime)
            {
                float k = time / backTime;
                leg.transform.position = Vector2.Lerp(reach, start, k * k);   // ゆっくり戻り始めて、すっと引っ込む
                yield return null;
            }
            if (legGraph.IsValid()) legGraph.Destroy();
            Destroy(leg);
            yield break;
        }

        var foot = MakeFootVisual($"Foot_{from.name}", from.color, dir, true, out var footGraph);

        for (float time = 0f; time < kickFlightTime; time += Time.deltaTime)
        {
            foot.transform.position = Vector2.Lerp(start, end, time / Mathf.Max(0.01f, kickFlightTime));
            yield return null;
        }
        if (footGraph.IsValid()) footGraph.Destroy();
        Destroy(foot);
        KickImpact(from, target, power);
    }

    // 足が届いた: 相手のレジ袋と画面を揺らす(蹴った人も少し)
    void KickImpact(Player from, Player target, float power)
    {
        // 蹴った向き: 1Pが蹴ると右へ、2Pが蹴ると左へ
        float toTarget = from == p1 ? 1f : -1f;
        // 蹴る強さ(kickStrength)を、袋と画面の揺れの全部にかける
        power *= Mathf.Max(0f, kickStrength);

        // レジ袋は「蹴られた」揺れ方: 蹴られた向きへぐっと押されて、揺れながら戻る
        if (kickShakeBag) ShakeBag(target, toTarget, power, true);
        // 同じ机なので、蹴った人のレジ袋も少し揺れる
        if (kickShakeBag && kickShakeSelf && kickSelfShake > 0f) ShakeBag(from, toTarget, power * kickSelfShake, false);

        // 画面も揺らす(棚の画面は揺らさない)
        //  蹴られた人の画面: 蹴られた向きへドンと押されて、ガタガタ揺れながら戻る
        //  蹴った人の画面: 相手の側へグッと踏み込んで、すっと戻る(蹴った動き)
        if (kickShakeScreen) ShakeScreen(target, toTarget, power, false);
        if (kickShakeScreen && kickShakeSelf && kickSelfShake > 0f) ShakeScreen(from, toTarget, power * kickSelfShake, true);
        lastEvent = $"{target.name}のレジ袋が揺れた(威力 {Mathf.RoundToInt(power * 100)})";
    }

    void ShakeBag(Player target, float direction, float power, bool kicked)
    {
        if (target.bag == null || target.bag.body == null) return;
        if (liftStarted) return;   // タイムアップで袋を持ち上げ始めたら、飛んでいる途中の足が届いても揺らさない(TwoPlayerShop2D.Lift.cs)
        if (target.shakeRoutine != null) StopCoroutine(target.shakeRoutine);
        target.shakeRoutine = StartCoroutine(ShakeBagRoutine(target, direction, power));
        if (kicked)
            StartCoroutine(Popup(target.layer, new Vector2(bagX, BagFloorY + bagHeight + 1.4f), "キックされた！", new Color(1f, 0.4f, 0.3f), 0.1f));
    }

    // プレイヤーの画面(カメラ)を、direction(+1 右 / -1 左)へ押されたように揺らす
    void ShakeScreen(Player p, float direction, float power, bool kicker)
    {
        if (kickScreenShake <= 0f || p.screen < 0 || p.screen >= switcher.screens.Length) return;
        var cam = switcher.screens[p.screen].camera;
        if (cam == null) return;
        if (!p.hasCameraBase) { p.cameraBase = cam.transform.position; p.hasCameraBase = true; }
        if (p.screenShakeRoutine != null) StopCoroutine(p.screenShakeRoutine);
        p.screenShakeRoutine = StartCoroutine(ShakeScreenRoutine(p, cam, direction, power, kicker));
    }

    IEnumerator ShakeScreenRoutine(Player p, Camera cam, float direction, float power, bool kicker)
    {
        float amplitude = kickScreenShake * power;
        float duration = Mathf.Max(0.01f, kickScreenShakeTime);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float t = time / duration;
            float shift;
            if (kicker)
            {
                // 蹴った動き: すばやく相手の側へ踏み込み(最初の2割)、ゆっくり戻る。行き来はしない
                float push = t < 0.2f ? Mathf.Sin(t / 0.2f * Mathf.PI / 2f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.8f);
                shift = direction * amplitude * push;
            }
            else
            {
                // 蹴られた動き: いきなり押されて、ガタガタ行き来しながら小さくなる
                float fade = (1f - t) * (1f - t);
                shift = direction * amplitude * 1.3f * fade * Mathf.Sin(t * Mathf.PI * 7f);
            }
            // 画面の中身を右へずらす = カメラを左へ動かす
            cam.transform.position = p.cameraBase + new Vector3(-shift, 0f, 0f);
            yield return null;
        }
        cam.transform.position = p.cameraBase;
        p.screenShakeRoutine = null;
    }

    // レジ袋を左右に揺らす。威力が大きいほど大きく揺れる。中の商品は袋に押されて動く(こぼれたら消える)
    IEnumerator ShakeBagRoutine(Player target, float direction, float power)
    {
        var body = target.bag.body;
        foreach (var go in target.inBag)   // 袋の中で止まっている商品を起こす
        {
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            if (rb != null) rb.WakeUp();
        }

        float amplitude = kickShakeMax * power;
        float up = kickShakeUp * power;
        for (float time = 0f; time < kickShakeTime; time += Time.fixedDeltaTime)
        {
            float fade = 1f - time / Mathf.Max(0.01f, kickShakeTime);
            float wave = time * kickShakeFrequency * Mathf.PI * 2f;
            float x = direction * amplitude * fade * Mathf.Sin(wave);     // 左右(最初は蹴られた向きへ)
            float y = up * fade * Mathf.Abs(Mathf.Sin(wave));             // 上に跳ねる
            body.MovePosition(new Vector2(x, y));
            yield return new WaitForFixedUpdate();
        }
        body.MovePosition(Vector2.zero);
        target.shakeRoutine = null;
    }

    // 文字を出して、上へ浮かびながら消える
    IEnumerator Popup(int layer, Vector2 pos, string text, Color color, float size)
    {
        var label = MakeText("Popup", null, layer, text, color, 2010);
        SetTextSize(label, size * Mathf.Max(0.1f, textScale));
        for (float time = 0f; time < 1f; time += Time.deltaTime)
        {
            label.transform.position = new Vector3(pos.x, pos.y + time, 0f);
            label.color = new Color(color.r, color.g, color.b, 1f - time);
            yield return null;
        }
        Destroy(label.gameObject);
    }

    // 足の見た目を作る(3Dモデル → 画像 → 自動生成の靴)。dir = 飛ぶ向き。play なら kickFootClip を再生する(graph を返す。止めたら Destroy する)
    GameObject MakeFootVisual(string objName, Color playerColor, Vector2 dir, bool play, out PlayableGraph graph, float clipSpeed = -1f)
    {
        graph = default;
        bool leftward = dir.x < 0f;
        var foot = new GameObject(objName);
        foot.layer = layerShop;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + kickFootRotation;

        if (kickFootModel != null)
        {
            // 入れ物: foot(飛ぶ向きに回す)→ Flip(左へ飛ぶときは上下反転)→ Model
            var flip = new GameObject("Flip").transform;
            flip.SetParent(foot.transform, false);
            var model = Instantiate(kickFootModel, flip);
            model.name = "Model";
            Quaternion prefabRotation = kickFootModel.transform.localRotation;
            Vector3 prefabScale = kickFootModel.transform.localScale;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(kickFootModelRotation) * prefabRotation;
            model.transform.localScale = prefabScale;
            foreach (var col in model.GetComponentsInChildren<Collider>()) col.enabled = false;   // 当たり判定は使わない
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) Debug.LogWarning($"[Shop2D] 足の3Dモデル {kickFootModel.name} に見た目(Renderer)がありません");
            else
            {
                // まだ回していない(foot が原点・回転なし)うちに大きさを測って、縦横の長いほうを kickFootSize にする
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                float size = Mathf.Max(0.01f, kickFootSize);
                float k = size / Mathf.Max(b.size.x, b.size.y, 0.01f);
                model.transform.localScale = prefabScale * k;
                // 真ん中を foot の位置に合わせて、kickFootModelOffset だけずらす
                Vector3 c = b.center * k;
                model.transform.localPosition = new Vector3(kickFootModelOffset.x * size - c.x, kickFootModelOffset.y * size - c.y, -c.z);
                UseSpriteOrder(renderers);   // 絵と同じ sortingOrder の順で描く(TwoPlayerShop2D.Products.cs)
                foreach (var r in renderers) r.sortingOrder = 1500;   // 商品とカーソルより手前、掴み合いのゲージより奥
            }
            SetLayer(model, layerShop);
            if (leftward) flip.localScale = new Vector3(1f, -1f, 1f);   // 左へ飛ぶときに足が逆さにならないように
            if (play && kickFootClip != null)
            {
                var animator = model.GetComponentInChildren<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                var playable = AnimationPlayableUtilities.PlayClip(animator, kickFootClip, out graph);
                playable.SetSpeed(Mathf.Max(0.01f, clipSpeed > 0f ? clipSpeed : kickFootClipSpeed));
            }
        }
        else
        {
            var sr = foot.AddComponent<SpriteRenderer>();
            sr.sprite = kickFootSprite != null ? kickFootSprite : GetFootSprite();
            sr.color = kickFootSprite != null ? Color.white : playerColor;
            sr.sortingOrder = 1500;   // 商品とカーソルより手前、掴み合いのゲージより奥
            sr.flipY = leftward;      // 左へ飛ぶときに靴が逆さにならないように
            Vector2 size = sr.sprite.bounds.size;
            float scale = kickFootSize / Mathf.Max(size.x, size.y, 0.01f);
            foot.transform.localScale = new Vector3(scale, scale, 1f);
        }
        foot.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        return foot;
    }

    // ---------- 足の見え方の確認(kickFootPreview) ----------
    // 棚の画面の左に 1P 用(右上へ飛ぶ向き)・右に 2P 用(左上へ飛ぶ向き)の足を止めて出す。設定を変えたら作り直す
    GameObject footPreview1, footPreview2;
    PlayableGraph footPreviewGraph1, footPreviewGraph2;
    (GameObject model, Sprite sprite, Vector3 rot, Vector2 offset, float size, float rotation, AnimationClip clip, float clipSpeed) footPreviewBuilt;

    void UpdateKickFootPreview()
    {
        var want = (kickFootModel, kickFootSprite, kickFootModelRotation, kickFootModelOffset, kickFootSize, kickFootRotation, kickFootClip, kickFootClipSpeed);
        bool changed = !want.Equals(footPreviewBuilt);
        if (!kickFootPreview || changed)
        {
            if (footPreviewGraph1.IsValid()) footPreviewGraph1.Destroy();
            if (footPreviewGraph2.IsValid()) footPreviewGraph2.Destroy();
            if (footPreview1 != null) Destroy(footPreview1);
            if (footPreview2 != null) Destroy(footPreview2);
            footPreview1 = footPreview2 = null;
        }
        if (!kickFootPreview || p1 == null || p2 == null) return;
        Vector2 view = ShopViewSize();
        if (footPreview1 == null)
        {
            footPreviewBuilt = want;
            // 飛ぶ向きは FlyFoot と同じ(1P は左下から右上へ・2P は右下から左上へ)
            Vector2 s1 = new Vector2(-view.x * 0.25f, -view.y / 2f - 1.5f), e1 = new Vector2(view.x / 2f + 1.5f, view.y / 2f + 1.5f);
            footPreview1 = MakeFootVisual("FootPreview_1P", p1.color, (e1 - s1).normalized, true, out footPreviewGraph1);
            footPreview2 = MakeFootVisual("FootPreview_2P", p2.color, (new Vector2(-e1.x, e1.y) - new Vector2(-s1.x, s1.y)).normalized, true, out footPreviewGraph2);
        }
        footPreview1.transform.position = FootRestPosition(true);
        footPreview2.transform.position = FootRestPosition(false);
    }

    // 足の置き場所(Kick Foot Preview で出す場所。伸ばす動きは、ここから伸ばして、ここへ引っ込める)。
    // kickFootRestPosition: X = 画面の真ん中から自分の側へ(画面の幅の割合。1P は左・2P は右)・Y = 上へ(画面の高さの割合)
    Vector3 FootRestPosition(bool isP1)
    {
        Vector2 view = ShopViewSize();
        float side = isP1 ? -1f : 1f;
        return new Vector3(side * view.x * kickFootRestPosition.x, view.y * kickFootRestPosition.y, 0f);
    }

    // 自動生成の靴(つま先が右)。足の画像ができたら kickFootSprite に入れる
    static Sprite GetFootSprite()
    {
        if (footSprite != null) return footSprite;
        const int size = 64;
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool leg = x >= 10 && x <= 28 && y >= 18 && y <= 60;               // すね
                bool foot = x >= 10 && x <= 50 && y >= 4 && y <= 24;               // 足の甲と靴底
                float dx = x - 50f, dy = y - 14f;
                bool toe = dx * dx + dy * dy <= 100f;                              // 丸いつま先
                px[y * size + x] = (leg || foot || toe) ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        footSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        return footSprite;
    }
}
