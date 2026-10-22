using UnityEngine;
using UnityEngine.InputSystem;

// エントリー画面(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「機能のON/OFF」→ Show Entry Screen と「エントリー画面」)
// 再生するとまずエントリー画面。1P/2P それぞれ A で参加(B で取り消し)。両方そろったらカウントダウン(3・2・1・スタート！。棚の画面だけに大きく)してゲーム開始。
// 制限時間はゲーム開始から数える。
//  - 先に A を押したコントローラーが 1P、次が 2P。ただし前に登録したコントローラー(TwoPlayerShop2D.Pads.cs)は、登録した側に入る
//  - 両方そろったら、その割り当てを登録として保存する(次の試合・次に起動したときも同じ側)
//  - キーボード予備: 1P Space / 2P Enter で参加
//  - 1P/2P の画面: 自分の参加の状態 / 棚の画面: ゲーム名と2人の状態
public partial class TwoPlayerShop2D
{
    int entryPhase = -1;                       // -1: まだ / 0: 終わった(ゲーム中) / 1: 参加を待っている / 2: カウントダウン中
    readonly bool[] entryReady = new bool[2];
    float entryCountdownLeft;
    GameObject entryRoot;
    TextMesh entryShopTitle, entryShopStatus;
    readonly TextMesh[] entryPlayerText = new TextMesh[2];

    bool InEntry => entryPhase > 0;

    // エントリー中なら true(そのフレームはゲームを止める)
    bool UpdateEntry()
    {
        if (entryPhase == -1)
        {
            entryPhase = showEntryScreen ? 1 : 0;
            if (entryPhase == 1) BuildEntryScreen();
        }
        if (entryPhase == 0) return false;

        if (entryPhase == 1)
        {
            UpdateEntryJoin();
            if (entryReady[0] && entryReady[1])
            {
                SaveEntryPads();
                entryPhase = 2;
                entryCountdownLeft = Mathf.Max(0f, entryCountdown);
            }
        }
        else
        {
            entryCountdownLeft -= Time.deltaTime;
            if (entryCountdownLeft <= 0f)
            {
                entryPhase = 0;
                StartCoroutine(EntryStartMessage());   // 「スタート！」を少し出してから消す
                lastEvent = "ゲーム開始！";
                return false;
            }
        }
        RefreshEntryTexts();
        return true;
    }

