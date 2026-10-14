using UnityEngine;

// 四角・丸・矢印・ゲージの形の自動生成(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
public partial class TwoPlayerShop2D
{
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
