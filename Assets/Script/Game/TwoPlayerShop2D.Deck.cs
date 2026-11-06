using System.Collections.Generic;
using UnityEngine;

// ランダム補充の山札(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「機能のON/OFF」→ Random Refill)
// トランプと同じ考え方:
//  - 山札 = 商品の一覧の商品1枚ずつ(一覧が50品なら50枚)
//  - 最初に、棚のマスの数だけ山札からランダムに引いて並べる(棚に6個なら、山札は残り44枚)
//  - 商品を取ったら、その商品のカードは山札に戻る。少し待って(Refill Delay)、空いたマスに山札からランダムに1枚引いて置く
//  - 棚が流れて画面の外で左へ回ったときも、その棚の商品を山札に戻して、引き直す
//  - 山札が空のときは、そのマスは空いたまま(次に回ってきたときに引く)
//  - この試合の献立の食材は、試合のあいだに必ず1回以上棚に出す(TwoPlayerShop2D.Menu.cs):
//    まだ一度も棚に出ていない食材があれば、引くときに menuIngredientBoost の確率でその食材を引く。
//    残り時間が少なくなったら(まだの数 x 棚のスライドの間隔 より少ない)、必ずその食材を引く
//  - ランクごとの出やすさ(rankWeighting): ランクが高い商品ほど出にくく、試合の終盤に出始める。
//    ランク r の商品は、試合が rankStartProgress[r-1](0 = 始めから・1 = 終わり)まで進むまで引かない。
//    引ける商品の中から、rankWeights[r-1] の重みで引く(重み 2 は重み 1 の2倍出やすい)
public partial class TwoPlayerShop2D
{
    readonly List<int> deck = new List<int>();   // 山札(商品の一覧の番号)
    readonly HashSet<int> shownCards = new HashSet<int>();   // この試合で一度でも棚に置いたカード

    // ランダム補充を使うか(商品の一覧が空なら使わない)
    bool UseDeck => randomRefill && productList != null && productList.Length > 0;

    void InitDeck()
    {
        deck.Clear();
        shownCards.Clear();
        if (productList == null) return;
        for (int i = 0; i < productList.Length; i++) deck.Add(i);
    }

    // 山札からランダムに1枚引く(空なら -1)。まだ棚に出ていない献立の食材があれば、そちらを先に引くことがある
    int DrawCard()
    {
        if (deck.Count == 0) return -1;
        int menuCard = DrawPendingMenuCard();
        if (menuCard >= 0) return menuCard;
        int k = rankWeighting ? DrawIndexByRank() : Random.Range(0, deck.Count);
        int card = deck[k];
        deck.RemoveAt(k);
        return card;
    }

    // 試合がどのくらい進んだか(0: 始め 〜 1: 終わり)。数え始める前(最初に棚を並べるとき)は 0
    float GameProgress => timeLeft < 0f || gameTime <= 0f ? 0f : Mathf.Clamp01(1f - timeLeft / gameTime);

    // ランクごとの出やすさで、山札の何枚目を引くか決める。今引けるランクのカードが無ければ、ふつうにランダム
    int DrawIndexByRank()
    {
        float progress = GameProgress;
        float sum = 0f;
        var weights = new float[deck.Count];
        for (int i = 0; i < deck.Count; i++)
        {
            int r = Mathf.Clamp(productList[deck[i]] != null ? productList[deck[i]].rank : 1, 1, 5) - 1;
            float start = rankStartProgress != null && r < rankStartProgress.Length ? rankStartProgress[r] : 0f;
            float w = rankWeights != null && r < rankWeights.Length ? Mathf.Max(0f, rankWeights[r]) : 1f;
            weights[i] = progress >= start ? w : 0f;
            sum += weights[i];
        }
        if (sum <= 0f) return Random.Range(0, deck.Count);
        float pick = Random.value * sum;
        for (int i = 0; i < deck.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0f && weights[i] > 0f) return i;
        }
        for (int i = deck.Count - 1; i >= 0; i--) if (weights[i] > 0f) return i;   // 計算の誤差で最後まで来たとき
        return Random.Range(0, deck.Count);
    }

    // 棚に置いたカードを覚える(PlaceProduct から)
    void MarkShown(int card)
    {
        if (card >= 0) shownCards.Add(card);
    }

    // まだ一度も棚に出ていない献立の食材のカードを引く(引かないときは -1)
    int DrawPendingMenuCard()
    {
        var menu = currentMenu;
        if (!menuMission || menu == null || menu.ingredients == null || productList == null) return -1;

        int pendingCount = 0;                      // まだ出ていない食材の数
        var candidates = new List<int>();          // そのうち、今山札にあるカード
        foreach (var ing in menu.ingredients)
        {
            bool shown = false;
            var inDeck = new List<int>();
            for (int i = 0; i < productList.Length; i++)
            {
                if (productList[i] == null || productList[i].name != ing) continue;
                if (shownCards.Contains(i)) shown = true;
                if (deck.Contains(i)) inDeck.Add(i);
            }
            if (shown) continue;
            pendingCount++;
            candidates.AddRange(inDeck);
        }
        if (candidates.Count == 0) return -1;

        // 残り時間が少なければ必ず、そうでなければ menuIngredientBoost の確率で引く
        bool urgent = timeLeft >= 0f && timeLeft < (pendingCount + 1) * Mathf.Max(1f, shelfSlideInterval);
        if (!urgent && Random.value >= Mathf.Clamp01(menuIngredientBoost)) return -1;

        int card = candidates[Random.Range(0, candidates.Count)];
        deck.Remove(card);
        return card;
    }

    // カードを山札に戻す
    void ReturnCard(int card)
    {
        if (card >= 0) deck.Add(card);
    }
}
