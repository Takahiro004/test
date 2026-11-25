using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// サイバーパンク風の見た目(かくしコマンド。TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「サイバーパンク風(かくしコマンド)」)
// エントリー画面で数字キーを cyberCode(初め「2077」)の順に打つと、3つの画面全部に特殊なノイズ(ハッキング風。おばあさんと同じ GlitchFx)が
// cyberGlitchTime 秒走り、そのあいだに見た目がサイバーパンク風に切り替わる。もう一度打つと元に戻る(同じくノイズが走る)。
//  - 切り替わる物: 3つの画面の背景の色(暗い紫)・1P/2P の画面の奥に、ネオンの街と光る床の格子の絵(スクリプトで描く)・
//    3つの画面にうっすら走査線・ときどき小さなノイズ(cyberIdleGlitch)
//  - Inspector の cyberTheme を ON/OFF しても切り替わる(ノイズなし)
//  - 数字キーは、キーボードの上の列でも、テンキーでもよい
public partial class TwoPlayerShop2D
{
    string cyberTyped = "";
    bool cyberApplied;
    bool cyberSwitching;
    SpriteRenderer[] cyberBacks;     // 1P/2P の画面の奥のネオンの絵
    SpriteRenderer[] cyberScans;     // 3つの画面の走査線
    GlitchFx[] cyberIdle;            // 3つの画面の、ときどき出る小さなノイズ
    static Sprite cyberBackSprite;

    // LateUpdate から毎フレーム
    void UpdateCyber()
    {
        if (switcher == null || p1 == null) return;
        // かくしコマンド(エントリー画面だけ)
        if (InEntry && !cyberSwitching)
        {
            char c = TypedDigit();
            if (c != '\0')
            {
                cyberTyped += c;
                string code = string.IsNullOrEmpty(cyberCode) ? "2077" : cyberCode;
                if (cyberTyped.Length > code.Length) cyberTyped = cyberTyped.Substring(cyberTyped.Length - code.Length);
                if (cyberTyped == code)
                {
                    cyberTyped = "";
                    StartCoroutine(CyberSwitch(!cyberTheme));
                }
            }
        }
        // Inspector で切り替えたとき(ノイズなし)
        if (!cyberSwitching && cyberTheme != cyberApplied) ApplyCyber(cyberTheme);
        if (cyberApplied) KeepCyber();
    }

    // このフレームに押された数字キー(上の列・テンキー)。無ければ '\0'
    char TypedDigit()
    {
        var k = Keyboard.current;
        if (k == null) return '\0';
        Key[] top = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        Key[] pad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
        for (int i = 0; i < 10; i++)
            if (k[top[i]].wasPressedThisFrame || k[pad[i]].wasPressedThisFrame) return (char)('0' + i);
        return '\0';
    }

