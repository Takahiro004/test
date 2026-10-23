using System.Collections;
using UnityEngine;

// キック(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 棚を操作中(商品を持っていないとき)に LB → 自分のレジ袋の画面の真ん中にゲージ → A で威力を決める
// → 棚の画面の下から相手側の上へ足が飛ぶ → 相手のレジ袋が威力に合わせて揺れる
// ゲージ中にもう一度 LB を押すとやめる
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
        p.kickCooldown = kickCooldown;

        int score = Mathf.RoundToInt(power * 100);
        bool perfect = power >= kickPerfect;
        StartCoroutine(Popup(p.layer, new Vector2(0f, 0.5f), perfect ? $"パーフェクト！！ {score}" : $"キック {score}",
            perfect ? new Color(1f, 0.85f, 0.2f) : Color.white, 0.12f));

        var target = p == p1 ? p2 : p1;
        StartCoroutine(FlyFoot(p, target, power));
        lastEvent = $"{p.name}のキック 威力 {score}" + (perfect ? " パーフェクト！！" : "");
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // 自分の画面に「LB: キック」/「キック あと○秒」を出す
    void UpdateKickLabel(Player p)
    {
        if (!kickEnabled) { if (p.kickLabel != null) p.kickLabel.gameObject.SetActive(false); return; }
        if (p.kickLabel == null)
            p.kickLabel = MakeText($"KickLabel_{p.name}", null, p.layer, "", Color.white, 1500);
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

    // 棚の画面の下(自分の側)から、相手側の上へ足が飛んでいく。着いたら相手のレジ袋を揺らす
    IEnumerator FlyFoot(Player from, Player target, float power)
    {
        Vector2 view = ShopViewSize();
        float side = from == p1 ? -1f : 1f;   // 1P は左から右上へ、2P は右から左上へ
        Vector2 start = new Vector2(side * view.x * 0.25f, -view.y / 2f - 1.5f);
        Vector2 end = new Vector2(-side * (view.x / 2f + 1.5f), view.y / 2f + 1.5f);
        Vector2 dir = (end - start).normalized;

        var foot = new GameObject($"Foot_{from.name}");
        foot.layer = layerShop;
        var sr = foot.AddComponent<SpriteRenderer>();
        sr.sprite = kickFootSprite != null ? kickFootSprite : GetFootSprite();
        sr.color = kickFootSprite != null ? Color.white : from.color;
        sr.sortingOrder = 1500;   // 商品とカーソルより手前、掴み合いのゲージより奥
        sr.flipY = dir.x < 0f;    // 左へ飛ぶときに靴が逆さにならないように
        Vector2 size = sr.sprite.bounds.size;
        float scale = kickFootSize / Mathf.Max(size.x, size.y, 0.01f);
        foot.transform.localScale = new Vector3(scale, scale, 1f);
        foot.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + kickFootRotation);

        for (float time = 0f; time < kickFlightTime; time += Time.deltaTime)
        {
            foot.transform.position = Vector2.Lerp(start, end, time / Mathf.Max(0.01f, kickFlightTime));
            yield return null;
        }
        Destroy(foot);

        // 蹴った向き: 1Pが蹴ると右へ、2Pが蹴ると左へ
        float toTarget = from == p1 ? 1f : -1f;

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
            StartCoroutine(Popup(target.layer, new Vector2(bagX, floorY + bagHeight + 1.4f), "キックされた！", new Color(1f, 0.4f, 0.3f), 0.1f));
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
