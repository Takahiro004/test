using System.Collections.Generic;
using UnityEngine;

// 高額商品のオーラ(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「高額商品のオーラ」と「機能のON/OFF: 棚」→ High Rank Aura)
// ランクが auraFromRank 以上の商品のまわりに、ゆらゆら動くモヤモヤの光を出す。
//  - 絵はスクリプトで描く(ふちが波打った、外へいくほど薄くなる白い光 x 2枚)。色 auraColor をかける
//  - 2枚を逆向きにゆっくり回し(auraSpin)、大きさと濃さを脈打たせる(auraPulseSpeed / auraPulse)ので、モヤモヤして見える
//  - 大きさは商品の大きさ x auraSize。商品の真ん中に置き、商品のすぐ奥に描く
//  - どこで出すか: 棚の上(auraOnShelf)・持ち運び中(auraWhileHeld)・自分の画面に落としたあと・袋の中(auraInBag)
//  - 炎(auraFlame): 商品のふち(当たり判定の形のふち。Collider2D.ClosestPoint で探す)から、しずく形の炎のかけらが
//    少し外へ・上へゆらゆら昇り、小さく・薄くなって消えるのをくり返す。商品が回っても、ふちに付いていく。
//    色は 生まれた所 auraFlameCoreColor → 途中 auraColor → 先 auraFlameTipColor。ちらちら大きさが揺れる。
//    数 auraFlameCount・昇る高さ auraFlameHeight・大きさ auraFlameSize・速さ auraFlameSpeed
//  - 商品の子にはしない(商品の大きさ・当たり判定の計算に入らないように)。LateUpdate から毎フレーム、商品に付いていく
//  - 再生中に変えてもすぐ反映
public partial class TwoPlayerShop2D
{
    class AuraLook
    {
        public SpriteRenderer a, b;   // 逆向きに回す2枚
        public float radius;          // 商品の半径(作ったときに1回だけ測る)
        public float phase;           // 脈打つタイミングを商品ごとにずらす
        public readonly List<FlamePart> flames = new List<FlamePart>();
        public Collider2D[] cols;     // 商品の当たり判定(炎の生まれる「ふち」を探す)
    }
    // 炎のかけら1つ
    class FlamePart
    {
        public SpriteRenderer sr;
        public float age, life;   // 生まれてからの秒数・消えるまでの秒数
        public Vector2 start;     // 生まれる位置(商品の中の位置。商品が回っても付いていく)
        public Vector2 normal;    // 生まれた所の外向き(商品の中の向き)
        public float wobble;      // 左右に揺れるタイミング
        public float size;        // 大きさのばらつき
    }
    readonly Dictionary<GameObject, AuraLook> auras = new Dictionary<GameObject, AuraLook>();
    readonly List<GameObject> auraRemove = new List<GameObject>();
    static Sprite auraSpriteA, auraSpriteB, flameSprite;

    void UpdateAuras()
    {
        // 消えた商品・オーラを出さなくなった商品のオーラを消す
        auraRemove.Clear();
        foreach (var kv in auras)
            if (kv.Key == null || !WantsAura(kv.Key)) auraRemove.Add(kv.Key);
        foreach (var go in auraRemove)
        {
            var l = auras[go];
            if (l.a != null) Destroy(l.a.gameObject);
            if (l.b != null) Destroy(l.b.gameObject);
            foreach (var f in l.flames) if (f.sr != null) Destroy(f.sr.gameObject);
            auras.Remove(go);
        }
        if (!highRankAura) return;

        // 新しく出す商品
        foreach (var go in infoOf.Keys)
            if (go != null && !auras.ContainsKey(go) && WantsAura(go)) auras[go] = MakeAura(go);

        float t = Time.time;
        foreach (var kv in auras)
        {
            var go = kv.Key;
            var l = kv.Value;
            var sr = go.GetComponent<SpriteRenderer>();
            bool visible = go.activeInHierarchy;
            l.a.gameObject.SetActive(visible);
            l.b.gameObject.SetActive(visible);
            foreach (var f in l.flames) f.sr.gameObject.SetActive(visible);
            if (!visible) continue;

            Vector3 c = go.transform.position;
            float pulse = Mathf.Sin((t + l.phase) * Mathf.PI * 2f * Mathf.Max(0f, auraPulseSpeed));   // -1 〜 1
            float diameter = l.radius * 2f * Mathf.Max(0.1f, auraSize);
            int order = (sr != null ? sr.sortingOrder : 0) - 1;   // 商品のすぐ奥
            int layer = go.layer;
            float k = Mathf.Clamp01(auraPulse);
            // 1枚目と2枚目は逆に脈打つ(片方が大きく濃いとき、もう片方は小さく薄い)
            // (光は炎の土台なので、Aura Color の濃さの 6 割)
            SetAuraPart(l.a, c, diameter * (1f + k * pulse), t * auraSpin, auraColor.a * 0.6f * (1f - k + k * (0.5f + 0.5f * pulse)), order, layer);
            SetAuraPart(l.b, c, diameter * (0.9f - k * pulse), -t * auraSpin * 1.3f + 40f, auraColor.a * 0.5f * (1f - k + k * (0.5f - 0.5f * pulse)), order, layer);
            UpdateFlames(go, l, c, order, layer);
        }
    }

