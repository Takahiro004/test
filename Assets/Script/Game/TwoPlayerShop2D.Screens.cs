using UnityEngine;

// 1P/棚/2P をどのカメラ(= ディスプレイ)に映すか(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// Inspector の Screen P1 / Screen Shop / Screen P2(DisplayTestSwitcher の Screens の番号)で決める。
// 再生中に入れ替えても、映すもの(レイヤー)・背景の色・カメラの回転や範囲を付け替える。
// 物はレイヤーで画面を分けているので、入れ替わるのは「どのカメラが、どの役割のレイヤーを映すか」だけ
public partial class TwoPlayerShop2D
{
    int appliedScreenP1 = -1, appliedScreenShop = -1, appliedScreenP2 = -1;

    void ApplyScreenAssignment()
    {
        // 前の棚のカメラは、縦のために縮めた範囲・回転を元に戻す(次の棚のカメラで改めて合わせる)
        if (appliedScreenShop >= 0 && appliedScreenShop < switcher.screens.Length)
        {
            var oldShop = switcher.screens[appliedScreenShop].camera;
            if (oldShop != null && shopCameraChanged) oldShop.rect = shopBaseRect;
        }
        shopCameraChanged = false;
        shopAppliedRect = new Rect(-1f, -1f, 0f, 0f);   // 次の棚のカメラの範囲を、元の範囲として覚え直す

        appliedScreenP1 = screenP1;
        appliedScreenShop = screenShop;
        appliedScreenP2 = screenP2;

        // 番号のまちがいを知らせる(同じ番号が2つ / 範囲の外)
        int n = switcher.screens.Length;
        if (screenP1 == screenShop || screenP1 == screenP2 || screenShop == screenP2)
            Debug.LogWarning($"[Shop2D] 画面の割り当てで同じ番号が使われています(1P {screenP1} / 棚 {screenShop} / 2P {screenP2})。3つは別々の番号にしてください");
        foreach (int s in new[] { screenP1, screenShop, screenP2 })
            if (s < 0 || s >= n)
                Debug.LogWarning($"[Shop2D] 画面の割り当ての番号 {s} は、DisplayTestSwitcher の Screens(0〜{n - 1})にありません");

        for (int i = 0; i < n; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;

            // 映すもの: その番号に割り当てた役割のレイヤーだけ
            int mask = 1 << 0;   // Default は全画面
            if (i == screenP1) mask |= 1 << layerP1;
            if (i == screenShop) mask |= 1 << layerShop;
            if (i == screenP2) mask |= 1 << layerP2;
            cam.cullingMask = mask;

            // 背景の色は役割について回る
            if (i == screenP1) cam.backgroundColor = p1BackColor;
            else if (i == screenShop) cam.backgroundColor = shopBackColor;
            else if (i == screenP2) cam.backgroundColor = p2BackColor;

            // 回転と位置はいったん元に戻す(1P/2P・棚のカメラは、毎フレームの処理で改めて回す)
            cam.transform.rotation = Quaternion.identity;
            cam.transform.position = new Vector3(0f, 0f, -10f);
        }

        // プレイヤーが使うカメラを付け替える(キックの画面の揺れは、新しいカメラの位置から)
        if (p1 != null) { p1.screen = screenP1; p1.hasCameraBase = false; }
        if (p2 != null) { p2.screen = screenP2; p2.hasCameraBase = false; }
    }
}
