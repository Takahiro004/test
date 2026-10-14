using UnityEngine;
using UnityEngine.InputSystem;

// コントローラー/キーボードの入力(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
public partial class TwoPlayerShop2D
{
    // ---------- 入力(コントローラー優先 + キーボード予備) ----------

    // 1P = 最初にAボタンを押したコントローラー、2P = 次に押したコントローラー
    Gamepad PadOf(int playerIndex)
    {
        return playerIndex == 0 ? pad1 : pad2;
    }

    int PadCount => Gamepad.all.Count;

    Keyboard Kb => useKeyboard ? Keyboard.current : null;

    Vector2 StickOrDpad(Gamepad pad)
    {
        Vector2 v = pad.leftStick.ReadValue();
        if (v.magnitude < stickDeadzone) v = Vector2.zero;
        if (v == Vector2.zero) v = pad.dpad.ReadValue();   // 十字キーでも動かせる
        return v;
    }

    Vector2 GetMove1P()
    {
        var k = Kb;
        if (k != null)
        {
            var kv = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
                                 (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            if (kv != Vector2.zero) return kv;
        }

        var pad = PadOf(0);
        return pad != null ? StickOrDpad(pad) : Vector2.zero;
    }

    Vector2 GetMove2P()
    {
        var k = Kb;
        if (k != null)
        {
            var kv = new Vector2((k.rightArrowKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed ? 1 : 0),
                                 (k.upArrowKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed ? 1 : 0));
            if (kv != Vector2.zero) return kv;
        }

        var pad = PadOf(1);
        return pad != null ? StickOrDpad(pad) : Vector2.zero;
    }

    bool Grab1PPressed()
    {
        var pad = PadOf(0);
        if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;   // Aボタン
        var k = Kb;
        return k != null && k.spaceKey.wasPressedThisFrame;
    }

    // キック: LBボタン(キーボード予備: 1P Q / 2P 右Shift)
    bool Kick1PPressed()
    {
        var pad = PadOf(0);
        if (pad != null && pad.leftShoulder.wasPressedThisFrame) return true;
        var k = Kb;
        return k != null && k.qKey.wasPressedThisFrame;
    }

    bool Kick2PPressed()
    {
        var pad = PadOf(1);
        if (pad != null && pad.leftShoulder.wasPressedThisFrame) return true;
        var k = Kb;
        return k != null && k.rightShiftKey.wasPressedThisFrame;
    }

    bool Grab2PPressed()
    {
        var pad = PadOf(1);
        if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;   // Aボタン
        var k = Kb;
        return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
    }
}