    // 炎: かけらを商品のふちから上へ昇らせ、消えたら別の場所から生まれ直す
    void UpdateFlames(GameObject go, AuraLook l, Vector3 c, int order, int layer)
    {
        int want = auraFlame ? Mathf.Clamp(auraFlameCount, 0, 80) : 0;
        while (l.flames.Count > want)
        {
            var last = l.flames[l.flames.Count - 1];
            if (last.sr != null) Destroy(last.sr.gameObject);
            l.flames.RemoveAt(l.flames.Count - 1);
        }
        if (want > 0 && l.cols == null) l.cols = go.GetComponentsInChildren<Collider2D>();
        while (l.flames.Count < want)
        {
            var f = new FlamePart { sr = new GameObject("Flame").AddComponent<SpriteRenderer>() };
            f.sr.sprite = GetFlameSprite();
            RespawnFlame(go, l, f);
            f.age = Random.value * f.life;   // 最初はばらばらの時点から(一斉に生まれないように)
            l.flames.Add(f);
        }
        float speed = Mathf.Max(0.05f, auraFlameSpeed);
        float r = l.radius;
        var tr = go.transform;
        foreach (var f in l.flames)
        {
            f.age += Time.deltaTime * speed;
            if (f.age >= f.life) RespawnFlame(go, l, f);
            float k = f.age / f.life;   // 生まれた 0 → 消える 1
            Vector3 edge = tr.TransformPoint(f.start);                 // ふちの今の位置(商品が回っても付いていく)
            Vector3 n = tr.TransformDirection(f.normal);
            n.z = 0f;
            if (n.sqrMagnitude > 0.0001f) n.Normalize();
            float rise = Mathf.Max(0f, auraFlameHeight) * r;
            float sway = Mathf.Sin(f.age * 11f + f.wobble) * 0.06f * r * k;   // 上にいくほど左右に揺れる
            Vector3 pos = edge + n * (0.12f * r * k) + Vector3.up * (rise * k * k * 0.6f + rise * k * 0.4f) + Vector3.right * sway;
            f.sr.transform.position = new Vector3(pos.x, pos.y, c.z);
            float flicker = 1f + 0.18f * Mathf.Sin(f.age * 31f + f.wobble * 3f);   // ちらちら
            float s = r * Mathf.Max(0.02f, auraFlameSize) * f.size * Mathf.Pow(1f - k, 0.6f) * flicker;
            f.sr.transform.localScale = new Vector3(s * 0.8f, s * 1.5f, 1f);   // 縦長
            f.sr.transform.rotation = Quaternion.Euler(0f, 0f, -sway / Mathf.Max(0.01f, r) * 120f);   // 揺れる向きに少し傾ける
            // 色: 生まれた所(明るい芯の色) → 途中(Aura Color) → 先(Tip Color)
            Color col = k < 0.35f ? Color.Lerp(auraFlameCoreColor, auraColor, k / 0.35f)
                                  : Color.Lerp(auraColor, auraFlameTipColor, (k - 0.35f) / 0.65f);
            col.a *= Mathf.Clamp01(k / 0.12f);   // 生まれた直後はふわっと出す
            f.sr.color = col;
            f.sr.sortingOrder = order;
            if (f.sr.gameObject.layer != layer) f.sr.gameObject.layer = layer;
        }
    }

