using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイムアップのあと(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 操作と点数を止めて、
//  0. 結果発表(showResultBars): 棚の画面で、合計金額の棒グラフ(1P 赤・2P 青)が下から伸びる。上にレジ袋と金額
//     2本は同じ速さで伸び、少ない方が先に止まる。画面の上の端 = resultBarScreenAmount(1万)。
//     超えたら画面が上へ進んだように、棒は下の端からもう一度伸びる(低い方の棒は画面の下へ流れて見えなくなる)
//     → 伸び終わったら、全体が見えるように縮めて見せる → resultBarHoldTime 秒たったら、棒グラフを残したまま勝ち負けの画面へ
//  - 1P/2P それぞれの画面: 合計金額の多い方に「あなたの勝ち！」、少ない方に「あなたの負け！」、同じなら「引き分け！」を大きく出す
//  - 棚の画面: 左右に分けて(左 1P / 右 2P)、1P/2P・合計金額・「Aでもう一度」を出す
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
    // グラフ全体を動かす・縮めることで「カメラが上へ進む」「カメラがズームアウトする」ように見せる
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
    float barChartScale = 1f;       // 今のグラフの縮み具合(1 = 1万が画面の高さ)
    float barBagSize;               // レジ袋の高さ(画面の上での大きさ。ズームしても変えない)
    readonly Transform[] barNames = new Transform[2];
    readonly System.Collections.Generic.List<Transform> gridLines = new System.Collections.Generic.List<Transform>();
    readonly System.Collections.Generic.List<Transform> gridLabels = new System.Collections.Generic.List<Transform>();
    float barViewWidth, barTextSize;
    TextMesh barTopLabel, barBottomLabel;

    // Start で呼ぶ: 前のゲームの割り当てを戻す
    void RestorePads()
    {
        if (keptPad1 != null && keptPad1.added) pad1 = keptPad1;
        if (keptPad2 != null && keptPad2.added) pad2 = keptPad2;
    }

    void UpdateResult()
    {
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
            // 「結果発表」の見出しの場所(画面のいちばん上)に「Aでもう一度」を出す
            Vector2 vis = ShopVisibleSize();
            float s2 = Mathf.Min(vis.x, vis.y) / 8f;
            var titleTr = barsRoot.transform.Find("Title");
            if (titleTr != null) titleTr.gameObject.SetActive(false);
            var hint = MakeText("Hint", t, layerShop, "Aでもう一度", new Color(0.2f, 0.2f, 0.25f), 3005);
            hint.characterSize = 0.07f * s2;
            hint.transform.position = new Vector3(0f, vis.y * 0.46f, 0f);
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
            nameText.characterSize = 0.18f * size;
            nameText.transform.position = new Vector3(cx, view.y * 0.2f, 0f);

            var total = MakeText($"Total{i + 1}", t, layerShop, $"¥{(i == 0 ? total1 : total2):N0}", Color.white, 3002);
            total.characterSize = 0.11f * size;
            total.transform.position = new Vector3(cx, -view.y * 0.05f, 0f);

            ShowResultOnPlayerScreen(player, r);

            var hint = MakeText($"Hint{i + 1}", t, layerShop, "Aで\nもう一度", new Color(1f, 1f, 1f, 0.8f), 3002);
            hint.characterSize = 0.06f * size;
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
        var ink = new Color(0.2f, 0.2f, 0.25f);

        // 画面に止まっているもの: 背景(白)・見出し・上の端/下の端の金額
        var panel = MakeBagPart("Panel", t, layerShop, new Color(0.97f, 0.97f, 0.97f), 2999, false);
        SetRect(panel, 0f, 0f, view.x + 2f, view.y + 2f);
        var title = MakeText("Title", t, layerShop, "結果発表", ink, 3006);
        title.characterSize = 0.08f * size;
        title.transform.position = new Vector3(0f, view.y * 0.46f, 0f);

        barBaseY = -view.y / 2f;     // 画面の下の端
        barMaxHeight = view.y;       // 画面の下の端〜上の端 = resultBarScreenAmount(1万)
        barTopEdge = view.y / 2f;
        barWidth = Mathf.Min(view.x, view.y) * 0.19f;

        barTopLabel = MakeText("TopLabel", t, layerShop, "", new Color(0.5f, 0.5f, 0.55f), 3006);
        barTopLabel.characterSize = 0.05f * size;
        barTopLabel.anchor = TextAnchor.UpperLeft;
        barTopLabel.transform.position = new Vector3(-view.x / 2f + 0.15f, barTopEdge - 0.05f, 0f);
        barBottomLabel = MakeText("BottomLabel", t, layerShop, "", new Color(0.5f, 0.5f, 0.55f), 3006);
        barBottomLabel.characterSize = 0.05f * size;
        barBottomLabel.anchor = TextAnchor.LowerLeft;
        barBottomLabel.transform.position = new Vector3(-view.x / 2f + 0.15f, barBaseY + view.y * 0.08f, 0f);

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
            barTexts[i].characterSize = 0.07f * size;

            var nameText = MakeText($"Name{i + 1}", barChart, layerShop, i == 0 ? "1P" : "2P", ink, 3004);
            nameText.characterSize = 0.08f * size;
            nameText.transform.localPosition = new Vector3(x, view.y * 0.04f, 0f);   // 画面の下の端の近く(棒に重なっても読める黒っぽい字)
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
            // 伸びている途中: 1画面(下の端〜上の端)ぶんを resultBarGrowTime 秒で伸びる。2本は同じ速さ
            barMoney = Mathf.Min(maxTotal, barMoney + screenAmount / Mathf.Max(0.1f, resultBarGrowTime) * Time.deltaTime);
            // 上の端を超えるたびに、カメラが1画面ぶん上へ進む(棒は下の端からもう一度伸びて見える)
            int lap = barMoney > 0f ? Mathf.FloorToInt((barMoney - 0.001f) / screenAmount) : 0;
            float bottom = lap * screenAmount;
            for (int i = 0; i < 2; i++)
                PlaceBar(i, Mathf.Min(barMoney, totals[i]), bottom + screenAmount);
            SetChartView(bottom, screenAmount);
            barTopLabel.text = $"¥{bottom + screenAmount:N0}";
            barBottomLabel.text = lap > 0 ? $"¥{bottom:N0}" : "";

            if (barMoney >= maxTotal)
            {
                // 伸び終わり → ズームアウトへ
                overviewTime = 0f;
                zoomFromBottom = bottom;
                barTopLabel.gameObject.SetActive(false);
                barBottomLabel.gameObject.SetActive(false);
            }
            return;
        }

        // ズームアウト: カメラが引いていくように、グラフ全体(棒の太さ・レジ袋・文字・間隔も)を同じ割合で縮める。
        // 最後は、下の端 = ¥0、上の端 = 一番多い金額(+ レジ袋と金額が入る余白)
        overviewTime += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, overviewTime / Mathf.Max(0.05f, resultBarOverviewTime));
        // レジ袋と金額は画面の上での大きさが変わらないので、その高さのぶん(画面の割合)だけ上を空ける
        float headroomRatio = Mathf.Clamp(barBagSize * 1.9f / barMaxHeight, 0f, 0.6f);
        float fullAmount = Mathf.Max(screenAmount, maxTotal / (1f - headroomRatio));
        float windowBottom = Mathf.Lerp(zoomFromBottom, 0f, k);
        float windowSize = Mathf.Lerp(screenAmount, fullAmount, k);
        for (int i = 0; i < 2; i++)
            PlaceBar(i, totals[i], float.MaxValue);   // ズーム中は金額を上の端で止めない
        SetChartView(windowBottom, windowSize);

        if (overviewTime >= Mathf.Max(0.05f, resultBarOverviewTime) + Mathf.Max(0f, resultBarHoldTime))
            barsDone = true;   // 棒グラフは消さずに残し、そのまま勝ち負けの画面へ(ShowResult)
    }

    // カメラが「金額 windowBottom 〜 windowBottom + windowSize」を映しているように、グラフを動かして縮める
    // (画面の下の端が windowBottom、上の端が windowBottom + windowSize。横は画面の真ん中を中心に縮む)
    void SetChartView(float windowBottom, float windowSize)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float scale = screenAmount / Mathf.Max(1f, windowSize);
        barChartScale = scale;
        barChart.localScale = new Vector3(scale, scale, 1f);
        barChart.position = new Vector3(0f, barBaseY - windowBottom / screenAmount * barMaxHeight * scale, 0f);

        // 縮めるのは棒だけ。レジ袋・金額・名前・目盛りの線の太さと文字は、画面の上での大きさを変えない
        float inv = 1f / scale;
        for (int i = 0; i < 2; i++)
        {
            barBags[i].localScale = new Vector3(inv, inv, 1f);
            barTexts[i].transform.localScale = new Vector3(inv, inv, 1f);
            barNames[i].localScale = new Vector3(inv, inv, 1f);
            var np = barNames[i].localPosition;
            barNames[i].localPosition = new Vector3(np.x, barMaxHeight * 0.04f * inv + windowBottom / screenAmount * barMaxHeight, 0f);   // いつも画面の下の端の近く
        }
        foreach (var line in gridLines)
            line.localScale = new Vector3(barViewWidth * 2f * inv, 0.03f * barTextSize * inv, 1f);
        foreach (var label in gridLabels)
        {
            label.localScale = new Vector3(inv, inv, 1f);
            var lp = label.localPosition;
            label.localPosition = new Vector3((-barViewWidth / 2f + 0.15f) * inv, lp.y, 0f);   // いつも画面の左の端
        }
    }

    // 棒 i を金額 money の高さにして、上にレジ袋と金額を乗せる(グラフの中の座標)
    // clampMoney: 金額とレジ袋がこの金額の高さ(= 今の画面の上の端)を超えないように、棒に重ねる
    void PlaceBar(int i, float money, float clampMoney)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float h = money / screenAmount * barMaxHeight;
        float x = bars[i].localPosition.x;
        bars[i].localPosition = new Vector3(x, h / 2f, 0f);
        bars[i].localScale = new Vector3(barWidth, h, 1f);

        float bagH = barBagSize / Mathf.Max(0.01f, barChartScale);   // 画面の上で同じ大きさに見える、グラフの中の高さ
        float edge = clampMoney == float.MaxValue ? float.MaxValue : clampMoney / screenAmount * barMaxHeight;
        float textY = Mathf.Min(h + bagH * 1.35f, edge - bagH * 0.3f);
        float bagY = Mathf.Min(h + bagH * 0.6f, textY - bagH * 0.75f);
        barBags[i].localPosition = new Vector3(x, bagY, 0f);
        barTexts[i].transform.localPosition = new Vector3(x, textY, 0f);
        barTexts[i].text = $"¥{Mathf.RoundToInt(money):N0}";
    }

    // 1万ごとの目盛り(横線と金額)。グラフの中に作るので、カメラが進む・引くのに合わせて動く
    void MakeBarGrid(Vector2 view, float size)
    {
        float screenAmount = Mathf.Max(1, resultBarScreenAmount);
        float maxTotal = Mathf.Max(0, Mathf.Max(p1.total, p2.total));
        for (float m = screenAmount; m <= maxTotal + screenAmount; m += screenAmount)
        {
            float y = m / screenAmount * barMaxHeight;
            var line = MakeBagPart("GridLine", barChart, layerShop, new Color(0.8f, 0.8f, 0.85f), 3000, false);
            line.localPosition = new Vector3(0f, y, 0f);
            line.localScale = new Vector3(view.x * 2f, 0.03f * size, 1f);
            gridLines.Add(line);
            var label = MakeText("GridLabel", barChart, layerShop, $"¥{m:N0}", new Color(0.55f, 0.55f, 0.6f), 3004);
            label.characterSize = 0.05f * size;
            label.anchor = TextAnchor.LowerLeft;
            label.transform.localPosition = new Vector3(-view.x / 2f + 0.15f, y + 0.05f, 0f);
            gridLabels.Add(label.transform);
        }
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
        main.characterSize = 0.22f * size;
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