    void UpdateEntryJoin()
    {
        foreach (var g in Gamepad.all)
        {
            if (g.buttonEast.wasPressedThisFrame)   // B: 取り消し
            {
                if (g == pad1) { entryReady[0] = false; pad1 = keptPad1 = null; }
                if (g == pad2) { entryReady[1] = false; pad2 = keptPad2 = null; }
                continue;
            }
            if (!g.buttonSouth.wasPressedThisFrame && !g.startButton.wasPressedThisFrame) continue;

            // もう割り当てがあるコントローラー(もう一度遊ぶとき)は、その側で参加
            if (g == pad1) { entryReady[0] = true; continue; }
            if (g == pad2) { entryReady[1] = true; continue; }

            // 登録したコントローラーは登録した側、それ以外は空いている側(1P が先)
            string id = PadId(g);
            int side = id == SavedPadId(0) && pad1 == null && !entryReady[0] ? 0
                     : id == SavedPadId(1) && pad2 == null && !entryReady[1] ? 1
                     : pad1 == null && !entryReady[0] ? 0
                     : pad2 == null && !entryReady[1] ? 1 : -1;
            if (side == 0) { pad1 = keptPad1 = g; entryReady[0] = true; }
            else if (side == 1) { pad2 = keptPad2 = g; entryReady[1] = true; }
            if (side >= 0) Debug.Log($"[Shop2D] エントリー {(side == 0 ? "1P" : "2P")}: {PadLabel(g)}");
        }

        var kb = Kb;   // キーボード予備
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) entryReady[0] = true;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) entryReady[1] = true;
        }
    }

    // エントリーで決まった割り当てを、登録として保存する
    void SaveEntryPads()
    {
        if (!fixedPadAssignment) return;
        if (pad1 != null) PlayerPrefs.SetString(Pad1Key, PadId(pad1));
        if (pad2 != null) PlayerPrefs.SetString(Pad2Key, PadId(pad2));
        PlayerPrefs.Save();
    }

    void BuildEntryScreen()
    {
        entryRoot = new GameObject("Entry");
        var t = entryRoot.transform;
        int[] layers = { layerP1, layerShop, layerP2 };
        foreach (int layer in layers)   // 3つの画面を暗くする(後ろの棚・袋はうっすら見える)
        {
            var panel = MakeBagPart("EntryPanel", t, layer, new Color(0f, 0f, 0f, 0.75f), 4900, false);
            SetRect(panel, 0f, 0f, 200f, 200f);
        }

        Vector2 shop = ShopViewSize();
        entryShopTitle = MakeText("EntryTitle", t, layerShop, entryTitle, new Color(1f, 0.85f, 0.3f), 4901);
        SetTextSize(entryShopTitle, shop.x / 55f);
        entryShopTitle.transform.position = new Vector3(0f, shop.y * 0.22f, 0f);
        entryShopStatus = MakeText("EntryStatus", t, layerShop, "", Color.white, 4901);
        SetTextSize(entryShopStatus, shop.x / 90f);
        entryShopStatus.transform.position = new Vector3(0f, -shop.y * 0.08f, 0f);

        float pw = PlayerViewSize().x;
        for (int i = 0; i < 2; i++)
        {
            entryPlayerText[i] = MakeText($"EntryPlayer{i + 1}", t, i == 0 ? layerP1 : layerP2, "", Color.white, 4901);
            SetTextSize(entryPlayerText[i], pw / 55f);
            entryPlayerText[i].transform.position = new Vector3(0f, PlayerViewSize().y * 0.1f, 0f);
        }
        RefreshEntryTexts();
    }

    void RefreshEntryTexts()
    {
        if (entryRoot == null) return;
        if (entryPhase == 2)
        {
            // カウントダウンは棚の画面だけに、大きく出す(1P/2P の画面の文字は消す)
            entryShopTitle.gameObject.SetActive(false);
            entryShopStatus.text = Mathf.CeilToInt(entryCountdownLeft).ToString();
            SetTextSize(entryShopStatus, ShopViewSize().x / 12f);
            entryShopStatus.transform.position = Vector3.zero;
            for (int i = 0; i < 2; i++) entryPlayerText[i].gameObject.SetActive(false);
            return;
        }

        string[] state = new string[2];
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            state[i] = entryReady[i] ? "参加OK！" : "Aで参加";
            entryPlayerText[i].text = $"{p.name}\n{state[i]}" + (entryReady[i] ? "\n(Bで取り消し)" : "");
            entryPlayerText[i].color = entryReady[i] ? p.color : Color.white;
        }
        entryShopStatus.text = $"1P  {state[0]}\n2P  {state[1]}" +
                               (entryReady[0] && entryReady[1] ? "" : "\n\nふたりそろったらスタート");
    }

    // カウントダウンが終わったら「スタート！」を出して、エントリー画面を消す
    System.Collections.IEnumerator EntryStartMessage()
    {
        if (entryRoot == null) yield break;
        foreach (var r in entryRoot.GetComponentsInChildren<SpriteRenderer>()) r.enabled = false;   // 暗い板を消す
        entryShopTitle.gameObject.SetActive(false);
        entryShopStatus.text = "スタート！";   // 棚の画面だけ
        entryShopStatus.color = new Color(1f, 0.85f, 0.3f);
        SetTextSize(entryShopStatus, ShopViewSize().x / 36f);
        yield return new WaitForSeconds(0.8f);
        Destroy(entryRoot);
        entryRoot = null;
    }
}