    // 3つの画面にノイズを走らせ、真ん中で見た目を切り替える
    IEnumerator CyberSwitch(bool on)
    {
        cyberSwitching = true;
        var fx = new[] { MakeGlitch(layerP1, false), MakeGlitch(layerShop, true), MakeGlitch(layerP2, false) };
        if (grannyNoiseSound && grannyRustleVolume > 0f) GrannyNoiseSound(true);
        float time = Mathf.Max(0.2f, cyberGlitchTime);
        bool switched = false;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            float power = Mathf.Sin(k * Mathf.PI);          // 0 → 1 → 0(真ん中でいちばん強い)
            foreach (var g in fx) StepGlitch(g, Mathf.Clamp01(power * 1.2f), t);
            if (!switched && k >= 0.5f) { switched = true; cyberTheme = on; ApplyCyber(on); }
            yield return null;
        }
        if (!switched) { cyberTheme = on; ApplyCyber(on); }
        if (grannyAudio != null) grannyAudio.Stop();
        foreach (var g in fx) Destroy(g.root.gameObject);
        cyberSwitching = false;
    }

    // 見た目を切り替える
    void ApplyCyber(bool on)
    {
        cyberApplied = on;
        // 背景の色
        int n = switcher.screens != null ? switcher.screens.Length : 0;
        for (int i = 0; i < n; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;
            if (on) cam.backgroundColor = cyberBackColor;
            else if (i == screenP1) cam.backgroundColor = p1BackColor;
            else if (i == screenShop) cam.backgroundColor = shopBackColor;
            else if (i == screenP2) cam.backgroundColor = p2BackColor;
        }
        // 奥の絵・走査線・小さなノイズ
        if (cyberBacks != null) foreach (var b in cyberBacks) if (b != null) Destroy(b.gameObject);
        if (cyberScans != null) foreach (var s in cyberScans) if (s != null) Destroy(s.gameObject);
        if (cyberIdle != null) foreach (var g in cyberIdle) if (g != null) Destroy(g.root.gameObject);
        cyberBacks = null; cyberScans = null; cyberIdle = null;
        if (!on) return;
        int[] players = { layerP1, layerP2 };
        cyberBacks = new SpriteRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            var sr = new GameObject("CyberBack").AddComponent<SpriteRenderer>();
            sr.sprite = cyberBackgroundSprite != null ? cyberBackgroundSprite : GetCyberBackSprite();
            sr.gameObject.layer = players[i];
            sr.sortingOrder = -110;   // いちばん奥(袋・机より奥)
            cyberBacks[i] = sr;
        }
        int[] all = { layerP1, layerShop, layerP2 };
        cyberScans = new SpriteRenderer[3];
        cyberIdle = new GlitchFx[3];
        for (int i = 0; i < 3; i++)
        {
            var sr = new GameObject("CyberScan").AddComponent<SpriteRenderer>();
            sr.sprite = GetScanlineSprite();
            sr.gameObject.layer = all[i];
            sr.sortingOrder = 4400;
            cyberScans[i] = sr;
            cyberIdle[i] = MakeGlitch(all[i], all[i] == layerShop);
        }
        KeepCyber();
    }

    // 毎フレーム: 奥の絵・走査線を画面に合わせる(持ち上げでカメラが上がっても)。ときどき小さなノイズ
    void KeepCyber()
    {
        Vector2 pv = PlayerViewSize(), sv = ShopViewSize();
        // 背景の色(画面の割り当てを入れ替えたときに戻されても、また暗くする)
        if (switcher.screens != null)
            foreach (var sc in switcher.screens) if (sc.camera != null) sc.camera.backgroundColor = cyberBackColor;
        if (cyberBacks != null)
            foreach (var b in cyberBacks)
            {
                if (b == null) continue;
                Vector2 bs = b.sprite.bounds.size;
                float k = Mathf.Max(pv.x / Mathf.Max(0.01f, bs.x), pv.y / Mathf.Max(0.01f, bs.y));
                b.transform.localScale = new Vector3(k, k, 1f);
                b.transform.position = new Vector3(0f, CameraFollowY, 20f);
            }
        if (cyberScans != null)
            for (int i = 0; i < cyberScans.Length; i++)
            {
                var s = cyberScans[i];
                if (s == null) continue;
                bool shop = i == 1;
                Vector2 v = shop ? sv : pv;
                Vector2 ss = s.sprite.bounds.size;
                s.transform.localScale = new Vector3(v.x / ss.x, v.y / ss.y, 1f);
                s.transform.position = new Vector3(0f, shop ? 0f : CameraFollowY, 0f);
                s.color = new Color(1f, 1f, 1f, Mathf.Clamp01(cyberScanAlpha));
            }
        if (cyberIdle != null)
            foreach (var g in cyberIdle)
                if (g != null) StepGlitch(g, Random.value < cyberIdleGlitch ? 0.35f : 0f, Time.time);
    }

    // ネオンの街の絵(上: 暗い紫の空と、ピンクと水色に光るビル / 下: 光る床の格子)
    static Sprite GetCyberBackSprite()
    {
        if (cyberBackSprite != null) return cyberBackSprite;
        const int w = 270, h = 480;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        var rnd = new System.Random(2077);
        Color top = new Color(0.05f, 0.02f, 0.12f), mid = new Color(0.25f, 0.05f, 0.3f);
        Color pink = new Color(1f, 0.15f, 0.75f), cyan = new Color(0.1f, 0.95f, 1f);
        int horizon = (int)(h * 0.38f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = y >= horizon ? Color.Lerp(mid, top, (y - horizon) / (float)(h - horizon)) : new Color(0.03f, 0.01f, 0.06f);
        // ビル(地平線の上に、高さのちがう暗い四角。窓がピンク・水色に光る)
        for (int bx = 0; bx < w; )
        {
            int bw = 14 + rnd.Next(26), bh = 40 + rnd.Next(150);
            Color win = rnd.Next(2) == 0 ? pink : cyan;
            for (int y = horizon; y < horizon + bh && y < h; y++)
                for (int x = bx; x < bx + bw && x < w; x++)
                {
                    bool window = (x - bx) % 6 >= 2 && (x - bx) % 6 <= 3 && (y - horizon) % 8 >= 3 && (y - horizon) % 8 <= 5 && rnd.Next(3) > 0;
                    bool edge = y == horizon + bh - 1 || x == bx || x == bx + bw - 1;
                    px[y * w + x] = window ? win * 0.9f : edge ? win * 0.5f : new Color(0.06f, 0.03f, 0.1f);
                }
            bx += bw + rnd.Next(4);
        }
        // 床の格子(手前ほど広がる線)
        for (int y = 0; y < horizon; y++)
        {
            float depth = (float)(horizon - y) / horizon;   // 0 地平線 〜 1 手前
            // 横の線(手前ほど間が広い)
            float row = Mathf.Pow(depth, 0.5f) * 14f;
            bool hLine = Mathf.Abs(row - Mathf.Round(row)) < 0.06f + depth * 0.04f;
            for (int x = 0; x < w; x++)
            {
                float u = (x - w / 2f) / (w / 2f);
                float col = u / Mathf.Max(0.05f, 0.15f + depth) * 4f;   // 縦の線(地平線の真ん中に集まる)
                bool vLine = Mathf.Abs(col - Mathf.Round(col)) < 0.05f + depth * 0.05f;
                if (hLine || vLine) px[y * w + x] = Color.Lerp(pink, cyan, depth) * (0.6f + 0.4f * depth);
            }
        }
        // 地平線の光
        for (int x = 0; x < w; x++) { px[horizon * w + x] = pink; if (horizon + 1 < h) px[(horizon + 1) * w + x] = pink * 0.7f; }
        for (int i = 0; i < px.Length; i++) px[i].a = 1f;
        tex.SetPixels(px);
        tex.Apply();
        cyberBackSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
        return cyberBackSprite;
    }
}
