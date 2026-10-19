using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// タイムアップのあと(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 操作と点数を止めて、棚の画面を左右に分け(左 1P / 右 2P)、
// 合計金額の多い方に「あなたの勝ち！」、少ない方に「あなたの負け！」、同じなら両方に「引き分け！」を出す。
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
            nameText.characterSize = 0.12f * size;
            nameText.transform.position = new Vector3(cx, view.y * 0.28f, 0f);

            string text = r > 0 ? "あなたの\n勝ち！" : r < 0 ? "あなたの\n負け！" : "引き\n分け！";
            var main = MakeText($"Result{i + 1}", t, layerShop, text, r < 0 ? new Color(0.75f, 0.8f, 0.95f) : Color.white, 3002);
            main.characterSize = 0.14f * size;
            main.transform.position = new Vector3(cx, view.y * 0.05f, 0f);

            var total = MakeText($"Total{i + 1}", t, layerShop, $"¥{(i == 0 ? total1 : total2):N0}", Color.white, 3002);
            total.characterSize = 0.1f * size;
            total.transform.position = new Vector3(cx, -view.y * 0.2f, 0f);

            var hint = MakeText($"Hint{i + 1}", t, layerShop, "Aで\nもう一度", new Color(1f, 1f, 1f, 0.8f), 3002);
            hint.characterSize = 0.06f * size;
            hint.transform.position = new Vector3(cx, -view.y * 0.38f, 0f);
            if (i == 0) resultHint1 = hint; else resultHint2 = hint;
        }

        // 真ん中の仕切り
        var line = MakeBagPart("Divider", t, layerShop, Color.white, 3001, false);
        SetRect(line, 0f, 0f, 0.08f * size, view.y + 2f);
        resultTime = 0f;
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
