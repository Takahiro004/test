using System.Collections.Generic;
using UnityEngine;

// 商品1つぶんの設定(Inspector の「商品の一覧」の1行)
// 画像(sprite)か 3Dモデル(model)を入れると、仮の図形の代わりにそれが出る。両方空なら shape の図形(色はランクごと)
[System.Serializable]
public class ProductInfo
{
    [Tooltip("商品の名前(棚の値札に出る)")]
    public string name;
    [Tooltip("ランク(1〜5)。仮の図形の色がランクごとに変わる")]
    [Range(1, 5)] public int rank = 1;
    [Tooltip("金額(袋に入ったら合計に足される)")]
    public int price = 100;
    [Tooltip("献立の食材(必ず棚に並べる)")]
    public bool menuIngredient;
    [Tooltip("重さ(1が普通)。重い商品ほど、下の割れる商品に重さがかかる")]
    public float weight = 1f;
    [Tooltip("割れる商品(たまご など)。袋の中で上に重さが乗ると割れて消える")]
    public bool fragile;
    [Tooltip("割れる商品が割れる重さ(重さ 1 の商品が何個ぶん乗ったら割れるか)")]
    public float breakLoad = 3f;
    [Tooltip("画像も3Dモデルも無いときの、仮の図形の形(当たり判定も同じ形)")]
    public ProductShape shape = ProductShape.Square;
    [Tooltip("商品の画像。入れると図形の代わりに出て、当たり判定は画像の形になる")]
    public Sprite sprite;
    [Tooltip("この商品の大きさ(1で普通。1.5なら1.5倍、0.7なら7割)。画像・3Dモデル・仮の図形のどれにも効き、当たり判定も一緒に変わる")]
    public float size = 1f;
    [Tooltip("商品の3Dモデル(プレハブ)。入れると図形・画像の代わりに出る。当たり判定はモデルを囲む四角。暗く・黒く見えたらマテリアルを Unlit にする")]
    public GameObject model;
    [Tooltip("3Dモデルの向き(度)。正面がこちらを向かないときに変える")]
    public Vector3 modelRotation;

    public ProductInfo() { }
    public ProductInfo(string name, int rank, int price, bool menuIngredient, ProductShape shape)
    {
        this.name = name; this.rank = rank; this.price = price; this.menuIngredient = menuIngredient; this.shape = shape;
    }

    // 2026-10-21 にチームで決めた商品(商品一覧.xlsx)。献立の食材(★)を先に並べてある
    // 金額は価格帯の中の仮の値(ランク1: 100 / 2: 200 / 3: 500 / 4: 2000 / 5: 10000)。決まったら Inspector で直す
    public static ProductInfo[] Defaults()
    {
        int[] price = { 0, 100, 200, 500, 2000, 10000 };
        (string name, int rank, bool menu)[] table = {
            // 献立の食材(★)
            ("にんじん", 1, true), ("じゃがいも", 1, true),
            ("たまご", 2, true), ("ぎゅうにゅう", 2, true), ("すぱげてぃ", 2, true), ("べーこん", 2, true),
            ("ぶろっこりー", 2, true), ("しょうゆ", 2, true),
            ("ゆでえび", 3, true), ("ばたー", 3, true), ("めんつゆ", 3, true), ("おりーぶおいる", 3, true),
            ("ぎゅうにく", 4, true), ("まぐろ", 4, true),
            ("ずわいかに", 5, true), ("たい", 5, true), ("ひらめ", 5, true),
            // ほかの商品
            ("うどん", 1, false), ("やきそば", 1, false), ("ちくわ", 1, false), ("なっとう", 1, false), ("ばなな", 1, false),
            ("ぐみ", 1, false), ("らむね", 1, false), ("おちゃ", 1, false), ("りんご", 1, false), ("いわし", 1, false),
            ("あいすばー", 1, false), ("めかぶ", 1, false), ("むきえだまめ", 1, false),
            ("さつまいも", 2, false), ("ころっけ", 2, false), ("ぽてち", 2, false), ("こーら", 2, false), ("だんご", 2, false),
            ("ぱぷりか", 2, false), ("りんごじゅーす", 2, false), ("ちょこれーと", 2, false), ("もなか", 2, false),
            ("さんま", 3, false), ("からあげ", 3, false), ("とんかつ", 3, false), ("ぎょうざ", 3, false), ("ろーす", 3, false),
            ("てばさき", 3, false), ("ぽてとさらだ", 3, false), ("つみれ", 3, false), ("ぷりんあらもーど", 3, false),
            ("ずんだもち", 3, false), ("うめぼし", 3, false),
            ("かるび", 4, false), ("ねぎたんしお", 4, false), ("らむにく", 4, false), ("もどりがつお", 4, false),
            ("さくらんぼ", 4, false), ("ぶどう", 4, false), ("ふるーつもり", 4, false),
            ("きゃびあ", 5, false), ("いちぼ", 5, false), ("ろーすとびーふ", 5, false), ("なまはむ", 5, false),
        };
        var shapes = (ProductShape[])System.Enum.GetValues(typeof(ProductShape));
        var list = new ProductInfo[table.Length];
        for (int i = 0; i < table.Length; i++)   // 仮の図形は、見分けやすいように順番に変える
            list[i] = new ProductInfo(table[i].name, table[i].rank, price[table[i].rank], table[i].menu, shapes[i % shapes.Length]);
        foreach (var p in list)   // 割れる商品(仮。ほかにも割れるものがあれば一覧で Fragile を付ける)
            if (p.name == "たまご") p.fragile = true;
        return list;
    }
}

