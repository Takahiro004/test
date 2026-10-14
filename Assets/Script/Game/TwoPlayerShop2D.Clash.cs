using UnityEngine;

// 掴み合い(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
public partial class TwoPlayerShop2D
{
    // 掴み合い(2人が同じ商品を掴んだとき)
    class Clash
    {
        public GameObject product;
        public float time;                          // 始まってからの秒数
        public readonly float[] value = new float[2];
        public readonly bool[] locked = new bool[2];
        public readonly float[] offset = new float[2];   // ゲージの動き出しを少しずらす
        public Player winner;                       // null のまま結果表示が終わったら引き分け → やり直し
        public float resultTimer;                   // 0より大きい間は勝ち負けを表示中
        public GameObject root;
        public readonly Transform[] fill = new Transform[2];   // ゲージの中身(形で切り抜く)
        public readonly SpriteRenderer[] fillSr = new SpriteRenderer[2];
        public TextMesh title, result;
    }

    Clash clash;
    const float ClashBarWidth = 6.4f, ClashBarHeight = 1.1f, ClashBarEdge = 7.6f;

    // ---------- 掴み合い ----------

    void StartClash(GameObject go)
    {
        go.GetComponent<SpriteRenderer>().color = originalColors[go];

        clash = new Clash { product = go };
        clash.offset[0] = Random.value;
        clash.offset[1] = Random.value;

        clash.root = new GameObject("Clash");
        clash.root.layer = layerShop;
        var t = clash.root.transform;
        float y = clashGaugeY;

        // 背景
        MakeClashRect(t, "Panel", 0f, y + 0.7f, 17.8f, 3.6f, new Color(0f, 0f, 0f, 0.6f), 2000);
        for (int i = 0; i < 2; i++)
        {
            // 1Pは左、2Pは右(左右反転)。どちらも真ん中(VS)に向かって太くなりながら伸びる
            var bar = new GameObject($"Gauge{i + 1}").transform;
            bar.SetParent(t);
            bar.gameObject.layer = layerShop;
            bar.position = new Vector3(i == 0 ? -ClashBarEdge : ClashBarEdge, y, 0f);
            bar.localScale = new Vector3(i == 0 ? 1f : -1f, 1f, 1f);

            // ふち(白) → 中の暗い部分 → 金色(パーフェクトの範囲) → 中身(プレイヤーの色)
            MakeGaugeShape(bar, "Frame", -0.12f, ClashBarWidth + 0.24f, ClashBarHeight + 0.24f, new Color(0.9f, 1f, 1f), 2001);
            MakeGaugeShape(bar, "Back", 0f, ClashBarWidth, ClashBarHeight, new Color(0.12f, 0.12f, 0.15f), 2002);
            var mask = new GameObject("Mask");
            mask.layer = layerShop;
            mask.transform.SetParent(bar, false);
            mask.transform.localScale = new Vector3(ClashBarWidth, ClashBarHeight / GetGaugeSprite().bounds.size.y, 1f);
            mask.AddComponent<SpriteMask>().sprite = GetGaugeSprite();

            float perfectW = ClashBarWidth * (1f - clashPerfect);
            MakeGaugeFill(bar, "Perfect", ClashBarWidth - perfectW, perfectW, new Color(1f, 0.85f, 0.2f, 0.6f), 2003);
            var fillSr = MakeGaugeFill(bar, "Fill", 0f, 0f, i == 0 ? p1.color : p2.color, 2004);
            clash.fill[i] = fillSr.transform;
            clash.fillSr[i] = fillSr;

            float outer = i == 0 ? -ClashBarEdge : ClashBarEdge;
            MakeText($"Name{i + 1}", t, layerShop, i == 0 ? "1P" : "2P", Color.white, 2006).transform.position = new Vector3(outer * 0.75f, y - 0.95f, 0f);
        }
        var vs = MakeText("VS", t, layerShop, "VS", new Color(1f, 0.95f, 0.3f), 2006);
        vs.transform.position = new Vector3(0f, y + 0.1f, 0f);
        vs.characterSize = 0.14f;
        clash.title = MakeText("Title", t, layerShop, "掴み合い！ 最大に近いところでA！", Color.white, 2006);
        clash.title.transform.position = new Vector3(0f, y + 1.9f, 0f);
        clash.result = MakeText("Result", t, layerShop, "", new Color(1f, 0.95f, 0.3f), 2007);
        clash.result.transform.position = new Vector3(0f, y + 1.1f, 0f);
        clash.result.characterSize = 0.12f;

        FitToShopView(clash.root.transform, clashGaugeY);   // 真ん中に出す / 縦の画面でもはみ出さないように縮める

        lastEvent = $"掴み合い！ {go.name}";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // ゲージの形(細い外側から、真ん中に向かって太くなる)。bar の中で x=from から幅 w
    void MakeGaugeShape(Transform bar, string objName, float from, float w, float h, Color color, int order)
    {
        var go = new GameObject(objName);
        go.layer = layerShop;
        go.transform.SetParent(bar, false);
        go.transform.localPosition = new Vector3(from, 0f, 0f);
        go.transform.localScale = new Vector3(w, h / GetGaugeSprite().bounds.size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetGaugeSprite();
        sr.color = color;
        sr.sortingOrder = order;
    }

    // ゲージの中身。四角をゲージの形で切り抜いて表示する
    SpriteRenderer MakeGaugeFill(Transform bar, string objName, float from, float w, Color color, int order)
    {
        var go = new GameObject(objName);
        go.layer = layerShop;
        go.transform.SetParent(bar, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = color;
        sr.sortingOrder = order;
        sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        SetGaugeFill(go.transform, from, w);
        return sr;
    }

    void SetGaugeFill(Transform fill, float from, float w)
    {
        fill.localPosition = new Vector3(from + w / 2f, 0f, 0f);
        fill.localScale = new Vector3(w, ClashBarHeight + 0.2f, 1f);
    }

    Transform MakeClashRect(Transform parent, string objName, float cx, float cy, float w, float h, Color color, int order)
    {
        var tr = MakeBagPart(objName, parent, layerShop, color, order, false);
        SetRect(tr, cx, cy, w, h);
        return tr;
    }

    void UpdateClash(bool press1, bool press2)
    {
        var c = clash;

        // 勝ち負けの表示中
        if (c.resultTimer > 0f)
        {
            c.resultTimer -= Time.deltaTime;
            if (c.resultTimer > 0f) return;
            if (c.winner != null) EndClash();
            else RestartClash();
            return;
        }

        c.time += Time.deltaTime;
        bool[] pressed = { press1, press2 };
        for (int i = 0; i < 2; i++)
        {
            if (c.locked[i]) continue;
            // 0 → 1(最大) → 0 と往復する
            c.value[i] = Mathf.PingPong((c.time + c.offset[i]) * clashGaugeSpeed * 2f, 1f);
            if (pressed[i])
            {
                c.locked[i] = true;
                c.fillSr[i].color = Color.white;   // 止めたら白く光る
            }
        }

        for (int i = 0; i < 2; i++)
            SetGaugeFill(c.fill[i], 0f, ClashBarWidth * c.value[i]);

        // 2人とも押すまで終わらない
        if (!c.locked[0] || !c.locked[1]) return;

        // 2人とも止めた → 最大に近い方の勝ち。表示の数字(0〜100)が同じなら引き分けでやり直し
        int score1 = Mathf.RoundToInt(c.value[0] * 100), score2 = Mathf.RoundToInt(c.value[1] * 100);
        c.title.text = $"1P {score1}  -  {score2} 2P";
        c.resultTimer = clashResultTime;
        if (score1 == score2)
        {
            c.result.text = "引き分け！ もう一回！";
            c.result.color = Color.white;
            lastEvent = $"掴み合い: 引き分け({score1}) → やり直し";
            Debug.Log($"[Shop2D] {lastEvent}");
            return;
        }

        int win = score1 > score2 ? 0 : 1;
        c.winner = win == 0 ? p1 : p2;
        bool perfect = c.value[win] >= clashPerfect;
        c.result.text = $"{c.winner.name}の勝ち！" + (perfect ? " パーフェクト！！" : "");
        c.result.color = c.winner.color;

        lastEvent = $"掴み合い: {c.result.text}(1P {c.value[0]:0.00} / 2P {c.value[1]:0.00})";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // 引き分けのあと: ゲージを0に戻して、もう一度
    void RestartClash()
    {
        var c = clash;
        c.time = 0f;
        for (int i = 0; i < 2; i++)
        {
            c.locked[i] = false;
            c.value[i] = 0f;
            c.offset[i] = Random.value;
            c.fillSr[i].color = i == 0 ? p1.color : p2.color;
            SetGaugeFill(c.fill[i], 0f, 0f);
        }
        c.title.text = "掴み合い！ 最大に近いところでA！";
        c.result.text = "";
    }

    void EndClash()
    {
        var c = clash;
        clash = null;
        Destroy(c.root);
        if (c.product == null) return;
        TakeProduct(c.winner, c.product);
        c.winner.lastTaken = null;   // 決着した商品で、すぐにまた掴み合いにならないように
    }
}