    // 炎のかけらを、商品のふちのどこかに生まれ直させる
    static void RespawnFlame(GameObject go, AuraLook l, FlamePart f)
    {
        f.age = 0f;
        f.life = Random.Range(0.35f, 0.65f);
        f.wobble = Random.Range(0f, Mathf.PI * 2f);
        f.size = Random.Range(0.6f, 1.2f);
        var tr = go.transform;
        Vector2 c = tr.position;
        float ang = Random.Range(0f, Mathf.PI * 2f);
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        Vector2 outside = c + dir * l.radius * 3f;
        Vector2 edge = c + dir * l.radius * 0.8f;   // 当たり判定が無いときは、丸く
        if (l.cols != null && l.cols.Length > 0)
        {
            var col = l.cols[Random.Range(0, l.cols.Length)];
            if (col != null && col.enabled)
            {
                Vector2 p = col.ClosestPoint(outside);   // 外の点から一番近い、当たり判定のふち
                if ((p - outside).sqrMagnitude > 0.0001f) edge = p;
            }
        }
        f.start = tr.InverseTransformPoint(edge);
        f.normal = tr.InverseTransformDirection(dir);
    }

    // しずく形(下が丸く、上がとがる)の、ふちが薄い白い炎のかけら(幅 1。真ん中がピボット)
    static Sprite GetFlameSprite()
    {
        if (flameSprite != null) return flameSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                // 高さ v での半分の幅: 下は円(中心 -0.35・半径 0.6)、上は先へいくほど細く
                float w = v < -0.35f ? Mathf.Sqrt(Mathf.Max(0f, 0.36f - (v + 0.35f) * (v + 0.35f)))
                                     : 0.6f * Mathf.Pow(Mathf.Clamp01(1f - (v + 0.35f) / 1.3f), 1.2f);
                float k = w > 0.001f ? Mathf.Clamp01((1f - Mathf.Abs(u) / w) / 0.6f) : 0f;
                float a = k * k * (3f - 2f * k);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        flameSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return flameSprite;
    }

    void SetAuraPart(SpriteRenderer r, Vector3 c, float diameter, float angle, float alpha, int order, int layer)
    {
        r.transform.position = c;
        r.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        r.transform.localScale = Vector3.one * Mathf.Max(0.01f, diameter);   // 絵の幅は 1
        r.color = new Color(auraColor.r, auraColor.g, auraColor.b, Mathf.Clamp01(alpha));
        r.sortingOrder = order;
        if (r.gameObject.layer != layer) r.gameObject.layer = layer;
    }

    // この商品にオーラを出すか(ランクと、今どこにあるか)
    bool WantsAura(GameObject go)
    {
        if (!highRankAura || !infoOf.TryGetValue(go, out var info) || info.rank < auraFromRank) return false;
        if (slotOf.ContainsKey(go)) return auraOnShelf;                                  // 棚の上
        if ((p1 != null && p1.held == go) || (p2 != null && p2.held == go)) return auraWhileHeld;   // 持ち運び中
        return auraInBag;                                                                // 自分の画面に落とした・袋の中
    }

    AuraLook MakeAura(GameObject go)
    {
        var l = new AuraLook { radius = HeldRadius(go), phase = Random.value * 10f };
        l.a = new GameObject($"Aura_{go.name}").AddComponent<SpriteRenderer>();
        l.b = new GameObject($"Aura2_{go.name}").AddComponent<SpriteRenderer>();
        l.a.sprite = GetAuraSprite(0);
        l.b.sprite = GetAuraSprite(1);
        return l;
    }

    // ふちが波打った、外へいくほど薄くなる白い光(幅 1)。kind で波の形を変える
    static Sprite GetAuraSprite(int kind)
    {
        if (kind == 0 && auraSpriteA != null) return auraSpriteA;
        if (kind == 1 && auraSpriteB != null) return auraSpriteB;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        // 波: 角度に対する半径の揺れ(ぐるっと一周でつながる sin の足し合わせ)
        int[] waves = kind == 0 ? new[] { 3, 5, 8 } : new[] { 4, 7, 11 };
        float[] amps = { 0.07f, 0.05f, 0.03f };
        float[] phases = kind == 0 ? new[] { 0.3f, 1.7f, 4.1f } : new[] { 2.2f, 0.9f, 5.3f };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                float edge = 0.78f;
                for (int i = 0; i < waves.Length; i++) edge += amps[i] * Mathf.Sin(waves[i] * ang + phases[i]);
                // 真ん中から半径の半分くらいまでは濃いまま(ここは商品に隠れる)、そこからふちへ薄くする。
                // (真ん中から薄くし始めると、商品の外に出ている所がほとんど透明になって見えないため)
                float k = Mathf.Clamp01((edge - r) / (edge * 0.45f));   // 半径の 55% まで 1 → ふち 0
                float a = k * k * (3f - 2f * k);                        // なめらかに薄くする
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        if (kind == 0) auraSpriteA = s; else auraSpriteB = s;
        return s;
    }
}
