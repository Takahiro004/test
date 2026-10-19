using UnityEngine;

public enum MonitorMode
{
    [InspectorName("縦型モニター(本番)")] Portrait,
    [InspectorName("横型モニター(縦と同じ見た目)")] LandscapeSameLook,
    [InspectorName("横型モニター(横いっぱい)")] LandscapeFill,
}

// 縦の配置・横の配置で変える値のまとまり(Inspector で中を開いて調整する)
[System.Serializable]
public class LayoutProfile
{
    [Tooltip("1P/2Pの画面に映す横幅(ゲーム内の長さ)。小さいほど全部大きく映る。縦 10 と横 17.8 で、商品・手・文字の見た目の大きさがそろう")]
    public float playerViewWidth = 10f;
    [Tooltip("床の高さ(範囲の下の端から、範囲の高さの何割上か)")]
    [Range(0f, 0.5f)] public float floorFromBottom = 0.04f;
    [Tooltip("机(床)の幅(範囲の横幅の何割か。1で端から端まで)")]
    [Range(0.2f, 1f)] public float deskWidthRatio = 1f;
    [Tooltip("机(床)の厚み(見た目だけ)")]
    public float deskThickness = 0.5f;
    [Tooltip("商品を持って横移動する高さ(範囲の下の端から、範囲の高さの何割上か)")]
    [Range(0f, 1.2f)] public float dropHeightRatio = 0.85f;
    [Tooltip("レジ袋の幅(範囲の横幅の何割か)")]
    [Range(0.1f, 1f)] public float bagWidthRatio = 0.9f;
    [Tooltip("レジ袋の高さ(範囲の高さの何割か)")]
    [Range(0.05f, 1f)] public float bagHeightRatio = 0.65f;
    [Tooltip("レジ袋の横の位置(-0.5 左の端 / 0 真ん中 / 0.5 右の端)")]
    [Range(-0.5f, 0.5f)] public float bagXRatio = 0f;
    [Tooltip("商品の大きさ(縦横の長いほうの長さ)。新しく出た商品から変わる")]
    public float productSize = 1.4f;
    [Tooltip("カーソル(手)の大きさ(縦横の長いほうの長さ)。再生前に決める")]
    public float cursorScale = 1.2f;
    [Tooltip("合計金額・「+¥」「LB: キック」などの文字の大きさ")]
    public float textScale = 2f;

    public static LayoutProfile PortraitDefault() => new LayoutProfile();

    // 横の画面(16:9)は高さが縦の画面の 9/16 なので、袋は横に細め・縦は少し高めの割合にする
    public static LayoutProfile LandscapeDefault() => new LayoutProfile
    {
        playerViewWidth = 17.8f,
        floorFromBottom = 0.06f,
        dropHeightRatio = 0.85f,
        bagWidthRatio = 0.5f,
        bagHeightRatio = 0.6f,
    };
}

// 縦型モニター用 / 横型モニター用の切り替え(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// monitorMode で選んだ配置を、袋・机・商品・カーソル・文字・棚の向き・画面いっぱいかどうか に入れる(毎フレーム)
public partial class TwoPlayerShop2D
{
    bool IsLandscapeMonitor => monitorMode != MonitorMode.Portrait;

    void ApplyMonitorMode()
    {
        var layout = monitorMode == MonitorMode.LandscapeFill ? landscapeLayout : portraitLayout;
        if (layout == null) return;

        playerViewWidth = layout.playerViewWidth;
        floorFromBottom = layout.floorFromBottom;
        deskWidthRatio = layout.deskWidthRatio;
        deskThickness = layout.deskThickness;
        dropHeightRatio = layout.dropHeightRatio;
        bagWidthRatio = layout.bagWidthRatio;
        bagHeightRatio = layout.bagHeightRatio;
        bagXRatio = layout.bagXRatio;
        productSize = layout.productSize;
        cursorScale = layout.cursorScale;
        textScale = layout.textScale;

        switch (monitorMode)
        {
            case MonitorMode.Portrait:
                // 縦のモニターいっぱい。棚は縦(倒しているなら回す)
                fillPlayerScreen = true;
                shopOrientation = shopScreenRotation == ScreenRotation.Right ? ShopOrientation.PortraitRotatedRight
                                : shopScreenRotation == ScreenRotation.Left ? ShopOrientation.PortraitRotatedLeft
                                : ShopOrientation.Portrait;
                break;
            case MonitorMode.LandscapeSameLook:
                // 横のモニターの真ん中に、本番と同じ縦長(Player View Aspect)の画面。左右は空ける
                fillPlayerScreen = false;
                shopOrientation = ShopOrientation.Portrait;
                break;
            case MonitorMode.LandscapeFill:
                // 横のモニターいっぱいに並べ直す
                fillPlayerScreen = true;
                shopOrientation = ShopOrientation.Landscape;
                break;
        }
    }

    // 1P/2P の画面を回すか。横型モニターのときは回さない(倒しているのは縦型モニターのときだけ)
    ScreenRotation RotationOf(Player p)
    {
        if (IsLandscapeMonitor) return ScreenRotation.None;
        return p == p1 ? p1ScreenRotation : p2ScreenRotation;
    }
}
