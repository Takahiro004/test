using UnityEngine;

public enum ShopOrientation
{
    Landscape,            // 横(今まで)
    Portrait,             // 縦。横のモニターでは真ん中に縦長で表示(左右は黒)
    PortraitRotatedRight, // 縦。Windows は横のまま、モニターを右回り(時計回り)に90度回したとき
    PortraitRotatedLeft,  // 縦。Windows は横のまま、モニターを左回りに90度回したとき
}

// 棚の画面(画面2)の向き・カメラ(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 縦のときは、DisplayTestSwitcher が決めた表示範囲の中に、縦長の画面を収める(LateUpdate で毎フレーム)
public partial class TwoPlayerShop2D
{
    ShopOrientation appliedOrientation;
    float appliedPortraitAspect, appliedPortraitWidth, appliedTopSpace;
    int appliedColumns;
    Rect shopBaseRect, shopAppliedRect;   // DisplayTestSwitcher が決めた範囲 / こちらで縮めた範囲
    TextMesh timerPlaceholder;
    bool shopCameraChanged;               // 縦のためにカメラを変えたか(横に戻すときに元へ戻す)
    Camera shopBackCamera;                // 縦のとき、左右(上下)の余りを黒く塗るカメラ

    bool IsPortrait => shopOrientation != ShopOrientation.Landscape;

    // 棚の画面に映る範囲(ワールド座標の横幅・縦幅)
    Vector2 ShopViewSize()
    {
        if (!IsPortrait) return new Vector2(17.8f, 10f);   // 横: 今までの 16:9(Orthographic Size 5)
        float w = Mathf.Max(1f, portraitViewWidth);
        return new Vector2(w, w / Mathf.Clamp(portraitAspect, 0.1f, 1f));
    }

    // 棚の画面に実際に映っている範囲(ゲーム内の横幅・縦幅)。カメラのズームと画面の形から計算する
    // (ShopViewSize は「並べるための範囲」。画面の形によっては、まわりにもう少し映っている)
    Vector2 ShopVisibleSize()
    {
        if (switcher == null || screenShop < 0 || screenShop >= switcher.screens.Length) return ShopViewSize();
        var cam = switcher.screens[screenShop].camera;
        if (cam == null || !cam.orthographic) return ShopViewSize();
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;   // 画面の縦・横
        bool rotated = shopOrientation == ShopOrientation.PortraitRotatedRight || shopOrientation == ShopOrientation.PortraitRotatedLeft;
        return rotated ? new Vector2(h, w) : new Vector2(w, h);    // 回しているときは縦横が入れ替わる
    }

    // 棚の並べ方(段と列・商品を置く場所)と棚の見た目は TwoPlayerShop2D.Shelf.cs

    Vector2 ClampToShopView(Vector2 v)
    {
        if (!IsPortrait) return ClampToView(v);
        Vector2 half = ShopViewSize() / 2f - new Vector2(0.3f, 0.3f);
        return new Vector2(Mathf.Clamp(v.x, -half.x, half.x), Mathf.Clamp(v.y, -half.y, half.y));
    }

    // 掴み合いのゲージの置き場所を決める(root は高さ baseY のゲージを、原点に置いた状態で作ってある)
    //  - 縦のときは、画面の横幅に収まるように縮める
    //  - clashGaugeCentered なら、ゲージ全体(背景の枠)の真ん中を画面の真ん中に合わせる
    void FitToShopView(Transform root, float baseY)
    {
        Vector2 view = ShopViewSize();
        float scale = IsPortrait ? Mathf.Min(1f, view.x / 18f) : 1f;
        const float panelCenter = 0.7f;   // 背景の枠の中心は、ゲージより 0.7 上(StartClash と同じ)

        float y;   // ゲージの高さ
        if (clashGaugeCentered) y = -panelCenter * scale;            // 枠の真ん中が画面の真ん中(0)に来る
        else if (IsPortrait) y = -view.y / 2f + 1.6f * scale;        // 縦: 画面の下のほう
        else y = baseY;                                              // 横: 今までどおり

        root.localScale = new Vector3(scale, scale, 1f);
        root.position = new Vector3(0f, y - baseY * scale, 0f);
    }

    // 向きや配置の設定を反映する(棚の商品・カーソルを並べ直す)
    void ApplyShopLayout()
    {
        appliedOrientation = shopOrientation;
        appliedPortraitAspect = portraitAspect;
        appliedPortraitWidth = portraitViewWidth;
        appliedColumns = portraitColumns;
        appliedTopSpace = portraitTopSpace;

        if (slots.Count != TotalSlotCount) RebuildShelfProducts();   // (TwoPlayerShop2D.Shelf.cs)
        ResetSectionOffsets();   // 棚が回ったかどうかの判定を、今の位置からやり直す   // 段・列で商品の数が変わった(TwoPlayerShop2D.Shelf.cs)
        for (int i = 0; i < slots.Count; i++) slots[i].pos = ShelfSlotPosition(i);
        foreach (var kv in slotOf)   // 棚に置いてある商品だけ動かす
            if (kv.Key != null) PutOnShelf(kv.Key, kv.Value);
        BuildShelfVisual();

        foreach (var p in new[] { p1, p2 })
            if (p != null) p.shopPos = ClampToShopView(p.shopPos);

        // タイマー予定地の目印
        if (timerPlaceholder == null)
            timerPlaceholder = MakeText("TimerPlaceholder", null, layerShop, "TIMER", new Color(1f, 1f, 1f, 0.35f), 5);
        Vector2 view = ShopViewSize();
        timerPlaceholder.gameObject.SetActive(IsPortrait && showTimerPlaceholder && !showGameTimer && portraitTopSpace > 0f);
        timerPlaceholder.transform.position = new Vector3(0f, view.y / 2f - view.y * portraitTopSpace / 2f, 0f);
        SetTextSize(timerPlaceholder, 0.08f * Mathf.Max(1f, view.x / 8f));
    }

