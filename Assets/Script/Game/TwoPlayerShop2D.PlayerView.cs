using UnityEngine;

public enum ScreenRotation
{
    None,    // 回さない
    Right,   // モニターを右回り(時計回り)に90度回して置いたとき
    Left,    // モニターを左回りに90度回して置いたとき
}

// 1P/2Pの画面(レジ袋の画面)の大きさと配置(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 「必ず映す範囲」(横 playerViewWidth、縦は playerViewAspect から計算)を決めておき、
//  - カメラは、どんな形・解像度の画面でもこの範囲が全部映るように自動でズームする(余りは背景の色)
//  - 床・壁・袋・落とす高さは、この範囲に対する割合で置く
// なので、画面の形を変えても、商品・手・足・袋・文字が全部そろって大きさが変わり、崩れない
public partial class TwoPlayerShop2D
{
    // 必ず映す範囲(ゲーム内の横幅・縦幅)
    Vector2 PlayerViewSize()
    {
        float w = Mathf.Max(1f, playerViewWidth);
        return new Vector2(w, w / Mathf.Clamp(PlayerViewAspectNow(), 0.1f, 4f));
    }

    // 範囲の横:縦。fillPlayerScreen なら、1Pの画面に実際に映る形(回しているときは縦横を入れ替える)
    float PlayerViewAspectNow()
    {
        if (!fillPlayerScreen || p1 == null || switcher == null || p1.screen < 0 || p1.screen >= switcher.screens.Length)
            return playerViewAspect;
        var cam = switcher.screens[p1.screen].camera;
        if (cam == null || cam.pixelHeight <= 0) return playerViewAspect;
        float aspect = cam.aspect;
        return RotationOf(p1) == ScreenRotation.None ? aspect : 1f / Mathf.Max(0.01f, aspect);
    }

    // 割合の設定から、床・壁・袋・落とす高さを計算する(値が変わったら、床や袋はそれぞれの処理で動く)
    void ApplyPlayerLayout()
    {
        Vector2 view = PlayerViewSize();
        float bottom = -view.y / 2f;
        floorY = bottom + view.y * floorFromBottom;
        wallX = view.x / 2f * deskWidthRatio;   // 机(床)の端 = 左右の壁
        bagWidth = Mathf.Min(view.x * bagWidthRatio, wallX * 2f);   // 机からはみ出さない
        bagHeight = view.y * bagHeightRatio;
        bagX = Mathf.Clamp(view.x * bagXRatio, -wallX + bagWidth / 2f, wallX - bagWidth / 2f);   // はみ出さない
        dropHeight = bottom + view.y * dropHeightRatio;
    }

    // 1P/2Pのカメラを、必ず映す範囲が全部入るようにズームする
    // (DisplayTestSwitcher が決めた表示の場所・形はそのまま。ズームだけ上書きする)
    // 回して描くときは、画面の縦がゲーム内の横になる
    void FitPlayerCameras()
    {
        Vector2 view = PlayerViewSize();
        foreach (var p in new[] { p1, p2 })
        {
            if (p == null || p.screen < 0 || p.screen >= switcher.screens.Length) continue;
            var cam = switcher.screens[p.screen].camera;
            if (cam == null || !cam.orthographic) continue;

            // 回す(モニターを右回りに置いたら、映像は左回りに回して描く)
            var rotation = RotationOf(p);
            float angle = rotation == ScreenRotation.Right ? -90f : rotation == ScreenRotation.Left ? 90f : 0f;
            cam.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            // 袋持ち上げターンから: カメラを持ち上げた袋(持ち手)と一緒に上へ動かす(机が映りすぎないように。TwoPlayerShop2D.Lift.cs)
            if (liftStarted && p.screenShakeRoutine == null)
                cam.transform.position = new Vector3(cam.transform.position.x, CameraFollowY, cam.transform.position.z);

            if (!autoFitPlayerScreens) continue;   // OFF: ズームは DisplayTestSwitcher に任せる
            float aspect = Mathf.Max(0.01f, cam.aspect);   // 今の画面(表示の場所)の横:縦
            cam.orthographicSize = rotation == ScreenRotation.None
                ? Mathf.Max(view.y / 2f, view.x / 2f / aspect)
                : Mathf.Max(view.x / 2f, view.y / 2f / aspect);
        }
    }
}
