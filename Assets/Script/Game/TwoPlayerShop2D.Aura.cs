using System.Collections.Generic;
using UnityEngine;

// 高額商品のオーラ(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「高額商品のオーラ」と「機能のON/OFF: 棚」→ High Rank Aura)
// ランクが auraFromRank 以上の商品のまわりに、ゆらゆら動くモヤモヤの光を出す。
//  - 絵はスクリプトで描く(ふちが波打った、外へいくほど薄くなる白い光 x 2枚)。色 auraColor をかける
//  - 2枚を逆向きにゆっくり回し(auraSpin)、大きさと濃さを脈打たせる(auraPulseSpeed / auraPulse)ので、モヤモヤして見える
//  - 大きさは商品の大きさ x auraSize。商品の真ん中に置き、商品のすぐ奥に描く
//  - どこで出すか: 棚の上(auraOnShelf)・持ち運び中(auraWhileHeld)・自分の画面に落としたあと・袋の中(auraInBag)
//  - 商品の子にはしない(商品の大きさ・当たり判定の計算に入らないように)。LateUpdate から毎フレーム、商品に付いていく
//  - 再生中に変えてもすぐ反映
public partial class TwoPlayerShop2D
{
    class AuraLook
    {
        public SpriteRenderer a, b;   // 逆向きに回す2枚
        public float radius;          // 商品の半径(作ったときに1回だけ測る)
        public float phase;           // 脈打つタイミングを商品ごとにずらす
    }
    readonly Dictionary<GameObject, AuraLook> auras = new Dictionary<GameObject, AuraLook>();
    readonly List<GameObject> auraRemove = new List<GameObject>();
    static Sprite auraSpriteA, auraSpriteB;

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
        }
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
                float k = Mathf.Clamp01(1f - r / edge);      // 真ん中 1 → ふち 0
                float a = k * k * (3f - 2f * k);             // なめらかに薄くする
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
