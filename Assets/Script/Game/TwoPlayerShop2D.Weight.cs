using System.Collections;
using UnityEngine;

// 商品の重さ・袋の重さの表示・重すぎたら袋が破れる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋が破れる」)
//  - 商品の重さ: useTempWeights なら名前から決めた仮の重さ(TempWeight)、OFF なら商品の一覧の Weight
//  - 袋の中(inBag)の商品の重さの合計が bagMaxWeight を超えたら、袋が破れる:
//    底が抜けて中の商品が全部こぼれ、少しして消える(合計から引く)。bagRepairTime 秒で新しい袋(空)に戻る
public partial class TwoPlayerShop2D
{
    // 商品1つの重さ(1が普通)
    float WeightOf(GameObject go)
    {
        if (go == null || !infoOf.TryGetValue(go, out var info)) return 1f;
        return useTempWeights ? TempWeight(info.name) : Mathf.Max(0f, info.weight);
    }

    // 仮の重さ(チームで重さを決めるまでの、名前から決めた適当な値)
    static float TempWeight(string name)
    {
        switch (name)
        {
            // 重い: 飲み物・調味料のびん、大きい魚、かたまりの肉
            case "ぎゅうにゅう": case "おりーぶおいる": case "まぐろ": case "ずわいかに":
                return 3f;
            case "しょうゆ": case "めんつゆ": case "おちゃ": case "こーら": case "りんごじゅーす":
            case "たい": case "ひらめ": case "もどりがつお": case "ぎゅうにく": case "ろーすとびーふ":
                return 2.5f;
            case "じゃがいも": case "さつまいも": case "かるび": case "ろーす": case "いちぼ": case "らむにく":
            case "さんま": case "ふるーつもり": case "とんかつ": case "らむね":
                return 2f;
            case "にんじん": case "ぶろっこりー": case "すぱげてぃ": case "うどん": case "やきそば":
            case "ばなな": case "りんご": case "ぶどう": case "ぽてとさらだ": case "からあげ": case "なまはむ":
                return 1.5f;
            // 軽い: お菓子・小さいもの
            case "たまご": case "ぐみ": case "ぽてち": case "あいすばー": case "ちょこれーと": case "もなか":
            case "うめぼし": case "さくらんぼ": case "きゃびあ": case "なっとう": case "めかぶ": case "むきえだまめ":
            case "だんご":
                return 0.5f;
            default:
                return 1f;
        }
    }

    // 袋の中の商品の重さの合計
    float BagWeight(Player p)
    {
        float sum = 0f;
        foreach (var go in p.inBag) sum += WeightOf(go);
        return sum;
    }

    // CheckBag から毎フレーム: 重さを表示して、重すぎたら破る
    void CheckBagWeight(Player p)
    {
        if (p.bag == null) return;
        float weight = BagWeight(p);
        UpdateWeightLabel(p, weight);
        if (bagTearEnabled && !p.bag.torn && weight > bagMaxWeight)
            StartCoroutine(TearBagRoutine(p, weight));
    }

    void UpdateWeightLabel(Player p, float weight)
    {
        var bag = p.bag;
        if (bag.weightLabel == null)
            bag.weightLabel = MakeText($"BagWeight_{p.name}", bag.root, p.layer, "", Color.white, 1001);
        bool show = showBagWeight && bagTearEnabled;
        bag.weightLabel.gameObject.SetActive(show);
        if (!show) return;

        SetTextSize(bag.weightLabel, HudTextSize * 0.7f);
        // 袋の口のすぐ下(袋の中の上のほう)。袋と一緒に動くように、袋の中の位置で決める
        bag.weightLabel.transform.localPosition = new Vector3(bagX, floorY + bagHeight - HudTextSize * 4f, 0f);
        bag.weightLabel.text = bag.torn ? "やぶれた！" : $"おもさ {weight:0.#} / {bagMaxWeight:0.#}";
        bool warn = bag.torn || weight > bagMaxWeight * bagWeightWarning;
        bag.weightLabel.color = warn ? new Color(1f, 0.3f, 0.25f) : Color.white;
    }

    // 袋が破れる: 底が抜けて中の商品が全部こぼれる → 少しして消える(合計から引く) → 新しい袋に戻る
    IEnumerator TearBagRoutine(Player p, float weight)
    {
        var bag = p.bag;
        bag.torn = true;
        lastEvent = $"{p.name}の袋が破れた！(重さ {weight:0.#} / {bagMaxWeight:0.#})";
        Debug.Log($"[Shop2D] {lastEvent}");
        StartCoroutine(Popup(p.layer, new Vector2(bagX, floorY + bagHeight + 1.4f), "袋が破れた！", new Color(1f, 0.3f, 0.25f), 0.12f));

        // 底を消す(見た目も当たり判定も)。中の商品を起こして落とす
        var bottomCol = bag.bottom.GetComponent<Collider2D>();
        var bottomSr = bag.bottom.GetComponent<SpriteRenderer>();
        bool bottomVisible = bottomSr.enabled;
        bottomCol.enabled = false;
        bottomSr.enabled = false;
        var spilled = new System.Collections.Generic.List<GameObject>(p.inBag);
        p.inBag.Clear();
        int lost = 0;
        foreach (var go in spilled)
        {
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null) rb.WakeUp();
            priceOf.TryGetValue(go, out int price);
            lost += price;
        }
        if (subtractSpilledFromTotal && lost != 0)
        {
            p.total -= lost;
            bag.totalLabel.text = $"¥{p.total}";
            StartCoroutine(MinusPopup(p, lost));
        }

        // 少し落ちるのを見せてから消す
        yield return new WaitForSeconds(0.4f);
        foreach (var go in spilled)
            if (go != null) RemoveProduct(p, go);

        // 新しい袋に戻る(空)
        yield return new WaitForSeconds(Mathf.Max(0f, bagRepairTime - 0.4f));
        bottomCol.enabled = true;
        bottomSr.enabled = bottomVisible;
        bag.torn = false;
        lastEvent = $"{p.name}の袋が新しくなった";
    }
}
