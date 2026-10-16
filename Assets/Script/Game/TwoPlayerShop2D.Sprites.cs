using System.Collections.Generic;
using UnityEngine;

// 商品の形(画像が無いときのテスト用)
public enum ProductShape
{
    Square,     // 四角
    Circle,     // 丸
    Triangle,   // 三角
    Pentagon,   // 五角形
    Hexagon,    // 六角形
    Rectangle,  // 長方形(横長 2:1)
    LShape,     // L字(四角3つ)
    TShape,     // T字(四角4つ)
}

// 商品の図形・矢印・ゲージの形の自動生成(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
public partial class TwoPlayerShop2D
{
    static readonly Dictionary<ProductShape, Sprite> shapeSprites = new Dictionary<ProductShape, Sprite>();

    // 図形の角の点(1x1 の中、真ん中が 0。左回り)。当たり判定と絵の両方に使う
    static Vector2[] ShapePoints(ProductShape shape)
    {
        const float t = 1f / 6f;   // T字の太さの半分(1/3 の半分)
        switch (shape)
        {
            case ProductShape.Triangle:
                return new[] { new Vector2(-0.5f, -0.433f), new Vector2(0.5f, -0.433f), new Vector2(0f, 0.433f) };
            case ProductShape.Pentagon: return RegularPolygon(5);
            case ProductShape.Hexagon: return RegularPolygon(6);
            case ProductShape.Rectangle:
                return new[] { new Vector2(-0.5f, -0.25f), new Vector2(0.5f, -0.25f), new Vector2(0.5f, 0.25f), new Vector2(-0.5f, 0.25f) };
            case ProductShape.LShape:
                return new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0f),
                               new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(-0.5f, 0.5f) };
            case ProductShape.TShape:
                return new[] { new Vector2(-t, -0.5f), new Vector2(t, -0.5f), new Vector2(t, t), new Vector2(0.5f, t),
                               new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f), new Vector2(-0.5f, t), new Vector2(-t, t) };
            default:
                return new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
        }
    }

    // 正多角形(角が上。半径 0.5)
    static Vector2[] RegularPolygon(int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI / 2f + i * 2f * Mathf.PI / n;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
        }
        return pts;
    }

    static Sprite GetShapeSprite(ProductShape shape)
    {
        if (shape == ProductShape.Square) return GetSquareSprite();
        if (shape == ProductShape.Circle) return GetCircleSprite();
        if (shapeSprites.TryGetValue(shape, out var cached) && cached != null) return cached;

        const int size = 64;
        var pts = ShapePoints(shape);
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                px[y * size + x] = InsidePolygon(p, pts) ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        shapeSprites[shape] = sprite;
        return sprite;
    }

    // 点が多角形の中にあるか(へこんだ形でもよい)
    static bool InsidePolygon(Vector2 p, Vector2[] pts)
    {
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            if ((pts[i].y > p.y) != (pts[j].y > p.y) &&
                p.x < (pts[j].x - pts[i].x) * (p.y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                inside = !inside;
        }
        return inside;
    }

    // ---------- スプライト生成 ----------

    static Sprite GetSquareSprite()
    {
        if (squareSprite != null) return squareSprite;
        var tex = new Texture2D(64, 64);
        var px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        squareSprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
        return squareSprite;
    }

    static Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int size = 64;
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                px[y * size + x] = (dx * dx + dy * dy <= r * r) ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        return circleSprite;
    }

    // ゲージの形: 左(外側)が細く、右(真ん中側)に向かって太くなる。両端は斜めに切る。左端の中央がピボット
    static Sprite GetGaugeSprite()
    {
        if (gaugeSprite != null) return gaugeSprite;
        const int w = 256, h = 64;
        var tex = new Texture2D(w, h);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float top = Mathf.Lerp(0.55f, 1f, u);      // 上のふちは真ん中に向かって上がる
                float bottom = Mathf.Lerp(0.2f, 0f, u);    // 下のふちは少し下がる
                bool inside = v >= bottom && v <= top &&
                              u >= 0.06f * (1f - v) &&           // 外側の端を斜めに切る
                              u <= 1f - 0.08f * v;               // 真ん中側の端を斜めに切る
                px[y * w + x] = inside ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        gaugeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), w);   // 幅1 x 高さ0.25
        return gaugeSprite;
    }

    // 左上向きの三角形(矢印カーソル)。先端がピボット
    static Sprite GetArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;
        const int size = 64;
        var tex = new Texture2D(size, size);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int fromTop = (size - 1) - y;
                bool inside = x <= fromTop * 0.6f && fromTop <= size - 8 && x <= size - 8 - fromTop * 0.2f && fromTop >= x * 1.3f;
                px[y * size + x] = inside ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 1f), 64f);
        return arrowSprite;
    }
}
