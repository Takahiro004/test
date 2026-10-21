using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

// コントローラーの 1P/2P の登録(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「機能のON/OFF」→ Fixed Pad Assignment)
// 登録を始める: キーボードの F2(ノートパソコンは Fn + F2 のことがある)/ どれかのコントローラーのビューボタン(□が2つのボタン)を1.5秒長押し /
//              Inspector で TwoPlayerShop2D を右クリック →「コントローラー登録を始める」
// → 1P用のコントローラーで A → 2P用のコントローラーで A。
// 登録はパソコンに保存され(PlayerPrefs)、ゲームを閉じても残る。次からは登録したコントローラーが必ず 1P/2P になる
// (ボタンを押さなくても、つながっていれば最初から割り当てる)。登録していないコントローラーは使わない。
// 登録が無いときは、前と同じ「最初に A を押した方が 1P」。
//
// コントローラーの見分け方(PadId):
//  - 製造番号(serial)が取れるものはそれで見分ける(Bluetooth のものなど)
//  - Xbox のコントローラーを USB でつないだときは製造番号が取れないので、Windows が付ける番号(1〜4。ボタンのランプの位置)で見分ける。
//    この番号はつないだ順に決まるので、つなぎ直して順番が変わったら、F2 で登録し直す
public partial class TwoPlayerShop2D
{
    const string Pad1Key = "Shop2D.Pad1", Pad2Key = "Shop2D.Pad2";
    int padRegisterStep;          // 0: 登録していない / 1: 1P用の A を待っている / 2: 2P用の A を待っている
    string registeredPad1;        // 登録の途中で 1P に選んだコントローラー
    TextMesh[] padRegisterLabels;
    Transform[] padRegisterPanels;
    float padRegisterHold;        // ビューボタンを長押ししている秒数
    bool padRegisterRequested;    // Inspector の右クリックから始めた

    static string SavedPadId(int playerIndex) => PlayerPrefs.GetString(playerIndex == 0 ? Pad1Key : Pad2Key, "");
    bool HasPadRegistration => fixedPadAssignment && (SavedPadId(0) != "" || SavedPadId(1) != "");

    // コントローラーを見分けるための名前
    static string PadId(Gamepad g)
    {
        var d = g.description;
        if (!string.IsNullOrEmpty(d.serial)) return "serial:" + d.serial;
        var m = Regex.Match(d.capabilities ?? "", "\"userIndex\"\\s*:\\s*(\\d+)");   // Xbox(XInput)の番号 0〜3
        if (m.Success) return "xinput:" + m.Groups[1].Value;
        return "path:" + g.path;   // どちらも取れないとき(つなぎ直すと変わることがある)
    }

    // 人に見せる番号(Xbox ならランプの番号 1〜4)
    static string PadLabel(Gamepad g)
    {
        string id = PadId(g);
        return id.StartsWith("xinput:") ? $"{g.displayName}(ランプ {int.Parse(id.Substring(7)) + 1})" : g.displayName;
    }

    // 登録中なら true(そのフレームはゲームを止める)
    bool UpdatePadRegistration()
    {
        var kb = Keyboard.current;
        if (padRegisterStep == 0)
        {
            // ビューボタンの長押し(どれかのコントローラー)
            bool holding = false;
            foreach (var g in Gamepad.all) if (g.selectButton.isPressed) holding = true;
            padRegisterHold = holding ? padRegisterHold + Time.deltaTime : 0f;

            bool start = (kb != null && kb.f2Key.wasPressedThisFrame) || padRegisterHold >= 1.5f || padRegisterRequested;
            if (!start) return false;
            padRegisterRequested = false;
            padRegisterHold = 0f;
            Debug.Log("[Shop2D] コントローラー登録を始めた");
            padRegisterStep = 1;
            registeredPad1 = null;
            ShowPadRegisterMessage("コントローラー登録\n1P用のコントローラーで A\n(Esc でやめる)");
            return true;
        }

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            padRegisterStep = 0;
            ShowPadRegisterMessage(null);
            lastEvent = "コントローラー登録をやめた(前の登録のまま)";
            return true;
        }

        foreach (var g in Gamepad.all)
        {
            if (!g.buttonSouth.wasPressedThisFrame) continue;
            string id = PadId(g);
            if (padRegisterStep == 1)
            {
                registeredPad1 = id;
                padRegisterStep = 2;
                ShowPadRegisterMessage($"1P: {PadLabel(g)}\n2P用のコントローラーで A\n(Esc でやめる)");
            }
            else if (id != registeredPad1)   // 1P と同じコントローラーは選べない
            {
                PlayerPrefs.SetString(Pad1Key, registeredPad1);
                PlayerPrefs.SetString(Pad2Key, id);
                PlayerPrefs.Save();
                padRegisterStep = 0;
                pad1 = pad2 = keptPad1 = keptPad2 = null;   // 登録どおりに割り当て直す
                ShowPadRegisterMessage(null);
                lastEvent = $"コントローラーを登録した 2P: {PadLabel(g)}";
                Debug.Log($"[Shop2D] コントローラー登録 1P={registeredPad1} 2P={id}");
            }
            break;
        }
        return true;
    }

    [ContextMenu("コントローラー登録を始める")]
    void RequestPadRegistration() => padRegisterRequested = true;   // 再生中に、Inspector の右クリックから

    // 登録の案内を、3つの画面の真ん中に、黒い板の上に出す(null で消す)
    void ShowPadRegisterMessage(string text)
    {
        if (padRegisterLabels == null)
        {
            int[] layers = { layerP1, layerShop, layerP2 };
            padRegisterLabels = new TextMesh[layers.Length];
            padRegisterPanels = new Transform[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                padRegisterPanels[i] = MakeBagPart("PadRegisterPanel", null, layers[i], new Color(0f, 0f, 0f, 0.8f), 4999, false);
                padRegisterLabels[i] = MakeText("PadRegister", null, layers[i], "", new Color(1f, 0.95f, 0.4f), 5000);
            }
        }
        // 画面の大きさに合わせる(どの画面でも真ん中に、横幅の8割)
        Vector2 view = ShopViewSize();
        float w = Mathf.Min(view.x, PlayerViewSize().x) * 0.9f;
        for (int i = 0; i < padRegisterLabels.Length; i++)
        {
            padRegisterLabels[i].gameObject.SetActive(text != null);
            padRegisterPanels[i].gameObject.SetActive(text != null);
            if (text == null) continue;
            padRegisterLabels[i].text = text;
            padRegisterLabels[i].characterSize = w / 90f;   // 1行 14文字くらいが板に入る大きさ
            SetRect(padRegisterPanels[i], 0f, 0f, w, w * 0.45f);
        }
    }

    // 登録どおりに割り当てる(つながっていれば、ボタンを押さなくても)。登録が無い人は、前と同じく A を押した順
    void AssignRegisteredPads()
    {
        foreach (var g in Gamepad.all)
        {
            string id = PadId(g);
            if (pad1 == null && id == SavedPadId(0) && g != pad2) { pad1 = keptPad1 = g; Debug.Log($"[Shop2D] 1P(登録済み): {PadLabel(g)}"); }
            else if (pad2 == null && id == SavedPadId(1) && g != pad1) { pad2 = keptPad2 = g; Debug.Log($"[Shop2D] 2P(登録済み): {PadLabel(g)}"); }
        }
    }

    // 登録済みのコントローラーか(登録したものは、A を押した順の割り当てに使わない)
    bool IsRegisteredPad(Gamepad g)
    {
        if (!HasPadRegistration) return false;
        string id = PadId(g);
        return id == SavedPadId(0) || id == SavedPadId(1);
    }
}
