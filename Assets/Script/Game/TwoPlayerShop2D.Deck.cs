using System.Collections.Generic;
using UnityEngine;

// ランダム補充の山札(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「機能のON/OFF」→ Random Refill)
// トランプと同じ考え方:
//  - 山札 = 商品の一覧の商品1枚ずつ(一覧が50品なら50枚)
//  - 最初に、棚のマスの数だけ山札からランダムに引いて並べる(棚に6個なら、山札は残り44枚)
//  - 商品を取ったら、その商品のカードは山札に戻る。少し待って(Refill Delay)、空いたマスに山札からランダムに1枚引いて置く
//  - 棚が流れて画面の外で左へ回ったときも、その棚の商品を山札に戻して、引き直す
//  - 山札が空のときは、そのマスは空いたまま(次に回ってきたときに引く)
public partial class TwoPlayerShop2D
{
    readonly List<int> deck = new List<int>();   // 山札(商品の一覧の番号)

    // ランダム補充を使うか(商品の一覧が空なら使わない)
    bool UseDeck => randomRefill && productList != null && productList.Length > 0;

    void InitDeck()
    {
        deck.Clear();
        if (productList == null) return;
        for (int i = 0; i < productList.Length; i++) deck.Add(i);
    }

    // 山札からランダムに1枚引く(空なら -1)
    int DrawCard()
    {
        if (deck.Count == 0) return -1;
        int k = Random.Range(0, deck.Count);
        int card = deck[k];
        deck.RemoveAt(k);
        return card;
    }

    // カードを山札に戻す
    void ReturnCard(int card)
    {
        if (card >= 0) deck.Add(card);
    }
}