    void LateUpdate()
    {
        if (switcher == null || p1 == null) return;
        UpdateShelfIfChanged();   // 段・列・棚の見た目を再生中に変えた
        UpdateShelfScroll();      // 棚をゆっくり左から右へ流す
        if (screenP1 != appliedScreenP1 || screenShop != appliedScreenShop || screenP2 != appliedScreenP2)
            ApplyScreenAssignment();   // Inspector で画面の割り当てを入れ替えた
        FitPlayerCameras();
        UpdateOwnScreenFrames();   // 商品を持って自分の画面を操作中なら、その画面にプレイヤーの色の枠(TwoPlayerShop2D.Frame.cs)
        UpdateShopScreenFrame();   // 棚の画面の枠(左右半分ずつプレイヤーの色)

        if (shopOrientation != appliedOrientation || portraitAspect != appliedPortraitAspect ||
            portraitViewWidth != appliedPortraitWidth || portraitColumns != appliedColumns ||
            portraitTopSpace != appliedTopSpace) ApplyShopLayout();
        if (timerPlaceholder != null)
            timerPlaceholder.gameObject.SetActive(IsPortrait && showTimerPlaceholder && !showGameTimer && portraitTopSpace > 0f);
        UpdateGameTimer();
        UpdateBagCreak();         // 袋が重くなったら「ミシ…」(TwoPlayerShop2D.Creak.cs)

        if (screenShop < 0 || screenShop >= switcher.screens.Length) return;
        var cam = switcher.screens[screenShop].camera;
        if (cam == null) return;
        ApplyShopCamera(cam);
        ApplyShopBackCamera(cam);
    }

    // 縦のとき、棚のカメラは真ん中しか描かないので、左右の余りには前の映像が残ってしまう
    // (棚が2つ重なって見える)。元の範囲全体を、先に黒で塗るカメラを置いて消す
    void ApplyShopBackCamera(Camera shopCam)
    {
        if (shopBackCamera == null)
        {
            var go = new GameObject("ShopBackCamera");
            shopBackCamera = go.AddComponent<Camera>();
            shopBackCamera.orthographic = true;
            shopBackCamera.cullingMask = 0;                        // 何も映さない(黒で塗るだけ)
            shopBackCamera.clearFlags = CameraClearFlags.SolidColor;
            shopBackCamera.backgroundColor = Color.black;
            go.transform.position = new Vector3(0f, 0f, -10f);
        }
        shopBackCamera.enabled = IsPortrait && shopCam.enabled;
        shopBackCamera.targetDisplay = shopCam.targetDisplay;
        shopBackCamera.rect = shopBaseRect;
        shopBackCamera.depth = shopCam.depth - 1f;                // 棚のカメラより先に描く
    }

    // 棚のカメラを向きに合わせる。DisplayTestSwitcher が決めた範囲の中に、縦長(または回した縦長)の画面を収める
    void ApplyShopCamera(Camera cam)
    {
        // DisplayTestSwitcher が範囲を変えたら(分割表示・本番切替)、それを元の範囲として覚える
        if (cam.rect != shopAppliedRect) shopBaseRect = cam.rect;

        if (!IsPortrait)
        {
            // 縦から戻したときは、範囲と向きを元に戻す
            if (shopCameraChanged)
            {
                cam.rect = shopBaseRect;
                cam.transform.rotation = Quaternion.identity;
                shopCameraChanged = false;
            }
            shopAppliedRect = cam.rect;
            // 横の配置(16:9)が、どんな形の画面でも全部映るようにズームする(縦長の画面なら上下に余り)
            Vector2 land = ShopViewSize();
            if (autoFitShopScreen)
                cam.orthographicSize = Mathf.Max(land.y / 2f, land.x / 2f / Mathf.Max(0.01f, cam.aspect));
            return;
        }
        shopCameraChanged = true;

        // 元の範囲の、画面上での横:縦の比率
        float displayW = cam.pixelWidth / Mathf.Max(0.0001f, cam.rect.width);
        float displayH = cam.pixelHeight / Mathf.Max(0.0001f, cam.rect.height);
        float baseAspect = (displayW * shopBaseRect.width) / Mathf.Max(1f, displayH * shopBaseRect.height);

        bool rotated = shopOrientation != ShopOrientation.Portrait;
        Vector2 view = ShopViewSize();
        // 画面上に映したい比率(回すときは縦横が入れ替わる)
        float wantAspect = rotated ? view.y / view.x : view.x / view.y;

        // 比率が合うように、元の範囲の真ん中を切り出す(余りは黒)
        Rect r = shopBaseRect;
        if (baseAspect > wantAspect)
        {
            float w = r.width * wantAspect / baseAspect;
            r.x += (r.width - w) / 2f;
            r.width = w;
        }
        else
        {
            float h = r.height * baseAspect / wantAspect;
            r.y += (r.height - h) / 2f;
            r.height = h;
        }
        cam.rect = r;
        shopAppliedRect = cam.rect;

        // 回すときは、画面の縦がワールドの横になる
        cam.orthographicSize = (rotated ? view.x : view.y) / 2f;
        // モニターを右回りに回したら、映像は左回りに回して描く(逆さに見えたらもう一方を選ぶ)
        float angle = shopOrientation == ShopOrientation.PortraitRotatedRight ? -90f
                    : shopOrientation == ShopOrientation.PortraitRotatedLeft ? 90f : 0f;
        cam.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
