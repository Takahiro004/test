using System.Collections.Generic;
using UnityEngine;

// 回転の目印(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「回転の目印」と「機能のON/OFF: 操作・画面」→ Show Rotate Hint)
// 商品を掴んで自分の画面で落とすターン(持ち運び中)に、商品のまわりに時計回りの矢印2本の輪を出す(RB で回せることが分かるように)。
//  - 絵は rotateHintSprite。空ならスクリプトで描く(白い矢印 + 少し暗い縁。色 rotateHintColor をかけるので、どんな色にもなる)
//  - 大きさは商品の大きさに合わせる(輪の内側が商品の外側くらい。rotateHintSize 倍)。ただし直径 rotateHintMaxSize まで(大きな商品は重なってよい)。
//    位置は rotateHintOffset でずらす。矢印の太さは rotateHintThickness(変えたら絵を描き直す)
//  - rotateHintSpin 度/秒で時計回りにゆっくり回す(0 なら止める)
//  - 商品より手前(sortingOrder 1000。持ち運び中の商品は 999)に描く
//  - 商品(と輪)の下に説明の文字 rotateHintText(「RBで回転」など)を出す。色 rotateHintTextColor・大きさ rotateHintTextSize・
//    上下のずれ rotateHintTextY。文字を空にすると出さない
//  - LateUpdate から毎フレーム。再生中に変えてもすぐ反映
public partial class TwoPlayerShop2D
{
    class RotateHintLook
    {
        public SpriteRenderer sr;
        public GameObject held;   // どの商品に合わせた大きさか
        public float radius;      // その商品の半径(縦横の長いほうの半分)
        public float angle;       // 回している角度
        public TextMesh label;    // 説明の文字
        public string labelText;  // どの文字・大きさで作ったか(変わったら作り直す)
        public float labelSize;
    }
    readonly Dictionary<Player, RotateHintLook> rotateHints = new Dictionary<Player, RotateHintLook>();
    static Sprite rotateHintDefaultSprite;
    static float rotateHintBuiltThickness;
    const float RotateHintRadius = 0.74f;   // 描いた輪の真ん中の半径(絵の幅を 2 として)

    void UpdateRotateHints()
    {
        UpdateRotateHint(p1);
        UpdateRotateHint(p2);
    }

    void UpdateRotateHint(Player p)
    {
        if (p == null) return;
        bool show = showRotateHint && rotateHeldProduct && p.held != null && !p.inShop && !IsTimeUp;
        rotateHints.TryGetValue(p, out var h);
        if (!show)
        {
            if (h != null && h.sr != null) h.sr.enabled = false;
            if (h != null && h.label != null) h.label.gameObject.SetActive(false);
            return;
        }
        if (h == null || h.sr == null)
        {
            var go = new GameObject($"RotateHint_{p.name}");
            h = new RotateHintLook { sr = go.AddComponent<SpriteRenderer>() };
            rotateHints[p] = h;
        }
        // 掴んだ商品が変わったら、その商品の大きさを測る(回しても輪の大きさが変わらないように、掴んだときに1回だけ)
        if (h.held != p.held)
        {
            h.held = p.held;
            h.radius = HeldRadius(p.held);
            h.angle = 0f;
        }

        var sprite = rotateHintSprite != null ? rotateHintSprite : GetRotateHintSprite(rotateHintThickness);
        h.sr.enabled = true;
        h.sr.sprite = sprite;
        h.sr.color = rotateHintColor;
        h.sr.sortingOrder = 1000;   // 持ち運び中の商品(999)の手前
        if (h.sr.gameObject.layer != p.held.layer) SetLayer(h.sr.gameObject, p.held.layer);

        // 輪の内側が、商品の外側に来る大きさ x rotateHintSize。直径は rotateHintMaxSize まで(大きな商品は重なってよい)
        float spriteW = Mathf.Max(0.01f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));
        float inner = rotateHintSprite != null ? 0.64f : RotateHintRadius - RotateHintHalfWidth(rotateHintThickness);
        float diameter = h.radius * 2f / Mathf.Max(0.1f, inner) * Mathf.Max(0.05f, rotateHintSize);
        if (rotateHintMaxSize > 0f) diameter = Mathf.Min(diameter, rotateHintMaxSize);
        h.sr.transform.localScale = Vector3.one * (diameter / spriteW);

        h.angle -= rotateHintSpin * Time.deltaTime;   // 時計回り(RB で回る向きと同じ)
        h.sr.transform.rotation = Quaternion.Euler(0f, 0f, h.angle);
        Vector3 c = p.held.transform.position;
        h.sr.transform.position = new Vector3(c.x + rotateHintOffset.x, c.y + rotateHintOffset.y, 0f);

