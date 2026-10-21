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
    [Tooltip("画像も3Dモデルも無いときの、仮の図形の形(当たり判定も同じ形)")]
    public ProductShape shape = ProductShape.Square;
    [Tooltip("商品の画像。入れると図形の代わりに出て、当たり判定は画像の形になる")]
    public Sprite sprite;
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

    // 3Dモデルの商品: 親に当たり判定(モデルを囲む四角)と、見えない SpriteRenderer(色・手前の順番の処理用)を付ける
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
            model.transform.localScale = Vector3.one * k;
            model.transform.localPosition = -b.center * k;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(b.size.x, b.size.y) * k;
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

    // 棚の値札(商品の名前と金額)。段の板の上、商品の上のあたりに出す
    void BuildPriceTags(Rect area)
    {
        if (!showPriceTags || shelfRoot == null) return;
        float cellW = area.width / ShelfColumns, cellH = area.height / ShelfRows;
        float size = Mathf.Min(cellW, cellH);
        for (int i = 0; i < slots.Count; i++)
        {
            var info = ProductFor(i);
            float x = area.xMin + cellW * (i % ShelfColumns + 0.5f);
            float rowTop = area.yMax - cellH * (i / ShelfColumns);
            var tag = MakeText($"PriceTag{i + 1}", shelfRoot, layerShop, $"{info.name}\n¥{info.price:N0}",
                               info.menuIngredient ? new Color(1f, 0.92f, 0.4f) : Color.white, -15);
            tag.characterSize = 0.012f * size;
            tag.anchor = TextAnchor.UpperCenter;
            tag.alignment = TextAlignment.Center;
            tag.transform.position = new Vector3(x, rowTop - 0.1f, 0f);
        }
    }
}
