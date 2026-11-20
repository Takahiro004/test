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
//  - 形(auraStyle): Silhouette(初め) = 商品の形をふちどる、ぼかした光(halo)を商品の奥に出し、同じ形の光が何枚も、
//    ふくらみながら上へゆらゆら昇って消えるのをくり返す(マンガのキャラの体から立ちのぼるオーラのイメージ)。
//    光の形は商品の絵(3Dモデルは正面から撮った絵 ModelIconSprite)を、太らせて(auraSilhouetteWidth。ゲームの長さなので、大きな商品でも同じ細さ)ぼかして作る(MakeAuraHaloSprite)。
//    枚数 auraSilhouetteLayers・ふくらむ大きさ auraSilhouetteSpread・昇る高さ auraSilhouetteRise・速さ auraSilhouetteSpeed
//    Flame = 下の炎(と丸い光)
//  - 商品の子にはしない(商品の大きさ・当たり判定の計算に入らないように)。LateUpdate から毎フレーム、商品に付いていく
//  - 再生中に変えてもすぐ反映
public enum AuraStyle { Silhouette, Flame }   // オーラの形(商品の形の光が立ちのぼる / 炎のつぶ)

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
        public Vector2[] silPts, silNormals;   // 3Dモデルの商品: 見た目の輪郭の点と外向き(商品の中の位置・向き。BuildSilhouetteEdge)
        // 形のオーラ(Silhouette)
        public bool haloBuilt;
        public Sprite halo;           // 商品の形の、ぼかした光の絵
        public bool haloIsModel;      // 3Dモデルの商品(撮った絵の真ん中を haloCenter に置き、幅を haloWidth にする)
        public Vector2 haloCenter;    // 商品の中の位置
        public float haloWidth;
        public SpriteRenderer haloBase;                 // いつも出ている光
        public readonly List<SpriteRenderer> haloWaves = new List<SpriteRenderer>();   // 昇っていく光
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
            if (l.haloBase != null) Destroy(l.haloBase.gameObject);
            foreach (var w in l.haloWaves) if (w != null) Destroy(w.gameObject);
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
            bool flameStyle = auraStyle == AuraStyle.Flame;
            l.a.gameObject.SetActive(visible && flameStyle && auraGlow);
            l.b.gameObject.SetActive(visible && flameStyle && auraGlow);
            foreach (var f in l.flames) f.sr.gameObject.SetActive(visible && flameStyle);
            if (l.haloBase != null) l.haloBase.gameObject.SetActive(visible && !flameStyle);
            foreach (var w in l.haloWaves) if (w != null) w.gameObject.SetActive(visible && !flameStyle);
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
            if (flameStyle) UpdateFlames(go, l, c, order, layer);
            else UpdateHaloAura(go, l, t, order, layer);
        }
    }

    // ---------- 形のオーラ(Silhouette) ----------
    void UpdateHaloAura(GameObject go, AuraLook l, float t, int order, int layer)
    {
        if (!l.haloBuilt)
        {
            l.haloBuilt = true;
            BuildHalo(go, l);
        }
        if (l.halo == null) return;

        // 枚数を合わせる
        int want = Mathf.Clamp(auraSilhouetteLayers, 0, 8);
        if (l.haloBase == null) l.haloBase = MakeHaloPart(l);
        while (l.haloWaves.Count > want) { Destroy(l.haloWaves[l.haloWaves.Count - 1].gameObject); l.haloWaves.RemoveAt(l.haloWaves.Count - 1); }
        while (l.haloWaves.Count < want) l.haloWaves.Add(MakeHaloPart(l));

        var tr = go.transform;
        Vector3 center = l.haloIsModel ? tr.TransformPoint(l.haloCenter) : tr.position;
        Vector3 baseScale = l.haloIsModel ? Vector3.one * l.haloWidth : new Vector3(tr.lossyScale.x, tr.lossyScale.y, 1f);
        float height = l.radius * 2f;   // 商品の大きさ(昇る高さ・揺れの幅の目安)
        Color col = auraColor;

        // いつも出ている光(少しちらつく)
        float flick = 0.85f + 0.15f * Mathf.Sin(t * 11f + l.phase * 3f);
        SetHaloPart(l.haloBase, center, tr.rotation, baseScale * (1f + 0.03f * Mathf.Sin(t * 7f + l.phase)),
                    new Color(col.r, col.g, col.b, col.a * 0.8f * flick), order, layer);

        // ふくらみながら上へ昇って消える光(何枚かを、時間をずらしてくり返す)
        float speed = Mathf.Max(0.05f, auraSilhouetteSpeed);
        for (int i = 0; i < l.haloWaves.Count; i++)
        {
            float ph = Mathf.Repeat(t * speed + (float)i / Mathf.Max(1, l.haloWaves.Count) + l.phase, 1f);   // 0 生まれた → 1 消える
            float grow = 1f + Mathf.Max(0f, auraSilhouetteSpread) * ph;
            Vector3 sc = new Vector3(baseScale.x * grow, baseScale.y * grow * (1f + 0.25f * ph), 1f);   // 上へ伸びる
            float sway = Mathf.Sin(t * 3f + i * 2.1f) * 0.04f * height * ph;
            Vector3 pos = center + Vector3.up * (Mathf.Max(0f, auraSilhouetteRise) * height * ph) + Vector3.right * sway;
            Quaternion rot = tr.rotation * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 4f + i * 1.7f) * 4f * ph);   // ゆらゆら傾く
            float a = col.a * 0.6f * Mathf.Pow(1f - ph, 1.5f) * Mathf.Clamp01(ph / 0.12f);
            SetHaloPart(l.haloWaves[i], pos, rot, sc, new Color(col.r, col.g, col.b, a), order, layer);
        }
    }

    SpriteRenderer MakeHaloPart(AuraLook l)
    {
        var r = new GameObject("AuraHalo").AddComponent<SpriteRenderer>();
        r.sprite = l.halo;
        return r;
    }

    static void SetHaloPart(SpriteRenderer r, Vector3 pos, Quaternion rot, Vector3 scale, Color color, int order, int layer)
    {
        r.transform.SetPositionAndRotation(pos, rot);
        r.transform.localScale = scale;
        r.color = color;
        r.sortingOrder = order;
        if (r.gameObject.layer != layer) r.gameObject.layer = layer;
    }

    // 光の形を作る: 3Dモデルの商品は撮った絵、画像の商品はその画像から
    readonly Dictionary<(Sprite, int), Sprite> haloCache = new Dictionary<(Sprite, int), Sprite>();
    const int HaloMaxSize = 128;   // 光の絵を作るときの、元の絵の縦横の長いほう(ピクセル)

    void BuildHalo(GameObject go, AuraLook l)
    {
        Sprite src = null;
        float worldWidth = 1f;   // 元の絵の横幅が、ゲームでどの長さに見えるか
        if (infoOf.TryGetValue(go, out var info) && info.model != null)
        {
            src = ModelIconSprite(info);
            if (src != null)
            {
                Bounds b = default;
                bool has = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r.GetComponent<TextMesh>() != null || r is SpriteRenderer) continue;
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
                if (!has) src = null;
                else
                {
                    l.haloIsModel = true;
                    l.haloCenter = go.transform.InverseTransformPoint(new Vector3(b.center.x, b.center.y, go.transform.position.z));
                    l.haloWidth = Mathf.Max(b.extents.x, b.extents.y) * 1.08f * 2f;   // 撮った絵の幅(ModelIconSprite と同じ)
                    worldWidth = l.haloWidth;
                }
            }
        }
        else
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) src = sr.sprite;
            if (src != null) worldWidth = src.textureRect.width / src.pixelsPerUnit * Mathf.Abs(go.transform.lossyScale.x);
        }
        if (src == null) return;
        // 光の太さ(ゲームの長さ)を、作る絵のピクセルに直す。大きな商品は 1 ピクセルが大きいので、ピクセルの数は少なくなる
        float scale = Mathf.Min(1f, HaloMaxSize / Mathf.Max(src.textureRect.width, src.textureRect.height));
        float pxPerWorld = src.textureRect.width * scale / Mathf.Max(0.01f, worldWidth);
        int widthPx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, auraSilhouetteWidth) * pxPerWorld), 1, 40);
        if (!haloCache.TryGetValue((src, widthPx), out var halo) || halo == null)
        {
            halo = MakeAuraHaloSprite(src, widthPx);
            haloCache[(src, widthPx)] = halo;
        }
        l.halo = halo;
    }

    // 絵の形を太らせて、ぼかした白い光の絵を作る(まわりに余白を足す。1ピクセルの大きさ・ピボットは元の絵と同じ見え方になるようにする)。
    // widthPx = 光の太さ(作る絵のピクセル)。太らせるのに 6 割・ぼかすのに 4 割を使う。
    // 読める絵(3Dモデルを撮った絵など)はそのまま読む。読めない設定(Read/Write OFF)の絵は、いったん RenderTexture に写してから読む
    static Sprite MakeAuraHaloSprite(Sprite src, int widthPx)
    {
        var srcPx = ReadSpritePixels(src, HaloMaxSize, out int w, out int h, out float scale);
        // 透明な所がほとんど無い(= 透明が読めていない)ときは、形が四角い光になってしまうので作らない
        int opaque = 0;
        foreach (var c in srcPx) if (c.a > 60) opaque++;
        if (opaque > srcPx.Length * 0.97f)
        {
            Debug.LogWarning($"[Shop2D] {src.name} の透明な所が読めないので、形のオーラを作れません");
            return null;
        }

        int grow = Mathf.Max(1, Mathf.RoundToInt(widthPx * 0.6f));
        int blur = Mathf.Max(1, Mathf.RoundToInt(widthPx * 0.4f));
        int pad = grow + blur * 2 + 2;   // 太らせ・ぼかしても、はみ出さない余白
        int W = w + pad * 2, H = h + pad * 2;
        var a = new float[W * H];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[(y + pad) * W + x + pad] = srcPx[y * w + x].a > 60 ? 1f : 0f;
        a = MaxFilter(a, W, H, grow);                        // 太らせる
        a = BoxBlur(BoxBlur(a, W, H, blur), W, H, blur);     // ぼかす(2回でなめらかに)
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i] * 1.2f) * 255f));
        var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        outTex.SetPixels32(px);
        outTex.Apply();
        Vector2 pivot = new Vector2((src.pivot.x * scale + pad) / W, (src.pivot.y * scale + pad) / H);
        return Sprite.Create(outTex, new Rect(0, 0, W, H), pivot, src.pixelsPerUnit * scale);
    }

    // 絵のピクセルを読む(縦横の長いほうが maxSize ピクセルまでに間引く。scale = 間引いた割合)。
    // 読める絵はそのまま、読めない設定(Read/Write OFF)の絵は、いったん RenderTexture に写してから読む
    static Color32[] ReadSpritePixels(Sprite src, int maxSize, out int w, out int h, out float scale)
    {
        var tex = src.texture;
        Rect rect = src.textureRect;
        scale = Mathf.Min(1f, maxSize / Mathf.Max(rect.width, rect.height));
        w = Mathf.Max(4, Mathf.RoundToInt(rect.width * scale));
        h = Mathf.Max(4, Mathf.RoundToInt(rect.height * scale));
        var px = new Color32[w * h];
        if (tex.isReadable)
        {
            var all = tex.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = Mathf.Clamp((int)(rect.x + (x + 0.5f) / scale), 0, tex.width - 1);
                    int sy = Mathf.Clamp((int)(rect.y + (y + 0.5f) / scale), 0, tex.height - 1);
                    px[y * w + x] = all[sy * tex.width + sx];
                }
            return px;
        }
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt, new Vector2(rect.width / tex.width, rect.height / tex.height), new Vector2(rect.x / tex.width, rect.y / tex.height));
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        read.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        px = read.GetPixels32();
        Object.Destroy(read);
        return px;
    }

    // 縦横 r ピクセルの範囲の一番大きい値(形を太らせる)
    static float[] MaxFilter(float[] a, int w, int h, int r)
    {
        var tmp = new float[a.Length];
        var o = new float[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float m = 0f;
                for (int k = Mathf.Max(0, x - r); k <= Mathf.Min(w - 1, x + r); k++) m = Mathf.Max(m, a[y * w + k]);
                tmp[y * w + x] = m;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float m = 0f;
                for (int k = Mathf.Max(0, y - r); k <= Mathf.Min(h - 1, y + r); k++) m = Mathf.Max(m, tmp[k * w + x]);
                o[y * w + x] = m;
            }
        return o;
    }

    // 縦横 r ピクセルの範囲の平均(ぼかす)
    static float[] BoxBlur(float[] a, int w, int h, int r)
    {
        var tmp = new float[a.Length];
        var o = new float[a.Length];
        float n = r * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            float sum = 0f;
            for (int k = -r; k <= r; k++) sum += a[y * w + Mathf.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[y * w + x] = sum / n;
                sum += a[y * w + Mathf.Min(w - 1, x + r + 1)] - a[y * w + Mathf.Max(0, x - r)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                o[y * w + x] = sum / n;
                sum += tmp[Mathf.Min(h - 1, y + r + 1) * w + x] - tmp[Mathf.Max(0, y - r) * w + x];
            }
        }
        return o;
    }

    // 炎: つぶを商品のふちに並べて上へ昇らせ、消えたら別の場所から生まれ直す
    void UpdateFlames(GameObject go, AuraLook l, Vector3 c, int order, int layer)
    {
        if (auraFlame && l.edge == null && l.silPts == null)
        {
            // 3Dモデルの商品は見た目の輪郭から(当たり判定は手足を包んだ大きな形なので、カニなどでは体から離れた輪になる)。
            // 画像・図形の商品と、輪郭が取れなかったときは当たり判定のふちから
            if (!(infoOf.TryGetValue(go, out var info) && info.model != null && BuildSilhouetteEdge(go, l, info))) BuildEdge(go, l);
        }
        // つぶの数 = ふちの長さ x 1 あたりの数(大きな商品ほど多い。つぶの大きさは同じ)
        int want = auraFlame ? Mathf.Clamp(Mathf.RoundToInt(l.perimeter * Mathf.Max(0f, auraFlameDensity)), 0, 250) : 0;
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

    // 3Dモデルの商品の輪郭: 献立のアイコンと同じく、モデルを正面から撮った絵(ModelIconSprite。TwoPlayerShop2D.Menu.cs)を使い、
    // 絵の色が付いた所と透明な所の境目を輪郭の点にする(2ピクセルおき)。外向きは、まわりの透明な所の向き。
    // 絵は「見た目の範囲の、縦横の長いほう x 1.08」の正方形を写しているので、今の見た目の範囲から位置を割り出す
    bool BuildSilhouetteEdge(GameObject go, AuraLook l, ProductInfo info)
    {
        var sprite = ModelIconSprite(info);
        if (sprite == null) return false;
        var tex = sprite.texture;
        Color32[] px;
        try { px = tex.GetPixels32(); } catch { return false; }
        int w = tex.width, h = tex.height;

        Bounds b = default;
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r.GetComponent<TextMesh>() != null || r is SpriteRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return false;
        float half = Mathf.Max(b.extents.x, b.extents.y) * 1.08f;   // 絵の正方形の半分の長さ(ゲームの長さ)

        const int step = 2;
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a > 80;
        var pts = new List<Vector2>();
        var normals = new List<Vector2>();
        var tr = go.transform;
        for (int y = 0; y < h; y += step)
            for (int x = 0; x < w; x += step)
            {
                if (!Solid(x, y)) continue;
                Vector2 n = Vector2.zero;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && !Solid(x + dx * step, y + dy * step)) n += new Vector2(dx, dy);
                if (n == Vector2.zero) continue;   // まわりが全部色付き = 輪郭ではない
                Vector2 world = new Vector2(b.center.x + ((x + 0.5f) / w * 2f - 1f) * half,
                                            b.center.y + ((y + 0.5f) / h * 2f - 1f) * half);
                pts.Add(tr.InverseTransformPoint(world));
                normals.Add(((Vector2)tr.InverseTransformDirection(n.normalized)).normalized);
            }
        if (pts.Count < 8) return false;
        l.silPts = pts.ToArray();
        l.silNormals = normals.ToArray();
        l.perimeter = pts.Count * (2f * half / w * step);   // 輪郭の点の数 x 点の間かく ≒ 輪郭の長さ
        return true;
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
        if (l.silPts != null)
        {
            // 3Dモデルの輪郭の点から選ぶ(点は輪郭に沿ってほぼ同じ間かくで並んでいる)
            for (int tryCount = 0; tryCount < 8; tryCount++)
            {
                int idx = Random.Range(0, l.silPts.Length);
                f.start = l.silPts[idx];
                f.normal = l.silNormals[idx];
                if (tr.TransformDirection(f.normal).y > -0.3f || Random.value < 0.25f) return;
            }
            return;
        }
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
