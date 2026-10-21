using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

// コントローラーの 1P/2P の登録(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「機能のON/OFF」→ Fixed Pad Assignment)
// キーボードの F2 で登録を始める → 1P用のコントローラーで A → 2P用のコントローラーで A。
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
            if (kb == null || !kb.f2Key.wasPressedThisFrame) return false;
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

    // 登録の案内を、3つの画面の真ん中に出す(null で消す)
    void ShowPadRegisterMessage(string text)
    {
        if (padRegisterLabels == null)
        {
            int[] layers = { layerP1, layerShop, layerP2 };
            padRegisterLabels = new TextMesh[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                padRegisterLabels[i] = MakeText("PadRegister", null, layers[i], "", new Color(1f, 0.95f, 0.4f), 5000);
                padRegisterLabels[i].characterSize = HudTextSize * 1.2f;
            }
        }
        foreach (var label in padRegisterLabels)
        {
            label.gameObject.SetActive(text != null);
            if (text != null) label.text = text;
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
