using System.Collections;
using UnityEngine;

// タイムアップのあと、結果発表の前に 1P/2P のレジ袋を持ち上げる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 詰め放題と同じで、持ち上げたときに袋からこぼれた商品は数えない(消えて、その金額を合計から引く)
//  1. bagLiftDelay 秒: 落ちている途中の商品が袋に入るのを待つ(いつもどおり CheckBag で数える)
//  2. bagLiftAnnounceTime 秒: 「袋持ち上げターン！」(1P/2P の画面と棚の画面)
//  3. bagLiftTime 秒: 袋(Kinematic の Rigidbody2D)を MovePosition で上へ動かす。中の商品は底に押されて一緒に上がる
//  4. bagLiftHoldTime 秒: 持ち上げたまま耐える(「耐えろ！ あと3」) → 「耐えた！」 → 合計金額の結果発表へ
//  3〜4 の間、袋の底より下へ落ちた商品は「こぼれた」として消し、合計から引く(Subtract Spilled From Total が ON のとき)
//  袋の中が重すぎたら、上まで持ち上げてから bagTearDelay 秒たったところで袋が破れて中身が散らばり、その人は0円(TwoPlayerShop2D.Weight.cs)
public partial class TwoPlayerShop2D
{
    bool liftStarted, liftDone;
    float liftHeight;   // 今持ち上げている高さ

    // 持ち上げている途中なら true(UpdateResult はそこで止まる)
    bool UpdateBagLift()
    {
        if (liftDone || !bagLiftOnTimeUp || !createBag || p1.bag == null || p2.bag == null) return false;
        if (!liftStarted)
        {
            liftStarted = true;
            StartCoroutine(BagLiftRoutine());
        }
        return true;
    }

    IEnumerator BagLiftRoutine()
    {
        // 途中の掴み合い・キックのゲージは消す
        if (clash != null) { Destroy(clash.root); clash = null; }
        EndKickGauge(p1);
        EndKickGauge(p2);

        // 1. 落ちている途中の商品を待つ
        for (float time = 0f; time < bagLiftDelay; time += Time.deltaTime)
        {
            CheckBag(p1);
            CheckBag(p2);
            yield return null;
        }

        // キックの揺れが残っていたら止めて、袋を元の位置へ
        foreach (var p in new[] { p1, p2 })
        {
            if (p.shakeRoutine != null) { StopCoroutine(p.shakeRoutine); p.shakeRoutine = null; }
            p.bag.body.MovePosition(Vector2.zero);
            foreach (var go in p.inBag)   // 袋の中で止まっている商品を起こす(止まったままだと底に押されない)
            {
                var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
                if (rb != null) rb.WakeUp();
            }
        }

        // 2. 「袋持ち上げターン！」(1P/2P の画面と棚の画面)
        BuildLiftBanner();
        SetLiftBanner("袋持ち上げターン！", new Color(1f, 0.9f, 0.3f));
        lastEvent = "タイムアップ！ 袋持ち上げターン";
        Debug.Log($"[Shop2D] {lastEvent}");
        yield return new WaitForSeconds(Mathf.Max(0f, bagLiftAnnounceTime));

        // 持ち上げる高さ: 画面の高さの bagLiftRatio。袋の上の合計金額が画面の上からはみ出さないところまで
        float viewTop = PlayerViewSize().y / 2f;
        float labelTop = floorY + bagHeight + 0.2f + HudTextSize * 8f;
        float target = Mathf.Clamp(PlayerViewSize().y * bagLiftRatio, 0f, Mathf.Max(0f, viewTop - labelTop));

        // 3. 持ち上げる → 4. 持ち上げたまま耐える(「耐えろ！ あと3」)
        SetLiftBanner("持ち上げろ！", Color.white);
        float total = Mathf.Max(0.01f, bagLiftTime) + Mathf.Max(0f, bagLiftHoldTime);
        int shownCount = -1;
        // 破れるのは、上まで持ち上げてから bagTearDelay 秒たったとき(耐える時間より長ければ、耐える時間の最後)
        float tearTime = Mathf.Min(Mathf.Max(0.01f, bagLiftTime) + Mathf.Max(0f, bagTearDelay), total - Time.fixedDeltaTime);
        for (float time = 0f; time < total; time += Time.fixedDeltaTime)
        {
            float k = Mathf.Clamp01(time / Mathf.Max(0.01f, bagLiftTime));
            liftHeight = target * Mathf.SmoothStep(0f, 1f, k);
            // 重すぎる袋は、上まで持ち上げて少し耐えたところで底が破れる(中身が落ちて0円。TwoPlayerShop2D.Weight.cs)
            if (time >= tearTime)
            {
                if (TryTearBagOnLift(p1)) SetLiftBannerOf(p1, "袋が破れた…", new Color(1f, 0.3f, 0.25f));
                if (TryTearBagOnLift(p2)) SetLiftBannerOf(p2, "袋が破れた…", new Color(1f, 0.3f, 0.25f));
            }
            p1.bag.body.MovePosition(new Vector2(0f, liftHeight));
            p2.bag.body.MovePosition(new Vector2(0f, liftHeight));
            CheckSpill(p1);
            CheckSpill(p2);

            // 持ち上げ終わったら、耐える残りの秒数を数える
            float holdLeft = total - time;
            if (time >= bagLiftTime)
            {
                int count = Mathf.CeilToInt(holdLeft);
                if (count != shownCount)
                {
                    shownCount = count;
                    SetLiftBanner($"耐えろ！ あと{count}", Color.white, true);
                }
            }
            yield return new WaitForFixedUpdate();
        }
        CheckSpill(p1);
        CheckSpill(p2);

        // 5. 耐えた！(破れた人は「袋が破れた…」のまま) → 合計金額の結果発表へ
        SetLiftBanner("耐えた！", new Color(0.5f, 1f, 0.5f), true);
        lastEvent = $"袋を持ち上げた: 1P ¥{p1.total} / 2P ¥{p2.total}";
        Debug.Log($"[Shop2D] {lastEvent}");
        yield return new WaitForSeconds(1f);

        if (liftBanner != null) Destroy(liftBanner);
        liftDone = true;   // 次の UpdateResult から結果発表へ(袋は持ち上げたまま)
    }

