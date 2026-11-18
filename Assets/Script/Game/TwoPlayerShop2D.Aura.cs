using System.Collections.Generic;
using UnityEngine;

// 高額商品のオーラ(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「高額商品のオーラ」と「機能のON/OFF: 棚」→ High Rank Aura)
// ランクが auraFromRank 以上の商品のまわりに、ゆらゆら動くモヤモヤの光を出す。
//  - 絵はスクリプトで描く(ふちが波打った、外へいくほど薄くなる白い光 x 2枚)。色 auraColor をかける
//  - 2枚を逆向きにゆっくり回し(auraSpin)、大きさと濃さを脈打たせる(auraPulseSpeed / auraPulse)ので、モヤモヤして見える
//  - 大きさは商品の大きさ x auraSize。商品の真ん中に置き、商品のすぐ奥に描く
//  - どこで出すか: 棚の上(auraOnShelf)・持ち運び中(auraWhileHeld)・自分の画面に落としたあと・袋の中(auraInBag)
//  - 炎(auraFlame): 商品のふち(当たり判定の形のふち。BuildEdge で探す)に、ふわっとした丸い炎のつぶを
//    すき間なく重ねて並べ、それぞれが上へゆらゆら昇って小さく・薄くなって消えるのをくり返す(つぶが重なって、ひと続きの炎に見える)。
//    つぶの大きさ・昇る高さは商品の大きさに関係なく同じ(ゲームの長さ)。大きな商品は、ふちが長いぶん、つぶの数が増える(auraFlameDensity)。
//    商品が回っても、ふちに付いていく。色は 生まれた所 auraFlameCoreColor → 途中 auraColor → 先 auraFlameTipColor。
//    つぶの大きさ auraFlameBlobSize・昇る高さ auraFlameRise・速さ auraFlameSpeed
//  - モヤモヤの光(auraGlow)は丸いので、商品の形に合わない。初めは OFF(炎だけ)
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
        public float perimeter = -1f; // ふちの長さ(ゲームの長さ。炎のつぶの数を決める)
        public Vector2[] edge;        // ふちの点(商品の中の位置。ぐるっと一周)
        public float[] edgeCum;       // 最初の点から各点までの、ふちに沿った長さ(ふちの上で同じ割合に生まれさせる)
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
            l.a.gameObject.SetActive(visible && auraGlow);
            l.b.gameObject.SetActive(visible && auraGlow);
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

    // 炎: つぶを商品のふちに並べて上へ昇らせ、消えたら別の場所から生まれ直す
    void UpdateFlames(GameObject go, AuraLook l, Vector3 c, int order, int layer)
    {
        if (auraFlame && l.edge == null) BuildEdge(go, l);
        // つぶの数 = ふちの長さ x 1 あたりの数(大きな商品ほど多い。つぶの大きさは同じ)
        int want = auraFlame ? Mathf.Clamp(Mathf.RoundToInt(l.perimeter * Mathf.Max(0f, auraFlameDensity)), 0, 150) : 0;
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
            RespawnFlame(go, l, f);
            f.age = Random.value * f.life;   // 最初はばらばらの時点から(一斉に生まれないように)
            l.flames.Add(f);
        }
        float speed = Mathf.Max(0.05f, auraFlameSpeed);
        float blob = Mathf.Max(0.02f, auraFlameBlobSize);
        float rise = Mathf.Max(0f, auraFlameRise);
        var tr = go.transform;
        foreach (var f in l.flames)
        {
            f.age += Time.deltaTime * speed;
            if (f.age >= f.life) RespawnFlame(go, l, f);
            float k = f.age / f.life;   // 生まれた 0 → 消える 1
            Vector3 edge = tr.TransformPoint(f.start);   // ふちの今の位置(商品が回っても付いていく)
            Vector3 n = tr.TransformDirection(f.normal);
            n.z = 0f;
            if (n.sqrMagnitude > 0.0001f) n.Normalize();
            float sway = Mathf.Sin(f.age * 9f + f.wobble) * 0.25f * blob * k;   // 上にいくほど左右に揺れる
            // 少し外へ押し出しながら、上へ(最初ゆっくり → だんだん速く)
            Vector3 pos = edge + n * (blob * (0.15f + 0.25f * k)) + Vector3.up * (rise * f.size * (0.4f * k + 0.6f * k * k)) + Vector3.right * sway;
            f.sr.transform.position = new Vector3(pos.x, pos.y, c.z);
            // 大きさ: 生まれてすぐふくらみ、昇りながら小さくなる。ちらちら揺れる
            float grow = Mathf.Clamp01(k / 0.1f);
            float flicker = 1f + 0.12f * Mathf.Sin(f.age * 29f + f.wobble * 3f);
            float sz = blob * f.size * grow * Mathf.Lerp(1f, 0.35f, k) * flicker;
            f.sr.transform.localScale = new Vector3(sz * 0.9f, sz * (1f + 0.6f * k), 1f);   // 昇るほど縦長
            f.sr.transform.rotation = Quaternion.identity;
            // 色: 生まれた所(明るい芯の色) → 途中(Aura Color) → 先(Tip Color)
            Color col = k < 0.35f ? Color.Lerp(auraFlameCoreColor, auraColor, k / 0.35f)
                                  : Color.Lerp(auraColor, auraFlameTipColor, (k - 0.35f) / 0.65f);
            f.sr.color = col;
            f.sr.sortingOrder = order;
            if (f.sr.gameObject.layer != layer) f.sr.gameObject.layer = layer;
        }
    }

    // ふちの点を作る: 商品の真ん中からぐるっと 96 方向に、当たり判定の外に出る所を探す(当たり判定が無ければ丸く)。
    // 商品の中の位置で覚えるので、商品が回っても付いていく。ふちに沿った長さも数えておく
    void BuildEdge(GameObject go, AuraLook l)
    {
        const int n = 96;
        var cols = go.GetComponentsInChildren<Collider2D>();
        Physics2D.SyncTransforms();   // 動かしたばかりの商品でも、当たり判定を今の位置にそろえてから調べる
        var tr = go.transform;
        Vector2 c = tr.position;
        float far = l.radius * 1.6f;
        l.edge = new Vector2[n];
        l.edgeCum = new float[n + 1];
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float d = l.radius * 0.8f;   // 当たり判定が無いときは丸く
            if (cols.Length > 0 && Inside(cols, c + dir * 0.001f))
            {
                // 内側 lo・外側 hi を半分ずつ狭めて、ふちを探す
                float lo = 0f, hi = far;
                for (int k = 0; k < 14; k++)
                {
                    float mid = (lo + hi) / 2f;
                    if (Inside(cols, c + dir * mid)) lo = mid; else hi = mid;
                }
                d = lo;
            }
            l.edge[i] = tr.InverseTransformPoint(c + dir * d);
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 a = tr.TransformPoint(l.edge[i]), b = tr.TransformPoint(l.edge[(i + 1) % n]);
            l.edgeCum[i + 1] = l.edgeCum[i] + (b - a).magnitude;
        }
        l.perimeter = l.edgeCum[n];
    }

    static bool Inside(Collider2D[] cols, Vector2 p)
    {
        foreach (var col in cols) if (col != null && col.enabled && col.OverlapPoint(p)) return true;
        return false;
    }

    // 炎のつぶを、商品のふちのどこかに生まれ直させる(ふちに沿って同じ割合に。下向きのふちは、昇ると商品に隠れるので少なめ)
    static void RespawnFlame(GameObject go, AuraLook l, FlamePart f)
    {
        f.age = 0f;
        f.life = Random.Range(0.45f, 0.85f);
        f.wobble = Random.Range(0f, Mathf.PI * 2f);
        f.size = Random.Range(0.7f, 1.15f);
        var tr = go.transform;
        int n = l.edge.Length;
        for (int tryCount = 0; tryCount < 8; tryCount++)
        {
            float t = Random.value * l.perimeter;
            int i = System.Array.BinarySearch(l.edgeCum, t);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, n - 1);
            int j = (i + 1) % n;
            float seg = l.edgeCum[i + 1] - l.edgeCum[i];
            float u = seg > 0.0001f ? (t - l.edgeCum[i]) / seg : 0f;
            Vector2 p = Vector2.Lerp(l.edge[i], l.edge[j], u);
            // 外向き = ふちの向きを 90 度回したもの(点は左回りに並んでいるので、右へ回す)
            Vector2 along = l.edge[j] - l.edge[i];
            Vector2 normal = along.sqrMagnitude > 0.000001f ? new Vector2(along.y, -along.x).normalized : p.normalized;
            Vector3 worldN = tr.TransformDirection(normal);
            f.start = p;
            f.normal = normal;
            if (worldN.y > -0.3f || Random.value < 0.25f) return;
        }
    }

    // ふわっとした丸い炎のつぶ(真ん中が濃く、ふちへなめらかに薄くなる白。幅 1)
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
                float k = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                float a = k * k * (3f - 2f * k);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        flameSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return flameSprite;
    }
}
