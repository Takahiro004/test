using System.Collections.Generic;
using UnityEngine;

// 高額商品の予告(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「高額商品の予告」と「機能のON/OFF: 棚」→ High Rank Notice)
// ランクごとの出やすさ(TwoPlayerShop2D.Deck.cs)で、高いランク(highRankNoticeFrom 以上)が出始める highRankNoticeLead 秒前に、
// 棚の画面のタイマーの下あたりに、予告の文字を右から左へ流す(highRankNoticeTime 秒で画面を横切る)。
//  - ランクごとに1回ずつ。2つ重なったら、前の文字が流れ終わってから次を流す
//  - 文字は highRankNoticeText({rank} はランクの数字に置きかわる)。上下の位置は highRankNoticeY でずらす
//  - 始めから出るランク(出始める時期 0)は予告しない
public partial class TwoPlayerShop2D
{
    readonly HashSet<int> noticedRanks = new HashSet<int>();   // もう予告したランク
    readonly Queue<string> noticeQueue = new Queue<string>();  // 流す順番を待っている文字
    TextMesh noticeText;
    float noticeElapsed;

    // LateUpdate から毎フレーム(タイマーのあと)
    void UpdateHighRankNotice()
    {
        if (highRankNotice && rankWeighting && UseDeck && !InEntry && timeLeft >= 0f && !IsTimeUp && gameTime > 0f)
        {
            float elapsed = gameTime - timeLeft;   // 試合が始まってからの秒数
            for (int rank = Mathf.Clamp(highRankNoticeFrom, 1, 5); rank <= 5; rank++)
            {
                float start = rankStartProgress != null && rank - 1 < rankStartProgress.Length ? rankStartProgress[rank - 1] : 0f;
                if (start <= 0f || noticedRanks.Contains(rank)) continue;
                if (elapsed < start * gameTime - Mathf.Max(0f, highRankNoticeLead)) continue;
                noticedRanks.Add(rank);
                noticeQueue.Enqueue((highRankNoticeText ?? "").Replace("{rank}", rank.ToString()));
            }
        }
        if (IsTimeUp) noticeQueue.Clear();

        // 今流している文字が無ければ、次を出す
        if (noticeText == null)
        {
            if (noticeQueue.Count == 0) return;
            noticeText = MakeText("HighRankNotice", null, layerShop, noticeQueue.Dequeue(), highRankNoticeColor, 1500);
            Vector2 v = ShopViewSize();
            float fit = IsPortrait ? Mathf.Max(1f, v.x / 8f) : 1f;   // タイマーと同じく、縦は幅に合わせる
            SetTextSize(noticeText, 0.07f * fit * Mathf.Max(0.5f, textScale / 2f));
            AddTextShadow(noticeText);
            noticeElapsed = 0f;
        }

        // 右の画面の外 → 左の画面の外へ流す(高さはタイマーの下あたり)
        Vector2 view = ShopViewSize();
        float timerY = IsPortrait && portraitTopSpace > 0f ? view.y / 2f - view.y * portraitTopSpace / 2f : view.y / 2f - 1f;
        float y = timerY - (IsPortrait && portraitTopSpace > 0f ? view.y * portraitTopSpace * 0.38f : 1.1f) + highRankNoticeY;
        float width = noticeText.GetComponent<MeshRenderer>().bounds.size.x;
        noticeElapsed += Time.deltaTime;
        float k = noticeElapsed / Mathf.Max(0.5f, highRankNoticeTime);
        float x = Mathf.Lerp(view.x / 2f + width / 2f, -view.x / 2f - width / 2f, k);
        noticeText.transform.position = new Vector3(x, y, 0f);
        if (k >= 1f)
        {
            Destroy(noticeText.gameObject);
            noticeText = null;
        }
    }
}