    // 「袋持ち上げターン！」などの大きな文字(1P の画面・2P の画面・棚の画面の3つ)
    GameObject liftBanner;
    readonly TextMesh[] liftTexts = new TextMesh[3];   // 0: 1P / 1: 2P / 2: 棚

    void BuildLiftBanner()
    {
        liftBanner = new GameObject("LiftBanner");
        var players = new[] { p1, p2 };
        for (int i = 0; i < 2; i++)
        {
            var tm = MakeText($"LiftText_{players[i].name}", liftBanner.transform, players[i].layer, "", Color.white, 3000);
            SetTextSize(tm, HudTextSize * 1.6f);
            tm.transform.position = new Vector3(bagX, PlayerViewSize().y * 0.3f, 0f);   // 画面の上のほう(袋の上)
            liftTexts[i] = tm;
        }
        Vector2 vis = ShopVisibleSize();
        var shop = MakeText("LiftText_Shop", liftBanner.transform, layerShop, "", Color.white, 3000);
        SetTextSize(shop, 0.12f * Mathf.Min(vis.x, vis.y) / 8f);
        shop.transform.position = Vector3.zero;   // 棚の画面の真ん中
        liftTexts[2] = shop;
    }

    // 3つの画面の文字をまとめて変える。keepTorn なら、袋が破れた人の画面はそのまま
    void SetLiftBanner(string text, Color color, bool keepTorn = false)
    {
        var players = new[] { p1, p2 };
        for (int i = 0; i < 3; i++)
        {
            if (keepTorn && i < 2 && players[i].bag.torn) continue;
            SetLiftText(liftTexts[i], text, color);
        }
    }

    void SetLiftBannerOf(Player p, string text, Color color)
    {
        SetLiftText(liftTexts[p == p1 ? 0 : 1], text, color);
    }

    static void SetLiftText(TextMesh tm, string text, Color color)
    {
        if (tm == null) return;
        tm.text = text;
        tm.color = color;
        // 影も作り直す(文字が変わると影の文字も変える必要がある)
        var old = tm.transform.Find("Shadow");
        if (old != null) Destroy(old.gameObject);
        AddTextShadow(tm);
        var shadow = tm.transform.GetChild(tm.transform.childCount - 1).GetComponent<TextMesh>();
        if (shadow != null) shadow.color = new Color(0f, 0f, 0f, 0.8f);   // 机や棚の上でも読めるように黒い影
    }

    // 持ち上げた袋の底より下へ落ちた商品は、こぼれたとして消し、合計から引く
    void CheckSpill(Player p)
    {
        float bottom = floorY + liftHeight - 0.2f;   // 今の袋の底(少しめり込んでいても袋の中)
        for (int i = p.inBag.Count - 1; i >= 0; i--)
        {
            var go = p.inBag[i];
            if (go == null) { p.inBag.RemoveAt(i); continue; }
            if (go.transform.position.y >= bottom) continue;

            p.inBag.RemoveAt(i);
            priceOf.TryGetValue(go, out int price);
            if (subtractSpilledFromTotal)
            {
                p.total -= price;
                p.bag.totalLabel.text = $"¥{p.total}";
                StartCoroutine(MinusPopup(p, price));
            }
            RemoveProduct(p, go);
            lastEvent = $"{p.name}の {go.name} が持ち上げた袋からこぼれた -¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }
    }
}
