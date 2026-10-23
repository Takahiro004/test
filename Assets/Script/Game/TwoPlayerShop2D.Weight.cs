using UnityEngine;

// 商品の重さ・最後に持ち上げたとき重すぎたら袋が破れる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋が破れる」)
//  - 商品の重さ: useTempWeights なら名前から決めた仮の重さ(TempWeight)、OFF なら商品の一覧の Weight。重さは画面に出さない
//  - タイムアップのあと袋を持ち上げたとき(bagTearAt まで上がったところ)、袋の中(inBag)の重さの合計が bagMaxWeight を超えていたら破れる:
//    袋の底だけ破れて中身が下へ落ち、机に散らばる。その人の金額は0円(散らばった商品は残すが、数えない)
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

    // 最後に袋を持ち上げたとき(TwoPlayerShop2D.Lift.cs): 重すぎたら破れて、中身が散らばり、金額は0円
    // 破れたら true
    bool TryTearBagOnLift(Player p)
    {
        if (!bagTearEnabled || p.bag == null || p.bag.torn) return false;
        float weight = BagWeight(p);
        if (weight <= bagMaxWeight) return false;

        var bag = p.bag;
        bag.torn = true;
        lastEvent = $"{p.name}の袋が破れた！(重さ {weight:0.#} / {bagMaxWeight:0.#}) → 0円";
        Debug.Log($"[Shop2D] {lastEvent}");

        // 底だけ破れる(底の見た目と当たり判定を消す)。左右のふちと袋の絵はそのまま
        var bottomCol = bag.bottom.GetComponent<Collider2D>();
        if (bottomCol != null) bottomCol.enabled = false;
        var bottomSr = bag.bottom.GetComponent<SpriteRenderer>();
        if (bottomSr != null) bottomSr.enabled = false;

        // 中身を起こして、底から落とす(少し左右に散らす)
        foreach (var go in p.inBag)
        {
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            if (rb == null) continue;
            rb.WakeUp();
            float side = Mathf.Sign(go.transform.position.x - bagX + Random.Range(-0.3f, 0.3f));
            rb.linearVelocity = new Vector2(side * Random.Range(0.3f, 1f) * bagTearScatter, -1f);
            rb.angularVelocity = Random.Range(-180f, 180f);
        }
        p.inBag.Clear();   // 袋の中のものはもう無い(散らばった商品は机に残る。数えない)

        // 獲得した金額は0円
        int lost = p.total;
        p.total = 0;
        bag.totalLabel.text = "¥0";
        if (lost > 0) StartCoroutine(MinusPopup(p, lost));
        return true;
    }
}