// 商品の一覧から棚の商品を作る・3Dモデルの商品(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「商品の一覧」)
public partial class TwoPlayerShop2D
{
    // 棚の i 番目に置く商品。一覧が棚より短いときは最初から繰り返す
    // 一覧が空なら、前の設定(Product Shapes / Sprites / Prices)から作る
    ProductInfo ProductFor(int i)
    {
        if (productList != null && productList.Length > 0) return productList[i % productList.Length];
        return new ProductInfo
        {
            name = $"Product_{i + 1}",
            price = productPrices != null && productPrices.Length > 0 ? productPrices[i % productPrices.Length] : 0,
            shape = productShapes != null && productShapes.Length > 0 ? productShapes[i % productShapes.Length] : ProductShape.Square,
            sprite = productSprites != null && productSprites.Length > 0 ? productSprites[i % productSprites.Length] : null,
        };
    }

    // 仮の図形の色(ランクごと。1 緑 → 5 紫)
    static Color RankColor(int rank)
    {
        switch (rank)
        {
            case 1: return new Color(0.4f, 0.85f, 0.45f);
            case 2: return new Color(0.95f, 0.9f, 0.3f);
            case 3: return new Color(1f, 0.6f, 0.2f);
            case 4: return new Color(0.95f, 0.3f, 0.3f);
            default: return new Color(0.75f, 0.4f, 0.95f);
        }
    }

