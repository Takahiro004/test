using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイムアップのあと(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 操作と点数を止めて、
//  0. 結果発表(showResultBars): 棚の画面で、合計金額の棒グラフ(1P 赤・2P 青)が下から伸びる。上にレジ袋と金額
//     2本は同じ速さで伸び、少ない方が先に止まる → 伸びきって resultBarHoldTime 秒たったら、下の勝ち負けの画面へ
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
    GameObject barsRoot;
    float barsTime;
    bool barsDone;
    readonly Transform[] bars = new Transform[2];
    readonly Transform[] barBags = new Transform[2];
    readonly TextMesh[] barTexts = new TextMesh[2];
    float barBaseY, barMaxHeight, barWidth;

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
            float s2 = Mathf.Min(view.x, view.y) / 8f;
            var hint = MakeText("Hint", t, layerShop, "Aでもう一度", new Color(0.2f, 0.2f, 0.25f), 3004);
            hint.characterSize = 0.07f * s2;
            hint.transform.position = new Vector3(0f, view.y * 0.47f, 0f);   // いちばん上(下は 1P/2P の名前)
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
        Vector2 view = ShopViewSize();
        float size = Mathf.Min(view.x, view.y) / 8f;

        // 背景(白)と見出し
        var panel = MakeBagPart("Panel", t, layerShop, new Color(0.97f, 0.97f, 0.97f), 3000, false);
        SetRect(panel, 0f, 0f, view.x + 2f, view.y + 2f);
        var title = MakeText("Title", t, layerShop, "結果発表", new Color(0.2f, 0.2f, 0.25f), 3004);
        title.characterSize = 0.12f * size;
        title.transform.position = new Vector3(0f, view.y * 0.4f, 0f);

        barBaseY = -view.y * 0.42f;
        barMaxHeight = view.y * 0.55f;
        barWidth = Mathf.Min(view.x, view.y) * 0.13f;
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * barWidth * 0.95f;
            Color c = i == 0 ? new Color(0.93f, 0.11f, 0.14f) : new Color(0f, 0.64f, 0.9f);
            bars[i] = MakeBagPart($"Bar{i + 1}", t, layerShop, c, 3001, false);
            SetRect(bars[i], x, barBaseY, barWidth, 0f);

            barBags[i] = MakeResultBagIcon(t, $"Bag{i + 1}", barWidth * 1.3f);
            barTexts[i] = MakeText($"Total{i + 1}", t, layerShop, "¥0", new Color(0.2f, 0.2f, 0.25f), 3004);
            barTexts[i].characterSize = 0.07f * size;

            var nameText = MakeText($"Name{i + 1}", t, layerShop, i == 0 ? "1P" : "2P", c, 3004);
            nameText.characterSize = 0.09f * size;
            nameText.transform.position = new Vector3(x, barBaseY - barWidth * 0.7f, 0f);
        }
        barsTime = 0f;
        lastEvent = "結果発表…";
    }

    void UpdateResultBars()
    {
        barsTime += Time.deltaTime;
        int[] totals = { p1.total, p2.total };
        int max = Mathf.Max(1, Mathf.Max(totals[0], totals[1]));
        // 2本は同じ速さで伸びる(多い方が resultBarGrowTime 秒で伸びきる)。少ない方が先に止まる
        float grown = Mathf.Clamp01(barsTime / Mathf.Max(0.1f, resultBarGrowTime));
        float height = barMaxHeight * Mathf.SmoothStep(0f, 1f, grown);

        for (int i = 0; i < 2; i++)
        {
            float target = barMaxHeight * Mathf.Max(0, totals[i]) / max;
            float h = Mathf.Min(height, target);
            float x = bars[i].position.x;
            SetRect(bars[i], x, barBaseY + h / 2f, barWidth, h);
            float top = barBaseY + h;
            float bagH = barBags[i].localScale.y;
            barBags[i].position = new Vector3(x, top + bagH * 0.6f, 0f);
            barTexts[i].transform.position = new Vector3(x, top + bagH * 1.35f, 0f);
            int shown = target > 0f ? Mathf.RoundToInt(Mathf.Max(0, totals[i]) * h / target) : 0;
            barTexts[i].text = $"¥{shown:N0}";
        }

        if (barsTime >= Mathf.Max(0.1f, resultBarGrowTime) + Mathf.Max(0f, resultBarHoldTime))
        {
            barsDone = true;   // 棒グラフは消さずに残し、そのまま勝ち負けの画面へ(ShowResult)
        }
    }

    // 棒の上に乗せるレジ袋。袋の画像(Bag Sprite)があればそれ、無ければ四角と持ち手の仮の形
    Transform MakeResultBagIcon(Transform parent, string objName, float size)
    {
        var root = new GameObject(objName);
        root.layer = layerShop;
        root.transform.SetParent(parent);
        root.transform.localScale = new Vector3(1f, size, 1f);   // 高さを y に覚えておく(位置合わせ用)
        var holder = new GameObject("Icon");
        holder.layer = layerShop;
        holder.transform.SetParent(root.transform, false);
        holder.transform.localScale = new Vector3(1f, 1f / size, 1f);   // 中は元の大きさに戻す

        if (bagSprite != null)
        {
            var sr = holder.AddComponent<SpriteRenderer>();
            sr.sprite = bagSprite;
            sr.sortingOrder = 3002;
            Vector2 b = bagSprite.bounds.size;
            float k = size / Mathf.Max(b.x, b.y, 0.01f);
            holder.transform.localScale = new Vector3(k, k / size, 1f);
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
