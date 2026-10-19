using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイムアップのあと(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 操作と点数を止めて、
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

    // Start で呼ぶ: 前のゲームの割り当てを戻す
    void RestorePads()
    {
        if (keptPad1 != null && keptPad1.added) pad1 = keptPad1;
        if (keptPad2 != null && keptPad2.added) pad2 = keptPad2;
    }

    void UpdateResult()
    {
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
