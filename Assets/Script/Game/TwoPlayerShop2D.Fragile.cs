using System.Collections.Generic;
using UnityEngine;

// 割れる商品(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「割れる商品」と、商品の一覧の Fragile / Break Load / Weight)
// 袋の中の割れる商品(たまご など)の上に、ほかの商品の重さが breakLoad 以上乗った状態が breakHoldTime 秒続いたら、
// 割れて袋から消え、その金額を合計から引く
//  - 重さは「物理の押す力」から計る。上に積んだ商品の重さは、間に何個はさまっていても全部かかる
//  - 単位は「重さ 1 の商品が何個ぶん」(商品ごとの重さは一覧の Weight。初期値は全部 1)
//  - 落ちてきた瞬間の衝撃では割れない(breakHoldTime 秒続いたときだけ)
public partial class TwoPlayerShop2D
{
    readonly Dictionary<GameObject, ProductInfo> infoOf = new Dictionary<GameObject, ProductInfo>();   // 商品ごとの設定
    readonly Dictionary<GameObject, float> overloadTime = new Dictionary<GameObject, float>();          // 重さが超えている時間
    readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[32];

    void CheckFragile(Player p)
    {
        if (!breakFragile) return;
        for (int i = p.inBag.Count - 1; i >= 0; i--)
        {
            var go = p.inBag[i];
            if (go == null || !infoOf.TryGetValue(go, out var info) || !info.fragile) continue;

            float load = LoadOn(go);
            overloadTime.TryGetValue(go, out float time);
            time = load >= info.breakLoad ? time + Time.deltaTime : 0f;
            overloadTime[go] = time;
            if (time < breakHoldTime) continue;

            // 割れた → 袋から消して、金額を引く
            p.inBag.RemoveAt(i);
            overloadTime.Remove(go);
            priceOf.TryGetValue(go, out int price);
            p.total -= price;
            p.bag.totalLabel.text = $"¥{p.total}";
            StartCoroutine(MinusPopup(p, price));
            StartCoroutine(Popup(p.layer, (Vector2)go.transform.position + Vector2.up * 0.5f, "割れた！", new Color(1f, 0.9f, 0.6f), 0.06f));
            RemoveProduct(p, go);
            lastEvent = $"{p.name}の {go.name} が重さで割れた -¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }
    }

    // go の上に乗っている重さ(重さ 1 の商品が何個ぶんか)
    float LoadOn(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) return 0f;
        int n = rb.GetContacts(contactBuffer);
        float impulse = 0f;
        for (int c = 0; c < n; c++)
        {
            var ct = contactBuffer[c];
            // 相手の当たり判定(ct.collider / ct.otherCollider のうち、go のものではない方)
            var other = ct.collider != null && ct.collider.attachedRigidbody == rb ? ct.otherCollider : ct.collider;
            if (other == null || !products.TryGetValue(other, out var otherGo) || otherGo == go) continue;   // 商品どうしだけ(袋のふち・底は数えない)
            if (ct.point.y <= go.transform.position.y) continue;   // 上から押しているものだけ
            impulse += ct.normalImpulse;
        }
        float weightOfOne = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, gravityScale) * Time.fixedDeltaTime;   // 重さ 1 が1回の計算で押す強さ
        return impulse / weightOfOne;
    }
}