        UpdateRotateHintLabel(p, h, c, diameter / 2f * (RotateHintRadius + RotateHintHalfWidth(rotateHintThickness) * 2.2f));
    }

    // 説明の文字(商品と輪の、下の端より少し下。回さない)
    void UpdateRotateHintLabel(Player p, RotateHintLook h, Vector3 c, float ringRadius)
    {
        string text = rotateHintText ?? "";
        float size = HudTextSize * 0.6f * Mathf.Max(0.1f, rotateHintTextSize);
        if (h.label != null && (h.labelText != text || !Mathf.Approximately(h.labelSize, size)))
        {
            Destroy(h.label.gameObject);
            h.label = null;
        }
        if (text.Length == 0) return;
        if (h.label == null)
        {
            h.label = MakeText($"RotateHintText_{p.name}", null, p.held.layer, text, rotateHintTextColor, 1001);
            SetTextSize(h.label, size);   // 影は付けない(二重に見えて読みにくいため)
            h.labelText = text;
            h.labelSize = size;
        }
        h.label.gameObject.SetActive(true);
        if (h.label.gameObject.layer != p.held.layer) SetLayer(h.label.gameObject, p.held.layer);
        h.label.color = rotateHintTextColor;
        float textH = h.label.GetComponent<MeshRenderer>().bounds.size.y;
        float bottom = Mathf.Max(h.radius, ringRadius);   // 商品と輪の、下に出ているほう
        h.label.transform.position = new Vector3(c.x, c.y - bottom - textH / 2f - 0.1f + rotateHintTextY, 0f);
    }

    // 商品の半径(見た目の範囲の、縦横の長いほうの半分。真ん中は商品の位置)
    static float HeldRadius(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0.5f;
        Vector3 pos = go.transform.position;
        float r = 0.1f;
        foreach (var rd in renderers)
        {
            if (!rd.enabled || rd.GetComponent<TextMesh>() != null) continue;   // 商品名などの文字は数えない
            var b = rd.bounds;
            r = Mathf.Max(r, Mathf.Abs(b.max.x - pos.x), Mathf.Abs(b.min.x - pos.x), Mathf.Abs(b.max.y - pos.y), Mathf.Abs(b.min.y - pos.y));
        }
        return r;
    }

    // 輪の太さの半分(絵の幅を 2 として)。thickness 1 で 0.10
    static float RotateHintHalfWidth(float thickness) => 0.10f * Mathf.Clamp(thickness, 0.2f, 2f);

    // 時計回りの矢印2本の輪(白 + 少し暗い縁。幅 1)。太さが変わったら描き直す
    static Sprite GetRotateHintSprite(float thickness)
    {
        thickness = Mathf.Clamp(thickness, 0.2f, 2f);
        if (rotateHintDefaultSprite != null && Mathf.Approximately(rotateHintBuiltThickness, thickness)) return rotateHintDefaultSprite;
        if (rotateHintDefaultSprite != null) { Destroy(rotateHintDefaultSprite.texture); Destroy(rotateHintDefaultSprite); }
        rotateHintBuiltThickness = thickness;
        float hw = RotateHintHalfWidth(thickness), headHw = hw * 2.2f;
        const int size = 256;
        const int ss = 3;   // ふちをなめらかにするため、1ピクセルを 3x3 に分けて数える
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int fill = 0, edge = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / ss) / size) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / ss) / size) * 2f - 1f;
                        if (InRotateArrow(u, v, hw * 0.3f, hw, headHw)) fill++;   // 縁の太さは輪の太さの3割
                        else if (InRotateArrow(u, v, 0f, hw, headHw)) edge++;
                    }
                int n = ss * ss;
                float a = (fill + edge) / (float)n;
                float g = a > 0f ? (fill + edge * 0.45f) / (fill + edge) : 1f;   // 縁は少し暗く(色をかけると、同じ色の濃いめの縁になる)
                px[y * size + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        rotateHintDefaultSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return rotateHintDefaultSprite;
    }

    // 点 (u, v)(真ん中 0・端 ±1)が矢印の中か。inset だけ内側に縮めた形で調べる(縁を作るため)
    // hw = 輪の太さの半分・headHw = 矢じりの根元の幅の半分
    static bool InRotateArrow(float u, float v, float inset, float hw, float headHw)
    {
        const float rc = RotateHintRadius;   // 輪の真ん中の半径
        const float body = 115f;    // 輪の部分の角度
        const float head = 30f;     // 矢じりの部分の角度
        float r = Mathf.Sqrt(u * u + v * v);
        if (r < 0.01f) return false;
        float a = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
        float insetAng = inset / r * Mathf.Rad2Deg;
        float dr = Mathf.Abs(r - rc);
        foreach (float start in new[] { 150f, -30f })   // 左上から右へ、右下から左へ(どちらも時計回り)
        {
            float d = Mathf.Repeat(start - a, 360f);   // 矢印の始まりから、時計回りに何度進んだ所か
            if (d >= insetAng && d <= body && dr <= hw - inset) return true;   // 輪の部分
            if (d >= body && d <= body + head)
            {
                float t = (d - body) / head;   // 矢じりの根元 0 〜 先 1
                float w = headHw * (1f - t) - inset;
                if (dr <= hw - inset) { if (dr <= w) return true; }       // 輪の続きの部分
                else if (d >= body + insetAng && dr <= w) return true;    // 矢じりの張り出した部分(根元の縁も作る)
            }
        }
        return false;
    }
}