    // 3Dモデルの商品: 親に当たり判定と、見えない SpriteRenderer(色・手前の順番の処理用)を付ける
    // 当たり判定は、モデルを画面の向き(手前から)見たときの輪郭。カメラは一方向だけなので、
    // モデルの頂点を画面の平面(x, y)に落として、それを全部包む形(凸包)を作り、点を減らして PolygonCollider2D にする。
    // へこんだ形は、へこみが埋まった形になる。モデルの頂点が読めないとき(ビルドで Read/Write が OFF など)は、囲む四角
    GameObject MakeModelProduct(string objName, Vector2 pos, ProductInfo info, int layer)
    {
        var go = new GameObject(objName);
        go.AddComponent<SpriteRenderer>();   // 絵は無し(ほかの処理が SpriteRenderer を使うので付けておく)

        var model = Instantiate(info.model, go.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(info.modelRotation);
        model.transform.localScale = Vector3.one;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            // 縦横の長いほうが productSize になる大きさにして、真ん中を親の位置に合わせる
            float k = productSize / Mathf.Max(b.size.x, b.size.y, 0.01f);
            var outline = ModelOutline(info, model, b.center);   // 大きさ 1 のときの輪郭(真ん中が 0)
            model.transform.localScale = Vector3.one * k;
            model.transform.localPosition = -b.center * k;
            if (outline != null)
            {
                var pts = new Vector2[outline.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = outline[i] * k;
                go.AddComponent<PolygonCollider2D>().points = pts;   // 親には絵が無いので、Unity が勝手に形を作ることはない
            }
            else
            {
                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(b.size.x, b.size.y) * k;
            }
        }
        else
        {
            Debug.LogWarning($"[Shop2D] {info.name} の3Dモデルに見た目(Renderer)がありません");
            go.AddComponent<BoxCollider2D>().size = Vector2.one * productSize;
        }

        go.transform.position = pos;
        SetLayer(go, layer);
        return go;
    }

    // 3Dモデルの、画面の向きから見た輪郭(大きさ 1 のとき。真ん中 center を 0 にした点)。
    // モデルと向きごとに1回だけ計算する。頂点が読めなければ null
    readonly Dictionary<string, Vector2[]> modelOutlineCache = new Dictionary<string, Vector2[]>();

    Vector2[] ModelOutline(ProductInfo info, GameObject model, Vector3 center)
    {
        string key = $"{info.model.GetInstanceID()}_{info.modelRotation}";
        if (modelOutlineCache.TryGetValue(key, out var cached)) return cached;

        // モデルは今、親(商品)の原点・大きさ 1 で置いてある。頂点を親の座標にして、画面の平面に落とす
        var points = new List<Vector2>();
        var parent = model.transform.parent;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            AddMeshPoints(mf.sharedMesh, mf.transform, parent, center, points);
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            AddMeshPoints(skin.sharedMesh, skin.transform, parent, center, points);

        Vector2[] result = null;
        if (points.Count >= 3)
        {
            var hull = ConvexHull(points);
            float size = 0.01f;
            foreach (var p in hull) size = Mathf.Max(size, Mathf.Abs(p.x) * 2f, Mathf.Abs(p.y) * 2f);
            // 点を減らす(画像の商品と同じ。上限を超えていたら、大まかさを少しずつ上げてやり直す)
            float eps = size * imageColliderDetail;
            var simple = hull;
            for (int n = 0; n < 20; n++)
            {
                simple = SimplifyClosed(hull, eps);
                if (simple.Length <= Mathf.Max(4, imageColliderMaxPoints)) break;
                eps *= 1.5f;
            }
            result = simple.Length >= 3 ? simple : hull;
        }
        else Debug.LogWarning($"[Shop2D] {info.name} の3Dモデルの頂点が読めないので、当たり判定は四角にした(モデルの Import Settings で Read/Write にチェック)");
        modelOutlineCache[key] = result;
        return result;
    }

    static void AddMeshPoints(Mesh mesh, Transform meshTransform, Transform parent, Vector3 center, List<Vector2> points)
    {
        if (mesh == null) return;
#if !UNITY_EDITOR
        if (!mesh.isReadable) return;   // ビルドしたゲームでは、Import Settings の Read/Write が ON のモデルだけ頂点が読める
#endif
        foreach (var v in mesh.vertices)
        {
            Vector3 local = parent.InverseTransformPoint(meshTransform.TransformPoint(v)) - center;
            points.Add(new Vector2(local.x, local.y));
        }
    }

    // 棚のマスに置く商品の設定(ランダム補充なら山札から引いたもの、そうでなければ一覧の順)
    ProductInfo SlotInfo(ShelfSlot slot)
    {
        if (slot.product >= 0 && productList != null && slot.product < productList.Length) return productList[slot.product];
        return ProductFor(slots.IndexOf(slot));
    }

    // 棚の値札(商品の名前と金額)。マスごとに1つ作り、商品の上のあたりに出す(棚と一緒に流れる)
    void BuildPriceTags(Rect area)
    {
        if (!showPriceTags || shelfRoot == null) return;
        float cellW = area.width / ShelfColumns, cellH = area.height / ShelfRows;
        float size = Mathf.Min(cellW, cellH);
        for (int i = 0; i < slots.Count; i++)
        {
            int section = i / ShelfCount, j = i % ShelfCount;   // どの棚の、何番目か
            if (section >= shelfSections.Count) break;
            float x = area.xMin + cellW * (j % ShelfColumns + 0.5f);
            float rowTop = area.yMax - cellH * (j / ShelfColumns);
            var tag = MakeText($"PriceTag{i + 1}", shelfSections[section], layerShop, "", Color.white, -15);
            SetTextSize(tag, 0.012f * size);
            tag.anchor = TextAnchor.UpperCenter;
            tag.alignment = TextAlignment.Center;
            tag.transform.localPosition = new Vector3(x, rowTop - 0.1f, 0f);
            slots[i].tag = tag;
            SetSlotTag(slots[i]);
        }
    }

    // 値札を、今そのマスにある商品にする(空きなら消す)
    void SetSlotTag(ShelfSlot slot)
    {
        if (slot.tag == null) return;
        if (slot.go == null) { slot.tag.text = ""; return; }
        var info = SlotInfo(slot);
        slot.tag.text = $"{info.name}\n¥{info.price:N0}";
        slot.tag.color = info.menuIngredient ? new Color(1f, 0.92f, 0.4f) : Color.white;
    }

    // ---------- 画像の商品の当たり判定 ----------
    // Unity が画像から自動で作る形は、細かい画像(すぱげてぃ など)だと点が何百にもなり、
    // 積み重なると物理の計算がとても重くなる。点の数を imageColliderMaxPoints 以下に減らした形を使う(画像ごとに1回だけ計算)
    readonly Dictionary<Sprite, Vector2[]> outlineCache = new Dictionary<Sprite, Vector2[]>();

    Vector2[] SimpleOutline(Sprite sprite)
    {
        if (outlineCache.TryGetValue(sprite, out var cached)) return cached;

        // 画像の形(Physics Shape)。いちばん大きいかたまりを使う
        var all = new List<Vector2>();
        List<Vector2> best = null;
        float bestArea = 0f, totalArea = 0f;
        for (int i = 0; i < sprite.GetPhysicsShapeCount(); i++)
        {
            var path = new List<Vector2>();
            sprite.GetPhysicsShape(i, path);
            all.AddRange(path);
            float area = Mathf.Abs(PolygonArea(path));
            totalArea += area;
            if (area > bestArea) { bestArea = area; best = path; }
        }

        Vector2[] result;
        // 形が無い・小さいかたまりに分かれている(細い麺など)ときは、全部を包む形(凸包)にする
        if (best == null || best.Count < 3 || bestArea < totalArea * 0.5f)
        {
            if (all.Count < 3) all.AddRange(sprite.vertices);
            result = ConvexHull(all);
        }
        else result = best.ToArray();

        // 点を減らす(上限を超えていたら、大まかさを少しずつ上げてやり直す)
        float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        float eps = size * imageColliderDetail;
        var simple = result;
        for (int n = 0; n < 20; n++)
        {
            simple = SimplifyClosed(result, eps);
            if (simple.Length <= Mathf.Max(4, imageColliderMaxPoints)) break;
            eps *= 1.5f;
        }
        if (simple.Length < 3) simple = ConvexHull(new List<Vector2>(result));
        outlineCache[sprite] = simple;
        return simple;
    }

    static float PolygonArea(List<Vector2> pts)
    {
        float a = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i], q = pts[(i + 1) % pts.Count];
            a += p.x * q.y - q.x * p.y;
        }
        return a / 2f;
    }

    // 点を全部包む、いちばん小さい凸の形
    static Vector2[] ConvexHull(List<Vector2> pts)
    {
        pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var hull = new List<Vector2>();
        for (int pass = 0; pass < 2; pass++)   // 下側 → 上側
        {
            int start = hull.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull.ToArray();
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    // 閉じた形の点を減らす(形からのずれが eps 以下の点を消す。ダグラス・ポイカー法)
    static Vector2[] SimplifyClosed(Vector2[] pts, float eps)
    {
        if (pts.Length <= 4) return pts;
        // いちばん離れた2点で2本の線に分けて、それぞれ減らす
        int far = 0;
        float farDist = 0f;
        for (int i = 1; i < pts.Length; i++)
        {
            float d = (pts[i] - pts[0]).sqrMagnitude;
            if (d > farDist) { farDist = d; far = i; }
        }
        var keep = new bool[pts.Length];
        keep[0] = keep[far] = true;
        Mark(pts, 0, far, eps, keep);
        Mark(pts, far, pts.Length, eps, keep);   // pts.Length は 0 番に戻る
        var list = new List<Vector2>();
        for (int i = 0; i < pts.Length; i++) if (keep[i]) list.Add(pts[i]);
        return list.ToArray();
    }

    static void Mark(Vector2[] pts, int from, int to, float eps, bool[] keep)
    {
        if (to - from < 2) return;
        Vector2 a = pts[from], b = pts[to % pts.Length];
        int idx = -1;
        float max = eps;
        for (int i = from + 1; i < to; i++)
        {
            float d = DistanceToSegment(pts[i], a, b);
            if (d > max) { max = d; idx = i; }
        }
        if (idx < 0) return;
        keep[idx] = true;
        Mark(pts, from, idx, eps, keep);
        Mark(pts, idx, to, eps, keep);
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }
}
