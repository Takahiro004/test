using UnityEngine;

// 操作中の画面の枠(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「操作中の画面の枠」と「機能のON/OFF」→ Show Own Screen Frame)
// プレイヤーが商品を持って自分の画面(レジ袋の画面)を操作しているあいだ、その画面のふちにプレイヤーの色の枠を出す。
// どの画面を操作しているかが、ひと目で分かるように。棚に戻ったら消える。
//  - 枠はカメラの子にするので、画面が揺れても画面のふちに付いたまま。映っている範囲いっぱいに出す
// 棚の画面の枠は、棚の画面を操作しているプレイヤーの色:
//  - 1人だけ棚にいるとき: その人の色で、ぐるっと全部を囲む(袋の画面の枠と同じ)
//  - 2人とも棚にいるとき: 縦に半分ずつ、それぞれの色(初期は 左 = 1P の赤・右 = 2P の青。Shop Frame Left Is P1 で入れ替え)
//  - 2人とも自分の画面に行っているとき: 出さない
public partial class TwoPlayerShop2D
{
    readonly Transform[][] ownFrames = new Transform[2][];   // [プレイヤー][上・下・左・右]
    Transform[] shopFrame;                                   // 棚の画面の枠: 上の左・上の右・下の左・下の右・左・右

    // LateUpdate から毎フレーム。棚の画面の枠(棚の画面は揺れないので、ゲームの座標の真ん中に置く)
    void UpdateShopScreenFrame()
    {
        if (shopFrame == null)
        {
            if (!showShopScreenFrame) return;
            shopFrame = new Transform[6];
            var root = new GameObject("ShopFrame").transform;
            for (int k = 0; k < 6; k++)
                shopFrame[k] = MakeBagPart($"ShopFrame_{k}", root, layerShop, Color.white, 2800, false);   // 商品・カーソルより手前
        }
        if (!showShopScreenFrame || p1 == null) { foreach (var b in shopFrame) b.gameObject.SetActive(false); return; }

        // 棚にいる人の色: 2人なら左右半分ずつ、1人ならその人の色で全部、0人なら出さない
        var leftPlayer = shopFrameLeftIsP1 ? p1 : p2;
        var rightPlayer = shopFrameLeftIsP1 ? p2 : p1;
        Player leftOwner = leftPlayer.inShop ? leftPlayer : rightPlayer.inShop ? rightPlayer : null;
        Player rightOwner = rightPlayer.inShop ? rightPlayer : leftPlayer.inShop ? leftPlayer : null;
        foreach (var b in shopFrame) b.gameObject.SetActive(leftOwner != null);
        if (leftOwner == null) return;

        Vector2 vis = ShopVisibleSize();   // 映っている範囲(縦に回したモニターでも、見る人から見た縦横)
        float w = vis.x, h = vis.y, t = Mathf.Max(0.01f, shopScreenFrameWidth);
        Color left = leftOwner.color, right = rightOwner.color;
        left.a = right.a = 1f;
        SetBar(shopFrame[0], -w / 4f, h / 2f - t / 2f, w / 2f, t, left);    // 上の左半分
        SetBar(shopFrame[1], w / 4f, h / 2f - t / 2f, w / 2f, t, right);    // 上の右半分
        SetBar(shopFrame[2], -w / 4f, -h / 2f + t / 2f, w / 2f, t, left);   // 下の左半分
        SetBar(shopFrame[3], w / 4f, -h / 2f + t / 2f, w / 2f, t, right);   // 下の右半分
        SetBar(shopFrame[4], -w / 2f + t / 2f, 0f, t, h, left);             // 左
        SetBar(shopFrame[5], w / 2f - t / 2f, 0f, t, h, right);             // 右

        static void SetBar(Transform tr, float x, float y, float width, float height, Color color)
        {
            tr.position = new Vector3(x, y, 0f);
            tr.localScale = new Vector3(width, height, 1f);
            tr.GetComponent<SpriteRenderer>().color = color;
        }
    }

    // LateUpdate から毎フレーム(カメラのズームが決まったあと)
    void UpdateOwnScreenFrames()
    {
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            if (p == null || switcher == null || p.screen < 0 || p.screen >= switcher.screens.Length) continue;
            var cam = switcher.screens[p.screen].camera;
            if (cam == null) continue;

            bool show = showOwnScreenFrame && !p.inShop && p.held != null && !(IsTimeUp && showResultOnTimeUp);
            var bars = ownFrames[i];
            if (bars == null || bars[0] == null || bars[0].parent != cam.transform)
            {
                if (!show) continue;
                if (bars != null) foreach (var b in bars) if (b != null) Destroy(b.gameObject);
                bars = new Transform[4];
                for (int k = 0; k < 4; k++)
                {
                    bars[k] = MakeBagPart($"OwnFrame_{p.name}_{k}", cam.transform, p.layer, p.color, 2800, false);   // 商品・カーソルより手前
                    bars[k].localRotation = Quaternion.identity;
                }
                ownFrames[i] = bars;
            }
            foreach (var b in bars) b.gameObject.SetActive(show);
            if (!show) continue;

            // カメラに映っている範囲(カメラから見た長さ)のふちに、太さ ownScreenFrameWidth の枠を置く
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            float t = Mathf.Max(0.01f, ownScreenFrameWidth);
            var col = new Color(p.color.r, p.color.g, p.color.b, 1f);
            Place(bars[0], 0f, h / 2f - t / 2f, w, t);    // 上
            Place(bars[1], 0f, -h / 2f + t / 2f, w, t);   // 下
            Place(bars[2], -w / 2f + t / 2f, 0f, t, h);   // 左
            Place(bars[3], w / 2f - t / 2f, 0f, t, h);    // 右
            foreach (var b in bars) b.GetComponent<SpriteRenderer>().color = col;
        }

        static void Place(Transform tr, float x, float y, float width, float height)
        {
            tr.localPosition = new Vector3(x, y, 1f);   // カメラの少し前
            tr.localScale = new Vector3(width, height, 1f);
        }
    }
}
