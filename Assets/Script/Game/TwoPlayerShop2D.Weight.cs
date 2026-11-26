using UnityEngine;

// 商品の重さ・最後に持ち上げたとき重すぎたら袋が破れる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋が破れる」)
//  - 商品の重さ: useTempWeights なら名前から決めた仮の重さ(TempWeight)、OFF なら商品の一覧の Weight。重さは画面に出さない
//  - タイムアップのあと袋を持ち上げたとき(上まで持ち上げて bagTearDelay 秒たったところ)、袋の中(inBag)の重さの合計が bagMaxWeight を超えていたら破れる:
//    袋の底だけ破れて中身が下へ落ち、机に散らばる。その人の金額は0円(散らばった商品は残すが、数えない)
//  - bagTearByChance(初め ON)なら、破れるかは確率で決める(持ち上げたときに1回くじ引き RollTear): 擬音の色が 白 5%・黄色 20〜30%・
//    赤(破れる重さを超えた)100%。とがった商品(bagSharpProducts)1つにつき +10%
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

    // 持ち上げたとき破れるか(裂けるアニメーションを始めるかどうか)
    bool WillTearOnLift(Player p)
        => bagTearEnabled && p.bag != null && !p.bag.torn && (bagTearByChance ? RollTear(p) : BagWeight(p) > bagMaxWeight);

    // 袋の重さの段階(擬音の色): 0 = 擬音なし / 1 = 白 / 2 = 黄色 / 3 = 赤(破れる重さを超えた)。k = 擬音の出はじめ〜破れる重さ の 0〜1
    int CreakLevel(Player p, out float k)
    {
        float ratio = BagWeight(p) / Mathf.Max(0.01f, bagMaxWeight);
        float start = Mathf.Clamp(bagCreakStart, 0f, 0.99f);
        k = Mathf.Clamp01((ratio - start) / (1f - start));
        if (ratio > 1f) return 3;
        if (ratio < start) return 0;
        return k >= bagCreakYellowFrom ? 2 : 1;
    }

    // とがった商品の数
    int SharpCount(Player p)
    {
        if (bagSharpProducts == null || bagSharpProducts.Length == 0) return 0;
        int n = 0;
        foreach (var go in p.inBag)
            if (go != null && infoOf.TryGetValue(go, out var info) && System.Array.IndexOf(bagSharpProducts, info.name) >= 0) n++;
        return n;
    }

    // 持ち上げたときに破れる確率(赤 = 100%・黄色 20〜30%・白 5%・擬音なし 0%。とがった商品1つにつき + bagSharpTearBonus)
    float TearChance(Player p)
    {
        int level = CreakLevel(p, out float k);
        if (level == 3) return 1f;
        float chance = level == 2 ? Mathf.Lerp(bagTearChanceYellow.x, bagTearChanceYellow.y, Mathf.InverseLerp(bagCreakYellowFrom, 1f, k))
                     : level == 1 ? bagTearChanceWhite : 0f;
        return Mathf.Clamp01(chance + SharpCount(p) * bagSharpTearBonus);
    }

    // 破れるかを1回だけくじ引きする(持ち上げたとき。同じ試合ではそのあと同じ結果)
    readonly bool[] tearRolled = new bool[2], tearWill = new bool[2];
    bool RollTear(Player p)
    {
        int i = p == p1 ? 0 : 1;
        if (!tearRolled[i])
        {
            tearRolled[i] = true;
            float chance = TearChance(p);
            tearWill[i] = Random.value < chance;
            Debug.Log($"[Shop2D] {p.name}の袋: 破れる確率 {chance * 100f:0}%(重さ {BagWeight(p):0.#} / {bagMaxWeight:0.#}・とがった商品 {SharpCount(p)}個) → {(tearWill[i] ? "破れる" : "耐えた")}");
        }
        return tearWill[i];
    }

    // 最後に袋を持ち上げたとき(TwoPlayerShop2D.Lift.cs): 重すぎたら破れて、中身が散らばり、金額は0円
    // 破れたら true
    bool TryTearBagOnLift(Player p)
    {
        if (!bagTearEnabled || p.bag == null || p.bag.torn) return false;
        float weight = BagWeight(p);
        // 裂けるアニメーションを始めていたら、途中で少しこぼれて軽くなっても破れる(TwoPlayerShop2D.BagModel.cs)
        bool started = p.bag.look != null && p.bag.look.tearStarted;
        bool tear = bagTearByChance ? RollTear(p) : weight > bagMaxWeight;
        if (!tear && !started) return false;

        var bag = p.bag;
        bag.torn = true;
        lastEvent = $"{p.name}の袋が破れた！(重さ {weight:0.#} / {bagMaxWeight:0.#}) → 0円";
        Debug.Log($"[Shop2D] {lastEvent}");

        // 底だけ破れる(今までの底の見た目と当たり判定を消し、代わりに左右2枚の扉にして開く)。左右のふちはそのまま
        var bottomCol = bag.bottom.GetComponent<Collider2D>();
        if (bottomCol != null) bottomCol.enabled = false;
        var bottomSr = bag.bottom.GetComponent<SpriteRenderer>();
        if (bottomSr != null) bottomSr.enabled = false;
        SpawnTearPieces(p);   // ビニールの破片(TwoPlayerShop2D.BagModel.cs)
        StartCoroutine(OpenBagBottom(p));   // 底が真ん中から割れて左右に開く(当たり判定も。TwoPlayerShop2D.BagModel.cs)
        if (bagSnapBack) StartCoroutine(SnapBackBag(p));   // 袋がゴムパッチンのように上へ跳ね戻る(TwoPlayerShop2D.BagModel.cs)

        // 中身を起こす。開いていく底の扉の上を滑って、真ん中から落ちる(少しだけ左右にばらつかせる)
        foreach (var go in p.inBag)
        {
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            if (rb == null) continue;
            rb.WakeUp();
            rb.linearVelocity = new Vector2(Random.Range(-0.3f, 0.3f) * bagTearScatter, -0.5f);
            rb.angularVelocity = Random.Range(-90f, 90f);
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
