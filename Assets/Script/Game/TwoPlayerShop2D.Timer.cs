using UnityEngine;

// 制限時間(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// ゲーム開始(エントリー画面のカウントダウンが終わったとき。エントリー画面が無ければ再生したとき)から gameTime 秒を数え、棚の画面に残り時間を出す。0 になったら「タイムアップ！」
public partial class TwoPlayerShop2D
{
    TextMesh timerText;
    float timeLeft = -1f;   // 残りの秒数(-1 = まだ数え始めていない)

    /// <summary>制限時間が 0 になったか(勝ち負けを決めるときに使う)</summary>
    public bool IsTimeUp => timeLeft == 0f;

    void UpdateGameTimer()
    {
        // 時間は表示を消していても数える
        if (timeLeft < 0f) timeLeft = Mathf.Max(0f, gameTime);
        if (!InEntry && entryPhase != -1) timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);   // エントリー中は数えない(TwoPlayerShop2D.Entry.cs)

        if (timerText == null)
            timerText = MakeText("GameTimer", null, layerShop, "", Color.white, 1500);
        timerText.gameObject.SetActive(showGameTimer);
        if (!showGameTimer) return;

        int sec = Mathf.CeilToInt(timeLeft);
        timerText.text = timeLeft > 0f ? $"{sec / 60}:{sec % 60:00}" : "タイムアップ！";
        timerText.color = timeLeft > timerWarningTime ? Color.white : new Color(1f, 0.35f, 0.3f);

        // 縦: 上の空き場所の真ん中 / 横: 画面の上(商品の上の段より上)
        Vector2 view = ShopViewSize();
        float y = IsPortrait && portraitTopSpace > 0f
            ? view.y / 2f - view.y * portraitTopSpace / 2f
            : view.y / 2f - 1f;
        timerText.transform.position = new Vector3(0f, y, 0f);
        float fit = IsPortrait ? Mathf.Max(1f, view.x / 8f) : 1f;   // 縦: 幅に合わせる(TIMER の目印と同じ)
        timerText.characterSize = 0.16f * fit * Mathf.Max(0.5f, textScale / 2f);
    }
}
