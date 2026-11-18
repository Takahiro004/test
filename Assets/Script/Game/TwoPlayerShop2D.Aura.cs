using System.Collections.Generic;
using UnityEngine;

// 高額商品のオーラ(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「高額商品のオーラ」と「機能のON/OFF: 棚」→ High Rank Aura)
// ランクが auraFromRank 以上の商品のまわりに、ゆらゆら動くモヤモヤの光を出す。
//  - 絵はスクリプトで描く(ふちが波打った、外へいくほど薄くなる白い光 x 2枚)。色 auraColor をかける
//  - 2枚を逆向きにゆっくり回し(auraSpin)、大きさと濃さを脈打たせる(auraPulseSpeed / auraPulse)ので、モヤモヤして見える
//  - 大きさは商品の大きさ x auraSize。商品の真ん中に置き、商品のすぐ奥に描く
//  - どこで出すか: 棚の上(auraOnShelf)・持ち運び中(auraWhileHeld)・自分の画面に落としたあと・袋の中(auraInBag)
//  - 炎(auraFlame): 商品のふちから、しずく形の炎のかけらが上へゆらゆら昇り、小さく・薄くなって消えるのをくり返す。
//    色は根元 auraColor → 先 auraFlameTipColor。数 auraFlameCount・昇る高さ auraFlameHeight・大きさ auraFlameSize・速さ auraFlameSpeed
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
    }
    // 炎のかけら1つ
    class FlamePart
    {
        public SpriteRenderer sr;
        public float age, life;   // 生まれてからの秒数・消えるまでの秒数
        public Vector2 start;     // 生まれる位置(商品の真ん中から。商品の半径を 1 として)
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
            SetAuraPart(l.a, c, diameter * (1f + k * pulse), t * auraSpin, auraColor.a * (1f - k + k * (0.5f + 0.5f * pulse)), order, layer);
            SetAuraPart(l.b, c, diameter * (0.9f - k * pulse), -t * auraSpin * 1.3f + 40f, auraColor.a * 0.8f * (1f - k + k * (0.5f - 0.5f * pulse)), order, layer);
            UpdateFlames(l, c, order, layer);
        }
    }

    // 炎: かけらを商品のふちから上へ昇らせ、消えたら別の場所から生まれ直す
    void UpdateFlames(AuraLook l, Vector3 c, int order, int layer)
    {
        int want = auraFlame ? Mathf.Clamp(auraFlameCount, 0, 60) : 0;
        while (l.flames.Count > want)
        {
            var last = l.flames[l.flames.Count - 1];
            if (last.sr != null) Destroy(last.sr.gameObject);
            l.flames.RemoveAt(l.flames.Count - 1);
        }
        while (l.flames.Count < want)
        {
            var f = new FlamePart { sr = new GameObject("Flame").AddComponent<SpriteRenderer>() };
            f.sr.sprite = GetFlameSprite();
            RespawnFlame(f);
            f.age = Random.value * f.life;   // 最初はばらばらの時点から(一斉に生まれないように)
            l.flames.Add(f);
        }
        float speed = Mathf.Max(0.05f, auraFlameSpeed);
        foreach (var f in l.flames)
        {
            f.age += Time.deltaTime * speed;
            if (f.age >= f.life) RespawnFlame(f);
            float k = f.age / f.life;   // 生まれた 0 → 消える 1
            float x = f.start.x + Mathf.Sin(f.age * 9f + f.wobble) * 0.12f * k;   // 上にいくほど左右に揺れる
            float y = f.start.y + k * Mathf.Max(0f, auraFlameHeight);
            f.sr.transform.position = new Vector3(c.x + x * l.radius, c.y + y * l.radius, c.z);
            float s = l.radius * Mathf.Max(0.05f, auraFlameSize) * f.size * (1f - 0.75f * k);   // 昇るほど小さく
            f.sr.transform.localScale = new Vector3(s, s * 1.5f, 1f);   // 縦長
            Color col = Color.Lerp(auraColor, auraFlameTipColor, k);
            col.a *= Mathf.Clamp01(k / 0.15f);   // 生まれた直後はふわっと出す
            f.sr.color = col;
            f.sr.sortingOrder = order;
            if (f.sr.gameObject.layer != layer) f.sr.gameObject.layer = layer;
        }
    }

    static void RespawnFlame(FlamePart f)
    {
        f.age = 0f;
        f.life = Random.Range(0.5f, 0.9f);
        float ang = Random.Range(0f, Mathf.PI * 2f);
        f.start = new Vector2(Mathf.Cos(ang) * 0.85f, Mathf.Sin(ang) * 0.7f - 0.1f);   // 商品のふちのあたり
        f.wobble = Random.Range(0f, Mathf.PI * 2f);
        f.size = Random.Range(0.7f, 1.2f);
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
