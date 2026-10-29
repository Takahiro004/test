using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイムアップのあと(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 操作と点数を止めて、
//  -. 袋を持ち上げる(bagLiftOnTimeUp。TwoPlayerShop2D.Lift.cs): こぼれた商品は消えて合計から引く
//  0. 結果発表(showResultBars): 棚の画面で、合計金額の棒グラフ(1P 赤・2P 青)が下から伸びる。上にレジ袋と金額
//     2本は同じ速さで伸び、少ない方が先に止まる。画面の上の端 = resultBarScreenAmount(1万)。
//     棒の先が画面の resultBarFollowHeight の高さまで来たら、カメラが棒の先を追いかけて上へ進む(低い方の棒は画面の下へ流れて見えなくなる)
//     → 伸び終わったら、カメラが引いて全体を見せる(棒も文字もレジ袋も全部一緒に小さくなる)
//     → resultBarHoldTime 秒たったら、棒グラフを残したまま勝ち負けの画面へ
//  - 1P/2P それぞれの画面: 合計金額の多い方に「あなたの勝ち！」、少ない方に「あなたの負け！」、同じなら「引き分け！」を大きく出す
//  - 棚の画面: 棒グラフを出していたら、それを残して 1P/2P の名前の下に「Aでもう一度」。出していなければ左右に分けて(左 1P / 右 2P)、1P/2P・合計金額・「Aでもう一度」を出す
// resultRestartDelay 秒たったら、どちらかが A を押すとシーンを読み込み直して最初から
public partial class TwoPlayerShop2D
{
    // シーンを読み込み直しても、コントローラーの 1P/2P の割り当ては残す
    static Gamepad keptPad1, keptPad2;

    GameObject resultRoot;
    TextMesh resultHint1, resultHint2;
    float resultTime;

    // 結果発表(棒グラフ)
    // 棒・レジ袋・金額・名前・目盛りは「グラフ」(barChart)の中に、本当の高さ(1万 = 画面の高さ)で作り、
    // グラフ全体を動かす・縮めることで「カメラが棒を追いかけて上へ進む」「カメラが引く」ように見せる
    GameObject barsRoot;
    Transform barChart;
    float barsTime;
    bool barsDone;
    readonly Transform[] bars = new Transform[2];
    readonly Transform[] barBags = new Transform[2];
    readonly TextMesh[] barTexts = new TextMesh[2];
    float barBaseY, barMaxHeight, barWidth, barTopEdge;
    float barMoney;                 // 今伸びている金額(2本共通)
    float overviewTime = -1f;       // ズームアウトの経過(-1 = まだ)
    float zoomFromBottom;           // ズームアウトを始めたときの、画面の下の端の金額
    float barBagSize;               // レジ袋の高さ(引く前の大きさ)
    float barItemScaleY = 1f;       // レジ袋・文字を、グラフの縦の縮みに対して何倍にしているか(小さくなりすぎないように)
    readonly Transform[] barNames = new Transform[2];
    readonly System.Collections.Generic.List<Transform> gridLines = new System.Collections.Generic.List<Transform>();
    readonly System.Collections.Generic.List<Transform> gridLabels = new System.Collections.Generic.List<Transform>();
    float barViewWidth, barTextSize, barNameY;

    bool heldCleared;

    // タイムアップのとき、1P/2P の画面で落とす前(持ち運び中)の商品と、その画面のカーソルを消す
    void ClearHeldOnTimeUp()
    {
        if (heldCleared) return;
        heldCleared = true;
        foreach (var p in new[] { p1, p2 })
        {
            if (p.held != null)
            {
                var go = p.held;
                foreach (var c in go.GetComponentsInChildren<Collider2D>()) products.Remove(c);
                originalColors.Remove(go);
                priceOf.Remove(go);
                infoOf.Remove(go);
                Destroy(go);
                p.held = null;
            }
            p.inShop = true;
            p.cursorOwn.gameObject.SetActive(false);
            p.cursorShop.gameObject.SetActive(false);   // 棚の画面の手も消す(持ち上げ・結果発表の画面に手が残らないように)
        }
    }

    // Start で呼ぶ: 前のゲームの割り当てを戻す
    void RestorePads()
    {
        if (keptPad1 != null && keptPad1.added) pad1 = keptPad1;
        if (keptPad2 != null && keptPad2.added) pad2 = keptPad2;
    }

    void UpdateResult()
    {
        ClearHeldOnTimeUp();   // 落とす前の商品とカーソルを消す(最初の1回だけ)

        // 袋を持ち上げる(こぼれた商品は合計から引く。TwoPlayerShop2D.Lift.cs)
        if (UpdateBagLift()) return;

        // 0. 結果発表(棒グラフ)
        if (showResultBars && !barsDone)
        {
            if (barsRoot == null) ShowResultBars();
            UpdateResultBars();
            return;
        }

        if (resultRoot == null) ShowResult();

        resultTime += Time.deltaTime;
        bool canRestart = resultTime >= resultRestartDelay;
        resultHint1.gameObject.SetActive(canRestart);
        resultHint2.gameObject.SetActive(canRestart);
        if (canRestart && AnyAPressed())
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void ShowResult()
    {
        // 途中の掴み合い・キックのゲージは消す
        if (clash != null) { Destroy(clash.root); clash = null; }
        EndKickGauge(p1);
        EndKickGauge(p2);

        int total1 = p1.total, total2 = p2.total;
        int result1 = total1 > total2 ? 1 : total1 < total2 ? -1 : 0;   // 1: 勝ち / -1: 負け / 0: 引き分け
        lastEvent = $"タイムアップ！ 1P ¥{total1} / 2P ¥{total2} → " +
                    (result1 > 0 ? "1Pの勝ち" : result1 < 0 ? "2Pの勝ち" : "引き分け");
        Debug.Log($"[Shop2D] {lastEvent}");

        resultRoot = new GameObject("Result");
        resultRoot.layer = layerShop;
        var t = resultRoot.transform;
        Vector2 view = ShopViewSize();
        float size = view.x / 8f;   // 文字の大きさの基準(縦の棚の幅 8 で 1)

        // 結果発表の棒グラフを出していたら、棚の画面はそのまま(棒・レジ袋・金額を残す)。
        // 1P/2P の画面に勝ち負けを出し、棚の画面には「Aでもう一度」だけ足す
        if (barsRoot != null)
        {
            ShowResultOnPlayerScreen(p1, result1);
            ShowResultOnPlayerScreen(p2, -result1);
            // 「Aでもう一度」は、棒の下の 1P/2P の名前よりさらに下に出す(画面の上のほうは他の UI と重なるため)
            Vector2 vis = ShopVisibleSize();
            float s2 = Mathf.Min(vis.x, vis.y) / 8f;
            var titleTr = barsRoot.transform.Find("Title");
            if (titleTr != null) titleTr.gameObject.SetActive(false);
            var hint = MakeText("Hint", t, layerShop, "Aでもう一度", new Color(0.2f, 0.2f, 0.25f), 3005);
            SetTextSize(hint, 0.07f * s2 * Mathf.Max(0.3f, resultBarTextScale));
            hint.anchor = TextAnchor.UpperCenter;   // 位置は文字の上の端(影より先に決める。影は作ったときの向きをまねる)
            AddTextShadow(hint);
            float nameBottom = Mathf.Min(barNames[0].GetComponent<MeshRenderer>().bounds.min.y,
                                         barNames[1].GetComponent<MeshRenderer>().bounds.min.y);
            float hintHeight = hint.characterSize * hint.fontSize * 0.1f;   // 文字の高さのおおよそ
            float hintY = Mathf.Max(nameBottom - 0.15f, -vis.y / 2f + 0.1f + hintHeight);   // 画面の下の端からははみ出さない
            hint.transform.position = new Vector3(0f, hintY, 0f);
            resultHint1 = resultHint2 = hint;
            resultTime = 0f;
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            int r = i == 0 ? result1 : -result1;
            float cx = (i == 0 ? -1f : 1f) * view.x / 4f;
            Color bg = r > 0 ? new Color(0.95f, 0.72f, 0.15f) : r < 0 ? new Color(0.22f, 0.22f, 0.28f) : new Color(0.35f, 0.38f, 0.5f);
            var panel = MakeBagPart($"Panel{i + 1}", t, layerShop, bg, 3000, false);
            SetRect(panel, cx, 0f, view.x / 2f, view.y + 2f);

            var player = i == 0 ? p1 : p2;
            var nameText = MakeText($"Name{i + 1}", t, layerShop, player.name, player.color, 3002);
            SetTextSize(nameText, 0.18f * size);
            nameText.transform.position = new Vector3(cx, view.y * 0.2f, 0f);

            var total = MakeText($"Total{i + 1}", t, layerShop, $"¥{(i == 0 ? total1 : total2):N0}", Color.white, 3002);
            SetTextSize(total, 0.11f * size);
            total.transform.position = new Vector3(cx, -view.y * 0.05f, 0f);

            ShowResultOnPlayerScreen(player, r);

            var hint = MakeText($"Hint{i + 1}", t, layerShop, "Aで\nもう一度", new Color(1f, 1f, 1f, 0.8f), 3002);
            SetTextSize(hint, 0.06f * size);
            hint.transform.position = new Vector3(cx, -view.y * 0.3f, 0f);
            if (i == 0) resultHint1 = hint; else resultHint2 = hint;
        }

        // 真ん中の仕切り
        var line = MakeBagPart("Divider", t, layerShop, Color.white, 3001, false);
        SetRect(line, 0f, 0f, 0.08f * size, view.y + 2f);
        resultTime = 0f;
    }

    // ---------- 結果発表(棒グラフ) ----------

    void ShowResultBars()
    {
        if (clash != null) { Destroy(clash.root); clash = null; }
        EndKickGauge(p1);
        EndKickGauge(p2);

        barsRoot = new GameObject("ResultBars");
        barsRoot.layer = layerShop;
        var t = barsRoot.transform;
        Vector2 view = ShopVisibleSize();   // 実際に映っている範囲(棒は画面の下の端から上の端まで使う)
        float size = Mathf.Min(view.x, view.y) / 8f;
        var ink = new Color(0.1f, 0.1f, 0.16f);     // 文字は濃い色
        var sub = new Color(0.3f, 0.32f, 0.4f);     // 目盛りなどの小さい文字
        float ts = Mathf.Max(0.3f, resultBarTextScale);

        // 画面に止まっているもの: 背景(上下で色が変わる)・見出し
        MakeResultBackground(t, view);
        var title = MakeText("Title", t, layerShop, "結果発表", ink, 3006);
        SetTextSize(title, 0.08f * size * ts);
        AddTextShadow(title);
        title.transform.position = new Vector3(0f, view.y * 0.46f, 0f);

        barBaseY = -view.y * 0.36f;  // 棒の伸び始める位置(下に 1P/2P の名前を出すぶん空ける)
        barTopEdge = view.y / 2f;    // 映っている画面の上の端
        barMaxHeight = barTopEdge - barBaseY;   // 伸び始める位置〜画面の上の端 = resultBarScreenAmount(1万)
        barNameY = -view.y * 0.05f;  // 名前は伸び始める位置の少し下(グラフの中の位置。カメラが進むと一緒に下へ流れる)
        barWidth = Mathf.Min(view.x, view.y) * 0.19f;

        // グラフ(動かす・縮めるもの)。グラフの中では、下の端が y=0、1万 = barMaxHeight
        barChart = new GameObject("Chart").transform;
        barChart.gameObject.layer = layerShop;
        barChart.SetParent(t);
        barChart.position = new Vector3(0f, barBaseY, 0f);
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * barWidth * 1.15f;   // 2本の間は棒1本ぶんより少し広く
            Color c = i == 0 ? new Color(0.93f, 0.11f, 0.14f) : new Color(0f, 0.64f, 0.9f);
            bars[i] = MakeBagPart($"Bar{i + 1}", barChart, layerShop, c, 3001, false);
            barBagSize = barWidth * 1.2f;
            barBags[i] = MakeResultBagIcon(barChart, $"Bag{i + 1}", barBagSize);
            barTexts[i] = MakeText($"Total{i + 1}", barChart, layerShop, "¥0", ink, 3004);
            SetTextSize(barTexts[i], 0.07f * size * ts);
            barTexts[i].color = i == 0 ? new Color(0.7f, 0.05f, 0.08f) : new Color(0f, 0.38f, 0.62f);   // 棒の色を濃くした色
            AddTextShadow(barTexts[i]);

            var nameText = MakeText($"Name{i + 1}", barChart, layerShop, i == 0 ? "1P" : "2P", ink, 3004);
            SetTextSize(nameText, 0.08f * size * ts);
            AddTextShadow(nameText);
            nameText.transform.localPosition = new Vector3(x, barNameY, 0f);   // 伸び始める位置の少し下
            barNames[i] = nameText.transform;
            bars[i].localPosition = new Vector3(x, 0f, 0f);
        }
        barViewWidth = view.x;
        barTextSize = size;
        gridLines.Clear();
        gridLabels.Clear();
        MakeBarGrid(view, size);

        barMoney = 0f;
        overviewTime = -1f;
        barsTime = 0f;
        lastEvent = "結果発表…";
    }

    void UpdateResultBars()
    {
        int[] totals = { Mathf.Max(0, p1.total), Mathf.Max(0, p2.total) };
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float maxTotal = Mathf.Max(totals[0], totals[1]);

        if (overviewTime < 0f)
        {
            // 伸びている途中: 最初はゆっくり、だんだん速くなる。2本は同じ速さ
            // 速さの基準は「1画面(下の端〜上の端)ぶんを resultBarGrowTime 秒」。これに Start Speed → End Speed を掛ける
            barsTime += Time.deltaTime;
            float a = Mathf.Clamp01(barsTime / Mathf.Max(0.05f, resultBarAccelTime));
            float speedRate = Mathf.Lerp(Mathf.Max(0.01f, resultBarStartSpeed), Mathf.Max(0.01f, resultBarEndSpeed), a * a);   // a*a: 加速が後になるほど強い
            barMoney = Mathf.Min(maxTotal, barMoney + screenAmount / Mathf.Max(0.1f, resultBarGrowTime) * speedRate * Time.deltaTime);
            // 棒の先が画面の resultBarFollowHeight の高さを超えたら、カメラが棒の先を追いかけて上へ進む
            float followMoney = screenAmount * Mathf.Clamp(resultBarFollowHeight, 0.1f, 1f);
            float bottom = Mathf.Max(0f, barMoney - followMoney);
            SetChartView(bottom, screenAmount);
            for (int i = 0; i < 2; i++)
                PlaceBar(i, Mathf.Min(barMoney, totals[i]));

            if (barMoney >= maxTotal)
            {
                // 伸び終わり → ズームアウトへ
                overviewTime = 0f;
                zoomFromBottom = bottom;
            }
            return;
        }

        // ズームアウト: カメラが引いていくように、グラフ全体(棒の太さ・レジ袋・文字・間隔も)を同じ割合で縮める。
        // 最後は、伸び始める位置 = ¥0、上の端 = 一番多い金額(+ レジ袋と金額が入る余白)
        overviewTime += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, overviewTime / Mathf.Max(0.05f, resultBarOverviewTime));
        // レジ袋と金額のぶん(画面の割合)だけ上を空ける(一緒に縮むので、割合はいつも同じ)
        // (文字とレジ袋は Result Bar Min Text Size より小さくならないので、最後の大きさで2回計算して合わせる)
        float fullAmount = screenAmount;
        for (int n = 0; n < 2; n++)
        {
            float textSize = Mathf.Max(screenAmount / fullAmount, Mathf.Clamp01(resultBarMinTextSize));   // 最後の文字の大きさ(引く前を1)
            float headroomRatio = Mathf.Clamp(barBagSize * 1.9f * textSize / barMaxHeight, 0f, 0.6f);
            fullAmount = Mathf.Max(screenAmount, maxTotal / (1f - headroomRatio));
        }
        // 映す範囲は掛け算で広げる(何倍ずつ広がるかが一定になり、カメラが一定の速さで引いて見える)。
        // 上の端も同じように動かし、引いている途中で棒の先が画面の外へ出ないようにする
        float windowSize = Mathf.Exp(Mathf.Lerp(Mathf.Log(screenAmount), Mathf.Log(fullAmount), k));
        float windowTop = Mathf.Exp(Mathf.Lerp(Mathf.Log(zoomFromBottom + screenAmount), Mathf.Log(fullAmount), k));
        float windowBottom = Mathf.Max(0f, windowTop - windowSize);
        SetChartView(windowBottom, windowSize);
        for (int i = 0; i < 2; i++)
            PlaceBar(i, totals[i]);

        if (overviewTime >= Mathf.Max(0.05f, resultBarOverviewTime) + Mathf.Max(0f, resultBarHoldTime))
            barsDone = true;   // 棒グラフは消さずに残し、そのまま勝ち負けの画面へ(ShowResult)
    }

    // カメラが「金額 windowBottom 〜 windowBottom + windowSize」を映しているように、グラフを動かして縮める
    // (伸び始める位置が windowBottom、上の端が windowBottom + windowSize。横は画面の真ん中に向かって縮む)
    // カメラが引いたように全部一緒に小さくするが、細く・小さくなりすぎないように
    //  - 棒の太さと2本の間隔(横)は Result Bar Min Width まで
    //  - 金額・レジ袋・名前・目盛りの文字は Result Bar Min Text Size まで
    void SetChartView(float windowBottom, float windowSize)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float scale = screenAmount / Mathf.Max(1f, windowSize);            // 縦の縮み
        float sx = Mathf.Max(scale, Mathf.Clamp01(resultBarMinWidth));      // 横の縮み
        float ts = Mathf.Max(scale, Mathf.Clamp01(resultBarMinTextSize));   // 文字・レジ袋の縮み
        barChart.localScale = new Vector3(sx, scale, 1f);
        barChart.position = new Vector3(0f, barBaseY - windowBottom / screenAmount * barMaxHeight * scale, 0f);

        // 文字・レジ袋は、グラフの縮みを打ち消して ts の大きさに見せる
        barItemScaleY = ts / scale;
        var item = new Vector3(ts / sx, ts / scale, 1f);
        for (int i = 0; i < 2; i++)
        {
            barBags[i].localScale = item;
            barTexts[i].transform.localScale = item;
            barNames[i].localScale = item;
            var np = barNames[i].localPosition;
            barNames[i].localPosition = new Vector3(np.x, barNameY * barItemScaleY, 0f);   // 名前と棒の間も文字に合わせる
        }
        foreach (var label in gridLabels)
            label.localScale = item;
        // 目盛りの線は、縮んでも画面の横いっぱいに見えるように長くする
        foreach (var line in gridLines)
            line.localScale = new Vector3(barViewWidth * 2f / sx, line.localScale.y, 1f);
    }

    // 棒 i を金額 money の高さにして、上にレジ袋と金額を乗せる(グラフの中の座標)
    void PlaceBar(int i, float money)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float h = money / screenAmount * barMaxHeight;
        float x = bars[i].localPosition.x;
        bars[i].localPosition = new Vector3(x, h / 2f, 0f);
        bars[i].localScale = new Vector3(barWidth, h, 1f);

        float bagH = barBagSize * barItemScaleY;   // 画面の上でレジ袋が見えている高さを、グラフの中の高さにしたもの
        float textY = h + bagH * 1.35f;
        float bagY = h + bagH * 0.6f;
        barBags[i].localPosition = new Vector3(x, bagY, 0f);
        barTexts[i].transform.localPosition = new Vector3(x, textY, 0f);
        SetTextWithShadow(barTexts[i], $"¥{Mathf.RoundToInt(money):N0}");
    }

    // 1万ごとの目盛り(横線と金額)。グラフの中に作るので、カメラが進む・引くのに合わせて動く
    void MakeBarGrid(Vector2 view, float size)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float maxTotal = Mathf.Max(0, Mathf.Max(p1.total, p2.total));
        for (float m = screenAmount; m <= maxTotal + screenAmount; m += screenAmount)
        {
            float y = m / screenAmount * barMaxHeight;
            var line = MakeBagPart("GridLine", barChart, layerShop, new Color(0.55f, 0.6f, 0.7f, 0.5f), 3000, false);
            line.localPosition = new Vector3(0f, y, 0f);
            line.localScale = new Vector3(view.x * 2f, 0.03f * size, 1f);
            gridLines.Add(line);
            var label = MakeText("GridLabel", barChart, layerShop, $"¥{m:N0}", new Color(0.3f, 0.32f, 0.4f), 3004);
            SetTextSize(label, 0.05f * size * Mathf.Max(0.3f, resultBarTextScale));
            label.anchor = TextAnchor.LowerLeft;
            label.transform.localPosition = new Vector3(-view.x / 2f + 0.15f, y + 0.05f, 0f);
            gridLabels.Add(label.transform);
        }
    }

    // 結果発表の背景: 上の色(resultBarBackTop)から下の色(resultBarBackBottom)へ、横じまでなめらかにつなぐ
    void MakeResultBackground(Transform parent, Vector2 view)
    {
        const int bands = 24;
        float h = (view.y + 2f) / bands;
        for (int b = 0; b < bands; b++)
        {
            float k = (b + 0.5f) / bands;   // 0 = 下、1 = 上
            var band = MakeBagPart($"Back{b}", parent, layerShop, Color.Lerp(resultBarBackBottom, resultBarBackTop, k), 2999, false);
            SetRect(band, 0f, -(view.y + 2f) / 2f + h * (b + 0.5f), view.x + 2f, h * 1.02f);
        }
    }

    // 文字の右下に、うすい影を付けて読みやすくする(文字と一緒に動く・大きさが変わる)
    static void AddTextShadow(TextMesh tm)
    {
        var mr = tm.GetComponent<MeshRenderer>();
        var go = new GameObject("Shadow");
        go.layer = tm.gameObject.layer;
        go.transform.SetParent(tm.transform, false);
        float off = tm.characterSize * tm.fontSize / 64f * 0.6f;   // 見た目の大きさに合わせる(SetTextSize)
        go.transform.localPosition = new Vector3(off, -off, 0f);
        var sh = go.AddComponent<TextMesh>();
        sh.font = tm.font;
        sh.text = tm.text;
        sh.fontSize = tm.fontSize;
        sh.characterSize = tm.characterSize;
        sh.anchor = tm.anchor;
        sh.alignment = tm.alignment;
        sh.color = new Color(1f, 1f, 1f, 0.85f);   // 背景が明るいので、白い影で文字のふちを立たせる
        var smr = go.GetComponent<MeshRenderer>();
        smr.material = mr.sharedMaterial;
        smr.sortingOrder = mr.sortingOrder - 1;
    }

    // 文字を変えたら、影の文字も同じにする
    static void SetTextWithShadow(TextMesh tm, string text)
    {
        tm.text = text;
        var sh = tm.transform.Find("Shadow");
        if (sh != null) sh.GetComponent<TextMesh>().text = text;
    }

    // 棒の上に乗せるレジ袋。袋の画像(Bag Sprite)があればそれ、無ければ四角と持ち手の仮の形
    Transform MakeResultBagIcon(Transform parent, string objName, float size)
    {
        var root = new GameObject(objName);
        root.layer = layerShop;
        root.transform.SetParent(parent);
        // (作っている間は root を世界の原点に置いたまま。中の形は原点からの位置で置く)
        var holder = new GameObject("Icon");
        holder.layer = layerShop;
        holder.transform.SetParent(root.transform, false);

        if (bagSprite != null)
        {
            var sr = holder.AddComponent<SpriteRenderer>();
            sr.sprite = bagSprite;
            sr.sortingOrder = 3002;
            Vector2 b = bagSprite.bounds.size;
            float k = size / Mathf.Max(b.x, b.y, 0.01f);
            holder.transform.localScale = new Vector3(k, k, 1f);
        }
        else
        {
            var gray = new Color(0.62f, 0.66f, 0.7f);
            var body = MakeBagPart("Body", holder.transform, layerShop, gray, 3002, false);
            SetRect(body, 0f, -size * 0.1f, size * 0.8f, size * 0.75f);
            var inner = MakeBagPart("Inner", holder.transform, layerShop, Color.white, 3003, false);
            SetRect(inner, 0f, -size * 0.1f, size * 0.7f, size * 0.65f);
            for (int s = -1; s <= 1; s += 2)
            {
                var handle = MakeBagPart("Handle", holder.transform, layerShop, gray, 3002, false);
                SetRect(handle, s * size * 0.28f, size * 0.38f, size * 0.12f, size * 0.25f);
            }
        }
        return root.transform;
    }

    // 1P/2P の画面いっぱいに、勝ち負けを大きく出す(レジ袋の画面の上にかぶせる)
    void ShowResultOnPlayerScreen(Player p, int result)
    {
        var root = new GameObject($"Result_{p.name}");
        root.layer = p.layer;
        root.transform.SetParent(resultRoot.transform);
        Vector2 view = PlayerViewSize();
        float size = view.x / 8f;

        Color bg = result > 0 ? new Color(0.95f, 0.72f, 0.15f, 0.92f) : result < 0 ? new Color(0.15f, 0.15f, 0.2f, 0.92f) : new Color(0.35f, 0.38f, 0.5f, 0.92f);
        var panel = MakeBagPart("Panel", root.transform, p.layer, bg, 3000, false);
        SetRect(panel, 0f, 0f, view.x * 3f, view.y * 3f);   // 画面の形が違っても全部おおう

        string text = result > 0 ? "あなたの\n勝ち！" : result < 0 ? "あなたの\n負け！" : "引き分け！";
        var main = MakeText("Result", root.transform, p.layer, text, result < 0 ? new Color(0.75f, 0.8f, 0.95f) : Color.white, 3002);
        SetTextSize(main, 0.22f * size);
        main.transform.position = Vector3.zero;   // 画面の真ん中(合計金額は棚の画面に出す)
    }

    // どちらかのコントローラー(参加していないものも)、またはキーボード(Space / Enter)で A
    bool AnyAPressed()
    {
        foreach (var g in Gamepad.all)
            if (g.buttonSouth.wasPressedThisFrame) return true;
        var k = Kb;
        return k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
    }
}
